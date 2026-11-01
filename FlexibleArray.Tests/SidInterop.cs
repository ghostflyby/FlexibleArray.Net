using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray.Tests;

// Windows SID UAPI (winnt.h): the canonical flexible array member with a paired release.
// typedef struct _SID {
//     BYTE  Revision;                 // offset 0
//     BYTE  SubAuthorityCount;        // offset 1 — the count field IS the tail length
//     SID_IDENTIFIER_AUTHORITY IdentifierAuthority;  // 6 bytes, offsets 2..7
//     DWORD SubAuthority[ANYSIZE_ARRAY];             // offset 8
// } SID;                             // sizeof = 8 + 4 * SubAuthorityCount
internal static class Sid
{
    public static readonly byte[] NtAuthority = [0, 0, 0, 0, 0, 5];
    public const int SubAuthorityOffset = 8;
}

/// <summary>
/// A Windows SID header: revision, count, 6-byte authority, then the SubAuthority tail. The
/// tail length is the header's own count field; the release is the paired FreeSid.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SidHeader
    : IFlexibleArrayHeader<SidHeader, uint>, ITransferableFlexibleArrayHeader<SidHeader, uint>
{
    public byte Revision;
    public byte SubAuthorityCount;
    private fixed byte _authority[6];
    public uint FirstElement;

    public static ref uint GetFirstElement(ref SidHeader header) => ref header.FirstElement;

    public static int GetTailLength(scoped in SidHeader header) => header.SubAuthorityCount;

    public static void Release(scoped ref SidHeader header)
    {
        ref SidHeader h = ref Unsafe.AsRef(in header);
        _ = Advapi32.FreeSid((void*)Unsafe.AsPointer(ref h));
    }
}

internal static partial class Advapi32
{
    [LibraryImport("advapi32", EntryPoint = "AllocateAndInitializeSid")]
    internal static unsafe partial int AllocateAndInitializeSid(
        byte* authority,
        byte subAuthorityCount,
        uint subAuthority0,
        uint subAuthority1,
        uint subAuthority2,
        uint subAuthority3,
        uint subAuthority4,
        uint subAuthority5,
        uint subAuthority6,
        uint subAuthority7,
        out byte* sid);

    // PVOID FreeSid(PSID pSid).
    [LibraryImport("advapi32", EntryPoint = "FreeSid")]
    internal static unsafe partial void* FreeSid(void* sid);

    // The OS's own view of the tail: GetSidSubAuthority(pSid, 0) must be the placeholder.
    [LibraryImport("advapi32", EntryPoint = "GetSidSubAuthority")]
    internal static unsafe partial uint* GetSidSubAuthority(void* sid, int index);

    [LibraryImport("advapi32", EntryPoint = "GetSidLengthSid")]
    internal static unsafe partial uint GetSidLengthSid(void* sid);

    // Experiment: AllocateAndInitializeSid hands the PSID through an out parameter (the
    // return value is BOOL). If the generator supports custom marshallers on out-parameter
    // positions, the whole flow becomes declarative.
    [LibraryImport("advapi32", EntryPoint = "AllocateAndInitializeSid")]
    internal static unsafe partial int AllocateAndInitializeSidDeclarative(
        byte* authority,
        byte subAuthorityCount,
        uint subAuthority0,
        uint subAuthority1,
        uint subAuthority2,
        uint subAuthority3,
        uint subAuthority4,
        uint subAuthority5,
        uint subAuthority6,
        uint subAuthority7,
        out FlexibleArray<SidHeader, uint> sid);
}

/// <summary>
/// Windows interop validation through the SID family: layout fidelity against the OS's own
/// accessors, manual Adopt with FreeSid release, and declarative transfer receive through
/// the CRT's _strdup/Free pair.
/// </summary>
public sealed unsafe class SidInteropTests
{
    [Fact]
    public void DeclarativeReceive_LayoutMatchesTheOsViewOfSid()
    {
        Assert.Equal(Sid.SubAuthorityOffset, (int)FlexibleArrayLayout<SidHeader, uint>.FirstElementOffset);
        Assert.Equal(12, FlexibleArrayLayout<SidHeader, uint>.GetRequiredBytes(1));

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        fixed (byte* authority = Sid.NtAuthority)
        {
            // Declarative path under test; the raw-pointer accessors below are the OS truth
            // the layout is checked against, not an alternate way to obtain the object.
            Assert.NotEqual(0, Advapi32.AllocateAndInitializeSidDeclarative(
                authority, 2, 11, 22, 0, 0, 0, 0, 0, 0, out FlexibleArray<SidHeader, uint>? adopted));
            using (adopted)
            {
                Assert.NotNull(adopted);
                using MemoryHandle pinned = adopted!.Pin();
                byte* sid = (byte*)pinned.Pointer;

                // The OS agrees with our geometry: its tail pointer equals header + our
                // offset, the count field drives the length, and the total size matches.
                Assert.Equal((nint)Advapi32.GetSidSubAuthority(sid, 0), (nint)(sid + Sid.SubAuthorityOffset));
                Assert.Equal(2, adopted.Length);
                Assert.Equal(8 + 2 * sizeof(uint), (int)Advapi32.GetSidLengthSid(sid));
                Assert.Equal(11u, adopted.Span.Elements[0]);
                Assert.Equal(22u, adopted.Span.Elements[1]);
            }
        }
    }

    [Fact]
    public void Adopt_ManualEntryPoint_AcceptsRawPointersAndReleases()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Adopt is a public API of its own: pointers that arrive outside a LibraryImport
        // signature (legacy DllImport bindings, out byte* shapes) still adopt and release.
        fixed (byte* authority = Sid.NtAuthority)
        {
            Assert.NotEqual(0, Advapi32.AllocateAndInitializeSid(
                authority, 1, 7, 0, 0, 0, 0, 0, 0, 0, out byte* sid));

            using FlexibleArray<SidHeader, uint> array = FlexibleArrayTransfer.Adopt<SidHeader, uint>((SidHeader*)sid);
            Assert.Equal(1, array.Length);
            Assert.Equal(7u, array.Span.Elements[0]);
        }
    }

    [Fact]
    public void DeclarativeOutReceive_AdoptsAndFreesThroughHeader()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        fixed (byte* authority = Sid.NtAuthority)
        {
            // Fully declarative: the PSID arrives through an out parameter and the
            // constrained marshaller adopts it; disposal frees through FreeSid.
            Assert.NotEqual(0, Advapi32.AllocateAndInitializeSidDeclarative(
                authority, 3, 1, 2, 3, 0, 0, 0, 0, 0, out FlexibleArray<SidHeader, uint>? sid));
            using (sid)
            {
                Assert.NotNull(sid);
                Assert.Equal(3, sid!.Length);
                Assert.Equal(new uint[] { 1, 2, 3 }, sid.Span.Elements.ToArray());
            }

            Assert.Throws<ObjectDisposedException>(() => { _ = sid!.Span; });
        }
    }

    [Fact]
    public void DeclarativeTransferReceive_AutoAdoptsAndFrees()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using FlexibleArray<MallocCStringHeader, byte> array = NativeStrDupAnyOs.StrDupView("windows!");

        Assert.Equal(8, array.Length);
        Assert.Equal("windows!"u8.ToArray(), array.Span.Elements.ToArray());

        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Span; });
    }
}

internal static partial class NativeStrDupAnyOs
{
    // The CRT strdup returns a malloc'd string on every OS: libc on POSIX, ucrtbase on
    // Windows — a real transfer returned directly by the native signature.
    [LibraryImport("libc", EntryPoint = "strdup", StringMarshalling = StringMarshalling.Utf8)]
    private static partial FlexibleArray<MallocCStringHeader, byte> StrDupViewPosix(string source);

    [LibraryImport("ucrtbase", EntryPoint = "_strdup", StringMarshalling = StringMarshalling.Utf8)]
    private static partial FlexibleArray<MallocCStringHeader, byte> StrDupViewWindows(string source);

    internal static FlexibleArray<MallocCStringHeader, byte> StrDupView(string source)
        => OperatingSystem.IsWindows() ? StrDupViewWindows(source) : StrDupViewPosix(source);
}
