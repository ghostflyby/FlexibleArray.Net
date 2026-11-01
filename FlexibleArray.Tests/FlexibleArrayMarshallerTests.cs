using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray.Tests;

/// <summary>
/// Verifies the default pointer marshaller: declaring a flexible array type directly in a
/// [LibraryImport] signature passes the native side a pinned THeader* to the contiguous
/// object memory.
/// </summary>
public sealed unsafe class FlexibleArrayMarshallerTests
{
    [Fact]
    public void Marshaller_PinsHeaderAndTailForTheCall()
    {
        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 7 }, [5, 6]);

        FlexibleArrayMarshaller<PacketHeader, int>.ClassMarshaller marshaller = default;
        marshaller.FromManaged(array);
        try
        {
            var pointer = marshaller.ToUnmanaged();

            ref var header = ref Unsafe.AsRef<PacketHeader>(pointer);
            Assert.Equal(2, header.Count);
            Assert.Equal(7, header.Timestamp);
            ref var headerStart = ref Unsafe.As<PacketHeader, byte>(ref header);
            ref var firstElement = ref Unsafe.As<int, byte>(ref PacketHeader.GetFirstElement(ref header));
            Assert.Equal(16, Unsafe.ByteOffset(ref headerStart, ref firstElement));

            var tail = MemoryMarshal.CreateSpan(ref PacketHeader.GetFirstElement(ref header), 2);
            Assert.Equal(5, tail[0]);
            Assert.Equal(6, tail[1]);
        }
        finally
        {
            marshaller.Free();
        }
    }

    [Fact]
    public void FromManaged_RejectsNullAndDisposedObjects()
    {
        FlexibleArrayMarshaller<PacketHeader, int>.ClassMarshaller marshaller = default;
        Assert.Throws<ArgumentNullException>(
            () => marshaller.FromManaged(null!));
        Assert.Throws<ArgumentNullException>(
            () => marshaller.FromManaged((FlexibleArray<PacketHeader, int>)null!));

        var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 0 }, ReadOnlySpan<int>.Empty);
        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => marshaller.FromManaged(array));
    }

    [Fact]
    public void DefaultMarshalling_IsWiredThroughNativeMarshalling()
    {
        var nativeMarshalling = typeof(MutableFlexibleArray<PacketHeader, int>)
            .GetCustomAttributes<NativeMarshallingAttribute>()
            .SingleOrDefault();
        Assert.NotNull(nativeMarshalling);
        Assert.True(nativeMarshalling.NativeType.IsGenericTypeDefinition);
        Assert.Equal(typeof(FlexibleArrayMarshaller<,>), nativeMarshalling.NativeType);

        // The single entry point hosts registrations for all four types; check the matrix.
        var registrations = typeof(FlexibleArrayMarshaller<PacketHeader, int>)
            .GetCustomAttributes<CustomMarshallerAttribute>()
            .ToList();
        Assert.Equal(8, registrations.Count);

        // Both span sends resolve to the entry's parameter-type overloads.
        Assert.All(registrations.Where(a => a.MarshalMode == MarshalMode.ManagedToUnmanagedIn &&
                (a.ManagedType == typeof(MutableFlexibleSpan<,>) || a.ManagedType == typeof(FlexibleSpan<,>))),
            a => Assert.Equal(typeof(FlexibleArrayMarshaller<,>), a.MarshallerType));
        // Span receives cannot be overloads (return types differ), so each has its own type.
        Assert.Equal(typeof(FlexibleArrayMarshaller<,>.WritableSpanMarshaller), registrations.Single(a =>
            a.MarshalMode == MarshalMode.ManagedToUnmanagedOut && a.ManagedType == typeof(MutableFlexibleSpan<,>)).MarshallerType);
        Assert.Equal(typeof(FlexibleArrayMarshaller<,>.ReadOnlySpanMarshaller), registrations.Single(a =>
            a.MarshalMode == MarshalMode.ManagedToUnmanagedOut && a.ManagedType == typeof(FlexibleSpan<,>)).MarshallerType);
        // Both object sends share one stateful marshaller.
        Assert.All(registrations.Where(a => a.MarshalMode == MarshalMode.ManagedToUnmanagedIn &&
                (a.ManagedType == typeof(MutableFlexibleArray<,>) || a.ManagedType == typeof(FlexibleArray<,>))),
            a => Assert.Equal(typeof(FlexibleArrayMarshaller<,>.ClassMarshaller), a.MarshallerType));
        // Transferable receives adopt automatically through the constrained marshallers.
        Assert.Equal(typeof(MutableFlexibleArrayOwnedMarshaller<,>), registrations.Single(a =>
            a.MarshalMode == MarshalMode.ManagedToUnmanagedOut && a.ManagedType == typeof(MutableFlexibleArray<,>)).MarshallerType);
        Assert.Equal(typeof(FlexibleArrayOwnedMarshaller<,>), registrations.Single(a =>
            a.MarshalMode == MarshalMode.ManagedToUnmanagedOut && a.ManagedType == typeof(FlexibleArray<,>)).MarshallerType);
    }

    [Fact]
    public void LibraryImport_PassesContiguousObjectBytesToNative()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 12345 }, [10, 20]);

        // Rebuild the exact object bytes: header write first, then the tail.
        Span<byte> expected = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        MutableFlexibleSpan<PacketHeader, int>.Create(expected, new PacketHeader { Timestamp = 12345 }, [10, 20]);

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
        var buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [7, 8]);

        fixed (byte* pointer = buffer)
        {
            var anchored =
                FlexibleArrayMarshaller<PacketHeader, int>.ReadOnlySpanMarshaller.ConvertToManaged((PacketHeader*)pointer);

            Assert.Equal(2, anchored.Length);
            Assert.Equal(new[] { 7, 8 }, anchored.Elements.ToArray());
            Assert.Equal(7, anchored.Header.FirstElement);
        }
    }

    [Fact]
    public void ConvertToManaged_AnchorsWritableSpanAtReturnedPointer()
    {
        // A received object already carries its header; the anchoring constructor snapshots
        // the tail length from it.
        var buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [5, 0]);

        fixed (byte* pointer = buffer)
        {
            var view =
                FlexibleArrayMarshaller<PacketHeader, int>.WritableSpanMarshaller.ConvertToManaged((PacketHeader*)pointer);

            Assert.Equal(2, view.Length);
            Assert.Equal(5, view.Elements[0]);

            // Mutating through the received view lands in the underlying storage.
            view.Elements[1] = 7;
        }

        Assert.Equal(7, BitConverter.ToInt32(buffer, 20));
    }

    [Fact]
    public void ConvertToUnmanaged_RejectsDefaultSpans()
    {
        Assert.Throws<InvalidOperationException>(
            () => { _ = FlexibleArrayMarshaller<PacketHeader, int>.ConvertToUnmanaged(default(MutableFlexibleSpan<PacketHeader, int>)); });
        Assert.Throws<InvalidOperationException>(
            () => { _ = FlexibleArrayMarshaller<PacketHeader, int>.ConvertToUnmanaged(default(FlexibleSpan<PacketHeader, int>)); });
    }

    [Fact]
    public void ClassMarshaller_PinsReadOnlyObjectsForTheCall()
    {
        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 1, Timestamp = 3 }, [6]);
        FlexibleArray<PacketHeader, int> readOnly = array;

        FlexibleArrayMarshaller<PacketHeader, int>.ClassMarshaller marshaller = default;
        marshaller.FromManaged(readOnly);
        try
        {
            Assert.Equal(3, marshaller.ToUnmanaged()->Timestamp);
        }
        finally
        {
            marshaller.Free();
        }
    }

    [Fact]
    public void LibraryImport_SpanParameter_PassesStackObjectToNative()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        Span<byte> buffer = stackalloc byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [10, 20]);

        Span<byte> expected = new byte[buffer.Length];
        buffer.CopyTo(expected);
        fixed (byte* right = expected)
        {
            Assert.Equal(0, NativeMemCmp.MemCmpSpan(view, right, (nuint)expected.Length));
        }
    }

    [Fact]
    public void LibraryImport_ReadOnlySpanParameter_PassesObjectToNative()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 77 }, [30, 40]);
        FlexibleSpan<PacketHeader, int> view = array.Span;

        Span<byte> expected = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        MutableFlexibleSpan<PacketHeader, int>.Create(expected, new PacketHeader { Timestamp = 77 }, [30, 40]);

        fixed (byte* right = expected)
        {
            Assert.Equal(0, NativeMemCmp.MemCmpReadOnlySpan(view, right, (nuint)expected.Length));
        }
    }

    [Fact]
    public void LibraryImport_DeclarativeWritableReceive_FromReturnedPointer()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Equal(0, NativeEnv.SetEnv("FA_RECV_WRITABLE", "hi!", overwrite: 1));
        try
        {
            var value = NativeEnv.GetEnvWritable("FA_RECV_WRITABLE");
            Assert.Equal(3, value.Length);
            Assert.Equal("hi!"u8.ToArray(), value.Elements.ToArray());
        }
        finally
        {
            NativeEnv.UnsetEnv("FA_RECV_WRITABLE");
        }
    }

    [Fact]
    public void LibraryImport_ReadOnlyObjectParameter_PassesObjectToNative()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 12345 }, [10, 20]);
        FlexibleArray<PacketHeader, int> readOnly = array;

        Span<byte> expected = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        MutableFlexibleSpan<PacketHeader, int>.Create(expected, new PacketHeader { Timestamp = 12345 }, [10, 20]);

        fixed (byte* right = expected)
        {
            Assert.Equal(0, NativeMemCmp.MemCmpReadOnly(readOnly, right, (nuint)expected.Length));
        }
    }

    [Fact]
    public void ConvertToManaged_NullPointerYieldsDefaultView()
    {
        Assert.Equal(0, FlexibleArrayMarshaller<PacketHeader, int>.ReadOnlySpanMarshaller.ConvertToManaged(null).Length);
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
            var value = NativeEnv.GetEnv("FA_RECV_TEST");
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
    // Declaration-only: the type-level default marshallers apply automatically.
    [LibraryImport("libc", EntryPoint = "memcmp")]
    internal static unsafe partial int MemCmp(MutableFlexibleArray<PacketHeader, int> left, void* right, nuint size);

    [LibraryImport("libc", EntryPoint = "memcmp")]
    internal static unsafe partial int MemCmpSpan(MutableFlexibleSpan<PacketHeader, int> left, void* right, nuint size);

    [LibraryImport("libc", EntryPoint = "memcmp")]
    internal static unsafe partial int MemCmpReadOnly(FlexibleArray<PacketHeader, int> left, void* right, nuint size);

    [LibraryImport("libc", EntryPoint = "memcmp")]
    internal static unsafe partial int MemCmpReadOnlySpan(FlexibleSpan<PacketHeader, int> left, void* right, nuint size);
}

internal static partial class NativeEnv
{
    // Declaration-only receive: native char* getenv(const char*) becomes a complete-object
    // view whose byte tail walks to the terminating NUL.
    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial FlexibleSpan<CStrHeader, byte> GetEnv(string name);

    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MutableFlexibleSpan<CStrHeader, byte> GetEnvWritable(string name);

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetEnv(string name, string value, int overwrite);

    [LibraryImport("libc", EntryPoint = "unsetenv", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int UnsetEnv(string name);
}
