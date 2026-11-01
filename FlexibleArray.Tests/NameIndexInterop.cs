using System.Runtime.InteropServices;

namespace FlexibleArray.Tests;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct IfNameIndexRecord
{
    public nuint Index;
    public byte* Name;
}

internal static unsafe partial class PosixNative
{
    [LibraryImport("libc", EntryPoint = "if_nameindex")]
    internal static partial IfNameIndexRecord* IfNameIndex();

    [LibraryImport("libc", EntryPoint = "if_freenameindex")]
    internal static partial void IfFreeNameIndex(IfNameIndexRecord* records);

    // Declaration-only fill-and-receive: getcwd writes into the caller's storage and returns
    // the buffer pointer, which becomes the borrowed byte-tail view automatically.
    [LibraryImport("libc", EntryPoint = "getcwd")]
    internal static partial ReadOnlyFlexibleArraySpan<CStrHeader, byte> GetCwd(byte* buffer, nuint size);
}

/// <summary>
/// POSIX validation through if_nameindex and getcwd: a sentinel-terminated array of records
/// released by a paired call, borrowed byte-tail views over OS-owned storage, and the
/// fill-into-caller-buffer pattern.
/// </summary>
public sealed unsafe class NameIndexInteropTests
{
    [Fact]
    public void IfNameIndex_WalksBorrowedNamesAndReleasesWithPairedCall()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        IfNameIndexRecord* records = PosixNative.IfNameIndex();
        Assert.True(records != null);

        // Each record borrows its name from the array's storage; anchor a strlen-tail view
        // per record while the array stays alive.
        List<string> names = [];
        for (IfNameIndexRecord* record = records; record->Index != 0; record++)
        {
            Assert.True(record->Name != null);
            ReadOnlyFlexibleArraySpan<CStrHeader, byte> view = new(*(CStrHeader*)record->Name);
            names.Add(System.Text.Encoding.ASCII.GetString(view.Elements));
        }

        // The loopback interface is "lo" on Linux and "lo0" on macOS.
        Assert.Contains(names, name => name.StartsWith("lo", StringComparison.Ordinal));

        PosixNative.IfFreeNameIndex(records);
    }

    [Fact]
    public void GetCwd_DeclarativeFillAndReceiveOverCallerStorage()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        Span<byte> buffer = stackalloc byte[1024];
        fixed (byte* pointer = buffer)
        {
            // Fill + declaration-only borrow receive in one signature: the returned buffer
            // pointer anchors a view whose strlen tail is derived from the filled content.
            ReadOnlyFlexibleArraySpan<CStrHeader, byte> view = PosixNative.GetCwd(pointer, (nuint)buffer.Length);
            Assert.True(view.Length > 0);
            string current = System.Text.Encoding.ASCII.GetString(view.Elements);
            Assert.Equal(Directory.GetCurrentDirectory(), current);
        }
    }
}
