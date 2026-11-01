using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FlexibleArray;

namespace FlexibleArray.Tests.Examples;

// The byte-tail header pair: same layout, different lifetime identity. Borrowed strings
// (getenv) never free; malloc'd strings (strdup) transfer and release with the CRT free that
// allocated them.
[StructLayout(LayoutKind.Sequential)]
public struct ExampleCStrHeader : IFlexibleArrayHeader<ExampleCStrHeader, byte>
{
    public byte FirstElement;

    public static ref byte GetFirstElement(ref ExampleCStrHeader header) => ref header.FirstElement;

    // strlen; termination is guaranteed by the producer — the standard trust model.
    public static int GetTailLength(scoped in ExampleCStrHeader header)
    {
        ref ExampleCStrHeader h = ref Unsafe.AsRef(in header);
        ReadOnlySpan<byte> tail = MemoryMarshal.CreateReadOnlySpan(ref GetFirstElement(ref h), int.MaxValue);
        return tail.IndexOf((byte)0);
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct ExampleMallocCStringHeader
    : IFlexibleArrayHeader<ExampleMallocCStringHeader, byte>,
      ITransferableFlexibleArrayHeader<ExampleMallocCStringHeader, byte>
{
    public byte FirstElement;

    public static ref byte GetFirstElement(ref ExampleMallocCStringHeader header) => ref header.FirstElement;

    public static int GetTailLength(scoped in ExampleMallocCStringHeader header)
    {
        ref ExampleMallocCStringHeader h = ref Unsafe.AsRef(in header);
        ReadOnlySpan<byte> tail = MemoryMarshal.CreateReadOnlySpan(ref GetFirstElement(ref h), int.MaxValue);
        return tail.IndexOf((byte)0);
    }

    public static unsafe void Release(scoped ref ExampleMallocCStringHeader header)
    {
        // The CRT free pairs with the CRT the memory came from. The pointer conversion is the
        // one unavoidable use: Release receives a header by ref.
        ref ExampleMallocCStringHeader h = ref Unsafe.AsRef(in header);
        nint pointer = (nint)Unsafe.AsPointer(ref h);
        _ = OperatingSystem.IsWindows() ? DeclarativePosix.FreeWindows(pointer) : DeclarativePosix.FreePosix(pointer);
    }
}

/// <summary>
/// The library path: borrowed strings arrive as views, malloc'd strings as objects that free
/// on dispose, and fills return views over the caller's own storage.
/// </summary>
internal static partial class DeclarativePosix
{
    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ReadOnlyFlexibleArraySpan<ExampleCStrHeader, byte> GetEnv(string name);

    [LibraryImport("libc", EntryPoint = "strdup", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial FlexibleArray<ExampleMallocCStringHeader, byte> StrDup(string source);

    [LibraryImport("libc", EntryPoint = "getcwd")]
    internal static partial ReadOnlyFlexibleArraySpan<ExampleCStrHeader, byte> GetCwd(Span<byte> buffer);

    [LibraryImport("libc", EntryPoint = "strdup", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint StrDupRaw(string source);

    [LibraryImport("libc", EntryPoint = "free")]
    internal static partial nint FreePosix(nint pointer);

    [LibraryImport("ucrtbase", EntryPoint = "free")]
    internal static partial nint FreeWindows(nint pointer);

    public static string BorrowedEnv(string name)
    {
        ReadOnlyFlexibleArraySpan<ExampleCStrHeader, byte> value = GetEnv(name);
        return value.Length == 0 ? string.Empty : Encoding.ASCII.GetString(value.Elements);
    }

    public static string TransferredCopy(string source)
    {
        using FlexibleArray<ExampleMallocCStringHeader, byte> copy = StrDup(source);
        return Encoding.ASCII.GetString(copy.Span.Elements);
    }

    public static string CurrentDirectory()
    {
        Span<byte> buffer = stackalloc byte[1024];
        return Encoding.ASCII.GetString(GetCwd(buffer).Elements);
    }
}

/// <summary>
/// The same calls without the library: nint returns, Marshal to cross the boundary, and
/// NUL-hunting by hand.
/// </summary>
internal static partial class RawPosixPInvoke
{
    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetEnv(string name);

    [LibraryImport("libc", EntryPoint = "strdup", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint StrDup(string source);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial nint Free(nint pointer);

    [LibraryImport("libc", EntryPoint = "getcwd")]
    private static partial nint GetCwd(Span<byte> buffer);

    public static string BorrowedEnv(string name)
    {
        nint value = GetEnv(name);
        return value == 0 ? string.Empty : Marshal.PtrToStringAnsi(value)!;
    }

    public static string TransferredCopy(string source)
    {
        nint copy = StrDup(source);
        if (copy == 0)
        {
            throw new OutOfMemoryException();
        }

        try
        {
            return Marshal.PtrToStringAnsi(copy)!;
        }
        finally
        {
            _ = Free(copy);
        }
    }

    public static string CurrentDirectory()
    {
        Span<byte> buffer = stackalloc byte[1024];
        nint result = GetCwd(buffer);
        if (result == 0)
        {
            throw new InvalidOperationException("getcwd failed");
        }

        int length = buffer.IndexOf((byte)0);
        return Encoding.ASCII.GetString(buffer[..length]);
    }
}

/// <summary>Side-by-side POSIX demos; the direct-declaration results always match the raw ones.</summary>
public sealed unsafe class PosixExamples
{
    [Fact]
    public void DirectDeclarations_MatchRawPInvoke()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Equal(RawPosixPInvoke.TransferredCopy("flexible!"), DeclarativePosix.TransferredCopy("flexible!"));
        Assert.Equal(RawPosixPInvoke.CurrentDirectory(), DeclarativePosix.CurrentDirectory());
        Assert.Equal(RawPosixPInvoke.BorrowedEnv("HOME"), DeclarativePosix.BorrowedEnv("HOME"));
    }

    [Fact]
    public void Adopt_AcceptsLegacyNintBindings()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        // A pointer from a legacy binding adopts as writable or read-only and releases
        // through the header policy.
        nint raw = DeclarativePosix.StrDupRaw("hello!");
        Assert.NotEqual(0, raw);

        using FlexibleArray<ExampleMallocCStringHeader, byte> array =
            FlexibleArrayTransfer.Adopt<ExampleMallocCStringHeader, byte>((ExampleMallocCStringHeader*)raw);
        Assert.Equal(6, array.Length);
        Assert.Equal("hello!"u8.ToArray(), array.Span.Elements.ToArray());

        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Span; });
    }

    [Fact]
    public void AdoptReadOnly_AcceptsLegacyNintBindings()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        nint raw = DeclarativePosix.StrDupRaw("readonly!");
        Assert.NotEqual(0, raw);

        using ReadOnlyFlexibleArray<ExampleMallocCStringHeader, byte> array =
            FlexibleArrayTransfer.AdoptReadOnly<ExampleMallocCStringHeader, byte>((ExampleMallocCStringHeader*)raw);
        Assert.Equal(9, array.Length);
        Assert.Equal("readonly!"u8.ToArray(), array.ReadOnlySpan.Elements.ToArray());
    }

    [Fact]
    public void Adopt_RejectsNullPointer()
    {
        Assert.Throws<ArgumentNullException>(
            () => { _ = FlexibleArrayTransfer.Adopt<ExampleMallocCStringHeader, byte>(null); });
        Assert.Throws<ArgumentNullException>(
            () => { _ = FlexibleArrayTransfer.AdoptReadOnly<ExampleMallocCStringHeader, byte>(null); });
    }
}
