using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace FlexibleArray.Tests.Examples;

internal static class Netlink
{
    public const int AF_NETLINK = 16;
    public const int SOCK_RAW = 3;
    public const int NETLINK_ROUTE = 0;
    public const int RTM_GETLINK = 18;
    public const int RTM_NEWLINK = 16;
    public const int NLMSG_ERROR = 2;
    public const int NLMSG_DONE = 3;
    public const int NLM_F_REQUEST = 0x1;
    public const int NLM_F_DUMP = 0x300;
    public const int IFLA_IFNAME = 3;
    public const int NLMSG_HDRLEN = 16;
    public const int NLA_HDRLEN = 4;
    public const int IFINFOMSG_SIZE = 16;

    public static int Align4(int length) => (length + 3) & ~3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SockAddrNl
{
    public ushort Family;
    public ushort Pad;
    public uint Pid;
    public uint Groups;
}

// Shared socket transport: plain blittable signatures — structs by in, buffers by spans.
internal static partial class NetlinkSocket
{
    [LibraryImport("libc", EntryPoint = "socket", SetLastError = true)]
    internal static partial int Socket(int domain, int type, int protocol);

    [LibraryImport("libc", EntryPoint = "bind", SetLastError = true)]
    internal static partial int Bind(int fd, in SockAddrNl addr, uint len);

    [LibraryImport("libc", EntryPoint = "sendto", SetLastError = true)]
    internal static partial long SendTo(int fd, ReadOnlySpan<byte> buf, int flags, in SockAddrNl dst, uint dstLen);

    [LibraryImport("libc", EntryPoint = "recvfrom", SetLastError = true)]
    internal static partial long RecvFrom(int fd, Span<byte> buf, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int fd);
}

// The header contracts: nlmsghdr and nlattr are flexible array objects with byte tails whose
// lengths cover their own headers.
[StructLayout(LayoutKind.Sequential)]
internal struct NlmsgHeader : IFlexibleArrayHeader<NlmsgHeader, byte>
{
    public uint Len;
    public ushort Type;
    public ushort Flags;
    public uint Seq;
    public uint Pid;
    public byte FirstElement;

    public static ref byte GetFirstElement(ref NlmsgHeader header) => ref header.FirstElement;

    // nlmsg_len covers the 16-byte header itself; the payload is the byte tail.
    public static int GetTailLength(scoped in NlmsgHeader header) => (int)header.Len - Netlink.NLMSG_HDRLEN;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NlattrHeader : IFlexibleArrayHeader<NlattrHeader, byte>
{
    public ushort Len;
    public ushort Type;
    public byte FirstElement;

    public static ref byte GetFirstElement(ref NlattrHeader header) => ref header.FirstElement;

    // nla_len covers the 4-byte attribute header and excludes trailing alignment padding.
    public static int GetTailLength(scoped in NlattrHeader header) => header.Len - Netlink.NLA_HDRLEN;
}

/// <summary>
/// The library path: each nlmsghdr message and each nlattr record is a complete-object view
/// anchored where it lies — the header contract derives the tail, and header fields are typed
/// members, not offsets.
/// </summary>
internal static class DeclarativeNetlink
{
    public static void BuildDumpRequest(Span<byte> request)
    {
        FlexibleArraySpan<NlmsgHeader, byte> message = FlexibleArraySpan<NlmsgHeader, byte>.Create(
            request, Netlink.IFINFOMSG_SIZE);
        message.Header = new NlmsgHeader
        {
            Len = (uint)request.Length,
            Type = Netlink.RTM_GETLINK,
            Flags = Netlink.NLM_F_REQUEST | Netlink.NLM_F_DUMP,
            Seq = 1,
        };
    }

    public static (List<string> Names, bool Finished, List<int> Errors) WalkLinkNames(ReadOnlySpan<byte> datagram)
    {
        List<string> names = [];
        List<int> errors = [];
        bool finished = false;

        int messageOffset = 0;
        while (messageOffset + Netlink.NLMSG_HDRLEN <= datagram.Length)
        {
            ref readonly NlmsgHeader header = ref Unsafe.As<byte, NlmsgHeader>(
                ref MemoryMarshal.GetReference(datagram[messageOffset..]));
            if (header.Len < Netlink.NLMSG_HDRLEN || messageOffset + header.Len > datagram.Length)
            {
                break;
            }

            // Anchoring constructor: the view derives its tail from the header in place.
            ReadOnlyFlexibleArraySpan<NlmsgHeader, byte> message = new(in header);
            if (header.Type == Netlink.NLMSG_DONE)
            {
                finished = true;
            }
            else if (header.Type == Netlink.NLMSG_ERROR)
            {
                errors.Add(BinaryPrimitives.ReadInt32LittleEndian(message.Elements));
            }
            else if (header.Type == Netlink.RTM_NEWLINK)
            {
                CollectAttributeNames(message.Elements, names);
            }

            messageOffset += Netlink.Align4((int)header.Len);
        }

        return (names, finished, errors);
    }

    private static void CollectAttributeNames(ReadOnlySpan<byte> payload, List<string> names)
    {
        int attributeOffset = Netlink.IFINFOMSG_SIZE;
        while (attributeOffset + Netlink.NLA_HDRLEN <= payload.Length)
        {
            ref readonly NlattrHeader record = ref Unsafe.As<byte, NlattrHeader>(
                ref MemoryMarshal.GetReference(payload[attributeOffset..]));
            if (record.Len < Netlink.NLA_HDRLEN || attributeOffset + record.Len > payload.Length)
            {
                break;
            }

            ReadOnlyFlexibleArraySpan<NlattrHeader, byte> attribute = new(in record);
            if (record.Type == Netlink.IFLA_IFNAME && attribute.Length > 0 && attribute.Elements[^1] == 0)
            {
                names.Add(Encoding.ASCII.GetString(attribute.Elements[..^1]));
            }

            attributeOffset += Netlink.Align4(record.Len);
        }
    }
}

/// <summary>
/// The same walk without the library: hand-computed offsets and endianness reads everywhere,
/// and every header field is a primitive extraction.
/// </summary>
internal static class RawNetlinkPInvoke
{
    public static (List<string> Names, bool Finished, List<int> Errors) WalkLinkNames(ReadOnlySpan<byte> datagram)
    {
        List<string> names = [];
        List<int> errors = [];
        bool finished = false;

        int messageOffset = 0;
        while (messageOffset + Netlink.NLMSG_HDRLEN <= datagram.Length)
        {
            int messageLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(datagram[messageOffset..]);
            ushort messageType = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(messageOffset + 4)..]);
            if (messageLength < Netlink.NLMSG_HDRLEN || messageOffset + messageLength > datagram.Length)
            {
                break;
            }

            ReadOnlySpan<byte> payload = datagram.Slice(messageOffset + Netlink.NLMSG_HDRLEN, messageLength - Netlink.NLMSG_HDRLEN);
            if (messageType == Netlink.NLMSG_DONE)
            {
                finished = true;
            }
            else if (messageType == Netlink.NLMSG_ERROR)
            {
                errors.Add(BinaryPrimitives.ReadInt32LittleEndian(payload));
            }
            else if (messageType == Netlink.RTM_NEWLINK)
            {
                CollectAttributeNames(payload, names);
            }

            messageOffset += Netlink.Align4(messageLength);
        }

        return (names, finished, errors);
    }

    private static void CollectAttributeNames(ReadOnlySpan<byte> payload, List<string> names)
    {
        int attributeOffset = Netlink.IFINFOMSG_SIZE;
        while (attributeOffset + Netlink.NLA_HDRLEN <= payload.Length)
        {
            int attributeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[attributeOffset..]);
            ushort attributeType = BinaryPrimitives.ReadUInt16LittleEndian(payload[(attributeOffset + 2)..]);
            if (attributeLength < Netlink.NLA_HDRLEN || attributeOffset + attributeLength > payload.Length)
            {
                break;
            }

            ReadOnlySpan<byte> value = payload.Slice(attributeOffset + Netlink.NLA_HDRLEN, attributeLength - Netlink.NLA_HDRLEN);
            if (attributeType == Netlink.IFLA_IFNAME && value.Length > 0 && value[^1] == 0)
            {
                names.Add(Encoding.ASCII.GetString(value[..^1]));
            }

            attributeOffset += Netlink.Align4(attributeLength);
        }
    }
}

/// <summary>Side-by-side netlink demos; the two walks always agree on the same datagrams.</summary>
public sealed class NetlinkExamples
{
    [Fact]
    public void DirectDeclaration_MatchesRawWalk_OnSyntheticDump()
    {
        // One RTM_NEWLINK message: nlmsghdr(16) + ifinfomsg(16) + two IFLA_IFNAME attributes
        // with unaligned lengths (7 and 9), so a walk must apply NLA_ALIGN to reach the second
        // one; then NLMSG_DONE: nlmsghdr(16) + error code(4).
        Span<byte> datagram = new byte[72];

        FlexibleArraySpan<NlmsgHeader, byte> message = FlexibleArraySpan<NlmsgHeader, byte>.Create(datagram, 36);
        message.Header = new NlmsgHeader { Len = 52, Type = Netlink.RTM_NEWLINK, Seq = 1 };
        Span<byte> attributes = message.Elements[16..];

        FlexibleArraySpan<NlattrHeader, byte> loopback = FlexibleArraySpan<NlattrHeader, byte>.Create(attributes, 3);
        loopback.Header = new NlattrHeader { Len = 7, Type = Netlink.IFLA_IFNAME };
        "lo\0"u8.CopyTo(loopback.Elements);

        FlexibleArraySpan<NlattrHeader, byte> ethernet = FlexibleArraySpan<NlattrHeader, byte>.Create(attributes[8..], 5);
        ethernet.Header = new NlattrHeader { Len = 9, Type = Netlink.IFLA_IFNAME };
        "eth0\0"u8.CopyTo(ethernet.Elements);

        FlexibleArraySpan<NlmsgHeader, byte> done = FlexibleArraySpan<NlmsgHeader, byte>.Create(datagram[52..], 4);
        done.Header = new NlmsgHeader { Len = 20, Type = Netlink.NLMSG_DONE, Seq = 1 };

        (List<string> declared, bool declaredFinished, List<int> declaredErrors) = DeclarativeNetlink.WalkLinkNames(datagram);
        (List<string> raw, bool rawFinished, List<int> rawErrors) = RawNetlinkPInvoke.WalkLinkNames(datagram);

        Assert.True(declaredFinished);
        Assert.True(rawFinished);
        Assert.Empty(declaredErrors);
        Assert.Empty(rawErrors);
        Assert.Equal(new[] { "lo", "eth0" }, declared);
        Assert.Equal(declared, raw);
    }

    [Fact]
    public void DirectDeclaration_MatchesRawWalk_OnKernelDump()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        int fd = NetlinkSocket.Socket(Netlink.AF_NETLINK, Netlink.SOCK_RAW, Netlink.NETLINK_ROUTE);
        Assert.True(fd >= 0, $"socket() failed with errno {Marshal.GetLastWin32Error()}");
        try
        {
            Assert.Equal(0, NetlinkSocket.Bind(fd, new SockAddrNl { Family = Netlink.AF_NETLINK }, 12));

            Span<byte> request = new byte[32];
            DeclarativeNetlink.BuildDumpRequest(request);
            SockAddrNl kernel = new() { Family = Netlink.AF_NETLINK };
            long sent = NetlinkSocket.SendTo(fd, request, 0, kernel, (uint)Marshal.SizeOf<SockAddrNl>());
            Assert.True(sent == request.Length, $"sendto() failed with errno {Marshal.GetLastWin32Error()}");

            Span<byte> response = new byte[65536];
            List<string> declaredNames = [];
            List<string> rawNames = [];
            bool finished = false;
            for (int round = 0; round < 16 && !finished; round++)
            {
                long received = NetlinkSocket.RecvFrom(fd, response, 0);
                Assert.True(received > 0, $"recvfrom() failed with errno {Marshal.GetLastWin32Error()}");

                ReadOnlySpan<byte> datagram = response[..(int)received];
                (List<string> names, bool done, List<int> errors) = DeclarativeNetlink.WalkLinkNames(datagram);
                Assert.Empty(errors);
                declaredNames.AddRange(names);

                (List<string> rawWalk, _, List<int> rawErrors) = RawNetlinkPInvoke.WalkLinkNames(datagram);
                Assert.Empty(rawErrors);
                Assert.Equal(names, rawWalk);
                rawNames.AddRange(rawWalk);

                finished = done;
            }

            Assert.True(finished);
            Assert.Contains("lo", declaredNames);
            Assert.Equal(declaredNames, rawNames);
        }
        finally
        {
            NetlinkSocket.Close(fd);
        }
    }
}
