using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray.Tests.Examples;

// The header contract, declared once: first-element placeholder, count-driven tail, and the
// paired release this protocol registers. No unsafe code needed for the contract itself —
// six plain byte fields reproduce the 6-byte IdentifierAuthority.
[StructLayout(LayoutKind.Sequential)]
public struct SidHeader
    : IFlexibleArrayHeader<SidHeader, uint>, ITransferableFlexibleArrayHeader<SidHeader, uint>
{
    public byte Revision;
    public byte SubAuthorityCount;
    public byte Authority0;
    public byte Authority1;
    public byte Authority2;
    public byte Authority3;
    public byte Authority4;
    public byte Authority5;
    public uint FirstElement;

    public static ref uint GetFirstElement(ref SidHeader header) => ref header.FirstElement;

    // The count field IS the tail length.
    public static int GetTailElementCount(scoped in SidHeader header) => header.SubAuthorityCount;

    public static unsafe void Release(scoped ref SidHeader header)
    {
        // The only unavoidable pointer: Release receives a header by ref, and the native
        // contract takes an address.
        _ = DeclarativeSid.FreeSid((nint)Unsafe.AsPointer(ref header));
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct SidIdentifierAuthority
{
    public byte B0;
    public byte B1;
    public byte B2;
    public byte B3;
    public byte B4;
    public byte B5;

    public static SidIdentifierAuthority NtAuthority => new() { B5 = 5 };
}

/// <summary>
/// The library path: [LibraryImport] signatures state the intent — an SID arrives as a
/// flexible array object (directly, or adopted from a legacy nint binding) and disposal
/// frees it. No marshalling code on the call site.
/// </summary>
internal static partial class DeclarativeSid
{
    internal const int SubAuthorityOffset = 8;

    [LibraryImport("advapi32", EntryPoint = "AllocateAndInitializeSid")]
    internal static partial int AllocateAndInitializeSid(
        in SidIdentifierAuthority authority,
        byte subAuthorityCount,
        uint subAuthority0,
        uint subAuthority1,
        uint subAuthority2,
        uint subAuthority3,
        uint subAuthority4,
        uint subAuthority5,
        uint subAuthority6,
        uint subAuthority7,
        out MutableFlexibleArray<SidHeader, uint>? sid);

    [LibraryImport("advapi32", EntryPoint = "FreeSid")]
    internal static partial nint FreeSid(nint sid);

    // The OS's own view of the tail, for layout comparison.
    [LibraryImport("advapi32", EntryPoint = "GetSidSubAuthority")]
    internal static partial nint GetSidSubAuthority(nint sid, int index);

    public static uint[] ReadSubAuthorities(in SidIdentifierAuthority authority, byte count)
    {
        if (AllocateAndInitializeSid(
                authority, count, 0x11, 0x22, 0x33, 0, 0, 0, 0, 0, out var sid) == 0)
        {
            throw new InvalidOperationException("AllocateAndInitializeSid failed");
        }

        using (sid)
        {
            return [.. sid!.Span.Elements];
        }
    }
}

/// <summary>
/// The same calls the way they look without the library: nint handles, Marshal reads byte by
/// byte at hand-computed offsets, and the release call tracked manually.
/// </summary>
internal static partial class RawSidPInvoke
{
    [LibraryImport("advapi32", EntryPoint = "AllocateAndInitializeSid")]
    internal static partial int AllocateAndInitializeSid(
        in SidIdentifierAuthority authority,
        byte subAuthorityCount,
        uint subAuthority0,
        uint subAuthority1,
        uint subAuthority2,
        uint subAuthority3,
        uint subAuthority4,
        uint subAuthority5,
        uint subAuthority6,
        uint subAuthority7,
        out nint sid);

    [LibraryImport("advapi32", EntryPoint = "FreeSid")]
    internal static partial nint FreeSid(nint sid);

    [LibraryImport("advapi32", EntryPoint = "GetSidSubAuthority")]
    internal static partial nint GetSidSubAuthority(nint sid, int index);

    public static uint[] ReadSubAuthorities(in SidIdentifierAuthority authority, byte count)
    {
        if (AllocateAndInitializeSid(
                authority, count, 0x11, 0x22, 0x33, 0, 0, 0, 0, 0, out var sid) == 0)
        {
            throw new InvalidOperationException("AllocateAndInitializeSid failed");
        }

        try
        {
            // Hand-computed geometry: count lives at offset 1, sub authorities at offset 8.
            int actual = Marshal.ReadByte(sid, 1);
            var subAuthorities = new uint[actual];
            for (var i = 0; i < actual; i++)
            {
                subAuthorities[i] = unchecked((uint)Marshal.ReadInt32(sid, 8 + 4 * i));
            }

            return subAuthorities;
        }
        finally
        {
            _ = FreeSid(sid);
        }
    }
}

/// <summary>Side-by-side SID demos; the direct-declaration results always match the raw ones.</summary>
public sealed class SidExamples
{
    [Fact]
    public void DirectDeclaration_MatchesRawPInvoke()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var authority = SidIdentifierAuthority.NtAuthority;
        Assert.Equal(RawSidPInvoke.ReadSubAuthorities(authority, 3), DeclarativeSid.ReadSubAuthorities(authority, 3));
    }

    [Fact]
    public unsafe void DirectDeclaration_LayoutMatchesTheOsView()
    {
        Assert.Equal(DeclarativeSid.SubAuthorityOffset,
            (int)FlexibleArrayLayout<SidHeader, uint>.FirstElementOffset);
        Assert.Equal(12, FlexibleArrayLayout<SidHeader, uint>.GetRequiredBytes(1));

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var authority = SidIdentifierAuthority.NtAuthority;
        if (DeclarativeSid.AllocateAndInitializeSid(
                authority, 2, 11, 22, 0, 0, 0, 0, 0, 0, out var sid) == 0)
        {
            throw new InvalidOperationException("AllocateAndInitializeSid failed");
        }

        using (sid)
        {
            using var pinned = sid!.Pin();

            // The OS agrees with our geometry: its tail pointer equals header + our offset,
            // and the stride arithmetic lands the second element where it should.
            Assert.Equal(DeclarativeSid.GetSidSubAuthority((nint)pinned.Pointer, 0),
                (nint)pinned.Pointer + DeclarativeSid.SubAuthorityOffset);
            Assert.Equal(DeclarativeSid.GetSidSubAuthority((nint)pinned.Pointer, 1),
                (nint)pinned.Pointer + DeclarativeSid.SubAuthorityOffset + sizeof(uint));
            Assert.Equal([11u, 22u], sid.Span.Elements);
        }
    }

    [Fact]
    public unsafe void Adopt_AcceptsLegacyNintBindings()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Pointers that arrive outside a declarative signature (legacy DllImport shapes)
        // still adopt and release through the header policy.
        var authority = SidIdentifierAuthority.NtAuthority;
        Assert.NotEqual(0, RawSidPInvoke.AllocateAndInitializeSid(
            authority, 1, 7, 0, 0, 0, 0, 0, 0, 0, out var raw));

        using var sid = FlexibleArrayTransfer.Adopt<SidHeader, uint>((SidHeader*)raw);
        Assert.Equal(1, sid.Length);
        Assert.Equal(7u, sid.Span.Elements[0]);
    }
}
