using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using FlexibleArray;

namespace FlexibleArray.Tests;

/// <summary>
/// Verifies the default pointer marshaller: declaring FlexibleArrayMemory directly in a
/// [LibraryImport] signature passes the native side a pinned THeader* to the contiguous
/// object memory.
/// </summary>
public sealed unsafe class FlexibleArrayMarshallerTests
{
    [Fact]
    public void Marshaller_PinsHeaderAndTailForTheCall()
    {
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 7 }, [5, 6]);

        FlexibleArrayMarshaller<PacketHeader, int> marshaller = default;
        marshaller.FromManaged(memory);
        try
        {
            PacketHeader* pointer = marshaller.ToUnmanaged();

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
        finally
        {
            marshaller.Free();
        }
    }

    [Fact]
    public void FromManaged_RejectsDefaultObject()
    {
        FlexibleArrayMarshaller<PacketHeader, int> marshaller = default;
        Assert.Throws<InvalidOperationException>(() => marshaller.FromManaged(default));
    }

    [Fact]
    public void DefaultMarshalling_IsWiredThroughNativeMarshalling()
    {
        NativeMarshallingAttribute? nativeMarshalling = typeof(FlexibleArrayMemory<PacketHeader, int>)
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
        Assert.Equal(typeof(FlexibleArrayMemory<,>), inputMode.ManagedType);
    }

    [Fact]
    public void LibraryImport_PassesContiguousObjectBytesToNative()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        FlexibleArrayMemory<PacketHeader, int> array = FlexibleArrayMemory<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 12345 }, [10, 20]);

        // Rebuild the exact object bytes: header write first, then the tail.
        Span<byte> expected = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
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
        byte[] buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        FlexibleArraySpan<PacketHeader, int> view = FlexibleArraySpan<PacketHeader, int>.Create(buffer, 2);
        view.Header = new PacketHeader { Count = 2 };
        view.Elements[0] = 7;
        view.Elements[1] = 8;

        fixed (byte* pointer = buffer)
        {
            ReadOnlyFlexibleArraySpan<PacketHeader, int> anchored =
                ReadOnlyFlexibleArraySpanMarshaller<PacketHeader, int>.ConvertToManaged((PacketHeader*)pointer);

            Assert.Equal(2, anchored.Length);
            Assert.Equal(new[] { 7, 8 }, anchored.Elements.ToArray());
            Assert.Equal(7, anchored.Header.FirstElement);
        }
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
    internal static unsafe partial int MemCmp(FlexibleArrayMemory<PacketHeader, int> left, void* right, nuint size);
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
