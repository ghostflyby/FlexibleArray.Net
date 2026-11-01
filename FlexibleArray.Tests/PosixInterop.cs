using System.Runtime.InteropServices;

namespace FlexibleArray.Tests;

internal static unsafe partial class PosixNative
{
    // Declaration-only fill-and-receive: getcwd writes into the caller's storage and returns
    // the buffer pointer, which becomes the borrowed byte-tail view automatically.
    [LibraryImport("libc", EntryPoint = "getcwd")]
    internal static partial ReadOnlyFlexibleArraySpan<CStrHeader, byte> GetCwd(byte* buffer, nuint size);
}

/// <summary>
/// POSIX fill-into-caller-storage validation: the declaration-only receive anchors a
/// borrowed strlen-tail view over storage the caller owns and just filled.
/// </summary>
public sealed unsafe class PosixInteropTests
{
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
