using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace FlexibleArray.Tests;

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
}

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

[StructLayout(LayoutKind.Sequential)]
internal struct SockAddrNl
{
    public ushort Family;
    public ushort Pad;
    public uint Pid;
    public uint Groups;
}

internal static partial class LinuxInterop
{
    [LibraryImport("libc", EntryPoint = "socket", SetLastError = true)]
    internal static partial int Socket(int domain, int type, int protocol);

    [LibraryImport("libc", EntryPoint = "bind", SetLastError = true)]
    internal static unsafe partial int Bind(int fd, SockAddrNl* addr, uint len);

    [LibraryImport("libc", EntryPoint = "sendto", SetLastError = true)]
    internal static unsafe partial long SendTo(int fd, byte* buf, nuint len, int flags, SockAddrNl* dst, uint dstLen);

    [LibraryImport("libc", EntryPoint = "recvfrom", SetLastError = true)]
    internal static unsafe partial long RecvFrom(int fd, byte* buf, nuint len, int flags, void* src, uint* srcLen);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int fd);
}

/// <summary>
/// Walks real netlink framing with anchored complete-object views: each nlmsghdr message and
/// each nlattr record is a flexible array object with a byte tail.
/// </summary>
public sealed unsafe class NetlinkTests
{
    [Fact]
    public void NetlinkWalk_ParsesSyntheticDump()
    {
        // One RTM_NEWLINK message: nlmsghdr(16) + ifinfomsg(16) + two IFLA_IFNAME attributes
        // with unaligned lengths (7 and 9), so the walk must apply NLA_ALIGN to reach the
        // second one; then NLMSG_DONE: nlmsghdr(16) + error code(4).
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

        List<string> names = [];
        List<int> errors = [];
        bool finished = CollectLinkNames(datagram, names, errors);

        Assert.True(finished);
        Assert.Empty(errors);
        Assert.Equal(new[] { "lo", "eth0" }, names);
    }

    [Fact]
    public void NetlinkRouteDump_ParsesLinkNamesFromKernel()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        int fd = LinuxInterop.Socket(Netlink.AF_NETLINK, Netlink.SOCK_RAW, Netlink.NETLINK_ROUTE);
        Assert.True(fd >= 0, $"socket() failed with errno {Marshal.GetLastWin32Error()}");
        try
        {
            SockAddrNl local = new() { Family = Netlink.AF_NETLINK };
            Assert.Equal(0, LinuxInterop.Bind(fd, &local, (uint)sizeof(SockAddrNl)));

            // Dump request: nlmsghdr + zeroed ifinfomsg, built through a flexible array view.
            Span<byte> request = new byte[32];
            FlexibleArraySpan<NlmsgHeader, byte> req = FlexibleArraySpan<NlmsgHeader, byte>.Create(request, Netlink.IFINFOMSG_SIZE);
            req.Header = new NlmsgHeader { Len = 32, Type = Netlink.RTM_GETLINK, Flags = Netlink.NLM_F_REQUEST | Netlink.NLM_F_DUMP, Seq = 1 };

            fixed (byte* requestPtr = request)
            {
                SockAddrNl kernel = new() { Family = Netlink.AF_NETLINK };
                long sent = LinuxInterop.SendTo(fd, requestPtr, (nuint)request.Length, 0, &kernel, (uint)sizeof(SockAddrNl));
                Assert.True(sent == request.Length, $"sendto() failed with errno {Marshal.GetLastWin32Error()}");
            }

            List<string> names = [];
            List<int> errors = [];
            bool finished = false;
            Span<byte> response = new byte[65536];
            for (int round = 0; round < 16 && !finished && errors.Count == 0; round++)
            {
                long received;
                fixed (byte* responsePtr = response)
                {
                    received = LinuxInterop.RecvFrom(fd, responsePtr, (nuint)response.Length, 0, null, null);
                }

                Assert.True(received > 0, $"recvfrom() failed with errno {Marshal.GetLastWin32Error()}");
                finished = CollectLinkNames(response[..(int)received], names, errors);
            }

            Assert.True(finished);
            Assert.Empty(errors);
            Assert.Contains("lo", names);
        }
        finally
        {
            LinuxInterop.Close(fd);
        }
    }

    // Collects IFLA_IFNAME values from a netlink datagram; returns true once NLMSG_DONE is
    // seen. NLMSG_ERROR records the (negative) errno so the caller can fail fast instead of
    // waiting for further datagrams that will never arrive.
    private static bool CollectLinkNames(ReadOnlySpan<byte> datagram, List<string> names, List<int> errors)
    {
        bool finished = false;
        int messageOffset = 0;
        while (messageOffset + Netlink.NLMSG_HDRLEN <= datagram.Length)
        {
            ref NlmsgHeader header = ref Unsafe.As<byte, NlmsgHeader>(ref MemoryMarshal.GetReference(datagram[messageOffset..]));
            if (header.Len < Netlink.NLMSG_HDRLEN || messageOffset + header.Len > datagram.Length)
            {
                break;
            }

            ReadOnlyFlexibleArraySpan<NlmsgHeader, byte> message = new(in header);
            if (header.Type == Netlink.NLMSG_DONE)
            {
                finished = true;
            }
            else if (header.Type == Netlink.NLMSG_ERROR)
            {
                errors.Add(BitConverter.ToInt32(message.Elements));
            }
            else if (header.Type == Netlink.RTM_NEWLINK)
            {
                CollectAttributeNames(message.Elements, names);
            }

            messageOffset += Align4((int)header.Len);
        }

        return finished;
    }

    // Walks the nlattr records that follow the fixed ifinfomsg part of an RTM_NEWLINK payload.
    private static void CollectAttributeNames(ReadOnlySpan<byte> payload, List<string> names)
    {
        int attributeOffset = Netlink.IFINFOMSG_SIZE;
        while (attributeOffset + Netlink.NLA_HDRLEN <= payload.Length)
        {
            ref NlattrHeader attribute = ref Unsafe.As<byte, NlattrHeader>(ref MemoryMarshal.GetReference(payload[attributeOffset..]));
            if (attribute.Len < Netlink.NLA_HDRLEN || attributeOffset + attribute.Len > payload.Length)
            {
                break;
            }

            ReadOnlyFlexibleArraySpan<NlattrHeader, byte> value = new(in attribute);
            if (attribute.Type == Netlink.IFLA_IFNAME && value.Length > 0 && value.Elements[^1] == 0)
            {
                names.Add(Encoding.ASCII.GetString(value.Elements[..^1]));
            }

            attributeOffset += Align4(attribute.Len);
        }
    }

    private static int Align4(int length) => (length + 3) & ~3;
}
