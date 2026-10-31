using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using FlexibleArray;

namespace FlexibleArray.Tests;

/// <summary>
/// Verifies the default pointer marshaller: declaring the owner directly in a [LibraryImport]
/// signature passes the native side a THeader* to the contiguous object memory.
/// </summary>
public sealed unsafe class FlexibleArrayMarshallerTests
{
    [Fact]
    public void ConvertToUnmanaged_PointsAtHeaderAndTail()
    {
        using var array = FlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 7 }, [5, 6]);

        PacketHeader* pointer = FlexibleArrayMarshaller<PacketHeader, int>.ConvertToUnmanaged(array);

        ref PacketHeader header = ref Unsafe.AsRef<PacketHeader>(pointer);
        Assert.Equal(2, header.Count);
        Assert.Equal(7, header.Timestamp);
        ref byte headerStart = ref Unsafe.As<PacketHeader, byte>(ref header);
        ref byte firstElement = ref Unsafe.As<int, byte>(ref PacketHeader.GetFirstElement(ref header));
        Assert.Equal(16, Unsafe.ByteOffset(ref headerStart, ref firstElement));

        Span<int> tail = MemoryMarshal.CreateSpan(ref PacketHeader.GetFirstElement(ref header), 2);
        Assert.Equal(5, tail[0]);
        Assert.Equal(6, tail[1]);
    }

    [Fact]
    public void DefaultMarshalling_IsWiredThroughNativeMarshalling()
    {
        NativeMarshallingAttribute? nativeMarshalling = typeof(FlexibleArray<PacketHeader, int>)
            .GetCustomAttributes<NativeMarshallingAttribute>()
            .SingleOrDefault();
        Assert.NotNull(nativeMarshalling);
        Assert.True(nativeMarshalling!.NativeType.IsGenericTypeDefinition);
        Assert.Equal(typeof(FlexibleArrayMarshaller<,>), nativeMarshalling.NativeType);

        CustomMarshallerAttribute? inputMode = typeof(FlexibleArrayMarshaller<PacketHeader, int>)
            .GetCustomAttributes<CustomMarshallerAttribute>()
            .SingleOrDefault(a => a.MarshalMode == MarshalMode.ManagedToUnmanagedIn);
        Assert.NotNull(inputMode);
        Assert.True(inputMode!.ManagedType!.IsGenericTypeDefinition);
        Assert.Equal(typeof(FlexibleArray<,>), inputMode.ManagedType);
    }

    [Fact]
    public void ConvertToUnmanaged_ThrowsAfterDispose()
    {
        FlexibleArray<PacketHeader, int> array = FlexibleArray<PacketHeader, int>.Create(default, ReadOnlySpan<int>.Empty);
        array.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => { _ = FlexibleArrayMarshaller<PacketHeader, int>.ConvertToUnmanaged(array); });
    }

    [Fact]
    public void LibraryImport_PassesContiguousObjectBytesToNative()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var array = FlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 12345 }, [10, 20]);

        // Rebuild the exact object bytes: header write first, then the tail.
        Span<byte> expected = new byte[array.ByteLength];
        FlexibleArraySpan<PacketHeader, int> expectedView = FlexibleArraySpan<PacketHeader, int>.Create(expected, 2);
        expectedView.Header = new PacketHeader { Count = 2, Timestamp = 12345 };
        expectedView.Elements[0] = 10;
        expectedView.Elements[1] = 20;

        fixed (byte* right = expected)
        {
            Assert.Equal(0, NativeMemCmp.MemCmp(array, right, (nuint)expected.Length));

            expected[17] ^= 0xFF;
            Assert.NotEqual(0, NativeMemCmp.MemCmp(array, right, (nuint)expected.Length));
        }
    }

    [Fact]
    public void ConvertToManaged_AnchorsViewAtReturnedPointer()
    {
        using var array = FlexibleArray<PacketHeader, int>.Create(new PacketHeader { Count = 2 }, [7, 8]);

        ReadOnlyFlexibleArraySpan<PacketHeader, int> view =
            ReadOnlyFlexibleArraySpanMarshaller<PacketHeader, int>.ConvertToManaged(
                (PacketHeader*)array.DangerousGetPointer());

        Assert.Equal(2, view.Length);
        Assert.Equal(new[] { 7, 8 }, view.Elements.ToArray());
        Assert.Equal(7, view.Header.FirstElement);
    }

    [Fact]
    public void ConvertToManaged_NullPointerYieldsDefaultView()
    {
        Assert.Equal(0, ReadOnlyFlexibleArraySpanMarshaller<PacketHeader, int>.ConvertToManaged(null).Length);
    }

    [Fact]
    public void LibraryImport_DeclarativeReceive_FromReturnedPointer()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        // Set through libc setenv: the managed environment block is not visible to native getenv.
        Assert.Equal(0, NativeEnv.SetEnv("FA_RECV_TEST", "hello!", overwrite: 1));
        try
        {
            ReadOnlyFlexibleArraySpan<CStrHeader, byte> value = NativeEnv.GetEnv("FA_RECV_TEST");
            Assert.Equal(6, value.Length);
            Assert.Equal("hello!"u8.ToArray(), value.Elements.ToArray());
        }
        finally
        {
            NativeEnv.UnsetEnv("FA_RECV_TEST");
        }
    }

    [Fact]
    public void LibraryImport_DeclarativeReceive_NullPointerBecomesDefaultView()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Equal(0, NativeEnv.GetEnv("FA_MISSING_VARIABLE_9F3A").Length);
    }
}

internal static partial class NativeMemCmp
{
    // Declaration-only: the type-level default marshaller applies automatically.
    [LibraryImport("libc", EntryPoint = "memcmp")]
    internal static unsafe partial int MemCmp(FlexibleArray<PacketHeader, int> left, void* right, nuint size);
}

internal static partial class NativeEnv
{
    // Declaration-only receive: native char* getenv(const char*) becomes a complete-object
    // view whose byte tail walks to the terminating NUL.
    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ReadOnlyFlexibleArraySpan<CStrHeader, byte> GetEnv(string name);

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetEnv(string name, string value, int overwrite);

    [LibraryImport("libc", EntryPoint = "unsetenv", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int UnsetEnv(string name);
}
