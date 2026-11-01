using System.Runtime.InteropServices;

namespace FlexibleArray.Tests;

internal static partial class NativeStdLib
{
    [LibraryImport("libc", EntryPoint = "strdup", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial byte* StrDup(string source);

    [LibraryImport("libc", EntryPoint = "free")]
    internal static unsafe partial void Free(void* pointer);
}

/// <summary>
/// Verifies the transfer-of-ownership lifecycle: adopted native objects are flexible array
/// objects that dispose — and are declared, received, and released — through the
/// header-registered policy.
/// </summary>
public sealed unsafe class FlexibleArrayTransferTests
{
    [Fact]
    public void Adopt_HoldsTransferredObjectAndReleasesThroughHeaderPolicy()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        byte* pointer = NativeStdLib.StrDup("hello!");
        Assert.True(pointer != null);

        using FlexibleArray<MallocCStringHeader, byte> array =
            FlexibleArrayTransfer.Adopt<MallocCStringHeader, byte>((MallocCStringHeader*)pointer);

        Assert.Equal(6, array.Length);
        Assert.Equal("hello!"u8.ToArray(), array.Span.Elements.ToArray());

        // Disposal routes through MallocCStringHeader.Release (free); the object dies with it.
        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Span; });
    }

    [Fact]
    public void AdoptReadOnly_HoldsTransferredObjectReadonly()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        byte* pointer = NativeStdLib.StrDup("readonly!");
        Assert.True(pointer != null);

        using ReadOnlyFlexibleArray<MallocCStringHeader, byte> array =
            FlexibleArrayTransfer.AdoptReadOnly<MallocCStringHeader, byte>((MallocCStringHeader*)pointer);

        Assert.Equal(9, array.Length);
        Assert.Equal("readonly!"u8.ToArray(), array.ReadOnlySpan.Elements.ToArray());

        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = array.ReadOnlySpan; });
    }

    [Fact]
    public void LibraryImport_DeclarativeTransferReceive_AutoAdopts()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using FlexibleArray<MallocCStringHeader, byte> array = NativeStrDup.StrDupView("declarative!");

        Assert.Equal(12, array.Length);
        Assert.Equal("declarative!"u8.ToArray(), array.Span.Elements.ToArray());

        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Span; });
    }

    [Fact]
    public void LibraryImport_DeclarativeTransferReceive_ReadOnly()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using ReadOnlyFlexibleArray<MallocCStringHeader, byte> array = NativeStrDup.StrDupViewReadOnly("ro!");

        Assert.Equal(3, array.Length);
        Assert.Equal("ro!"u8.ToArray(), array.ReadOnlySpan.Elements.ToArray());
    }

    [Fact]
    public void Adopt_RejectsNullPointer()
    {
        Assert.Throws<ArgumentNullException>(
            () => { _ = FlexibleArrayTransfer.Adopt<MallocCStringHeader, byte>(null); });
        Assert.Throws<ArgumentNullException>(
            () => { _ = FlexibleArrayTransfer.AdoptReadOnly<MallocCStringHeader, byte>(null); });
    }
}

internal static partial class NativeStrDup
{
    // Declaration-only transfer receive: the constrained marshallers adopt the returned
    // pointer automatically, so the object carries the whole lifecycle.
    [LibraryImport("libc", EntryPoint = "strdup", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial FlexibleArray<MallocCStringHeader, byte> StrDupView(string source);

    [LibraryImport("libc", EntryPoint = "strdup", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ReadOnlyFlexibleArray<MallocCStringHeader, byte> StrDupViewReadOnly(string source);
}
