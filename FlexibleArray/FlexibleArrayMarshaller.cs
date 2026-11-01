using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// Entry point for the default marshalling of every flexible array type: native code receives
/// a <c>THeader*</c> to the complete object, and a returned pointer becomes an anchored view.
/// No copying happens in either direction.
/// </summary>
/// <remarks>
/// The span send directions are parameter-type overloads on this entry type; the receive
/// directions cannot be — they differ only in return type, and CustomMarshallerAttribute has
/// no method-name argument — so each lives on its own nested type. Span views hold no
/// handle, and unlike the built-in <c>Span&lt;T&gt;</c> marshalling no pin is taken: a span
/// over a movable managed array must not be passed — the backing must be stack, native, or
/// pinned/pinned-heap memory, and that precondition is the caller's runtime responsibility,
/// not something a declaration can enforce. Memory-backed objects are pinned by
/// <see cref="MemoryMarshaller"/> regardless of backing kind; the two kinds share one
/// stateful shape through FromManaged overloads. The memory-backed types register no receive
/// direction: a raw pointer cannot produce a Memory. Default-constructed objects are rejected
/// instead of marshalling to a silent NULL pointer. Runtime-based DllImport ignores custom
/// marshallers; this only applies to [LibraryImport] source generation.
/// </remarks>
[CustomMarshaller(typeof(FlexibleArraySpan<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>))]
[CustomMarshaller(typeof(FlexibleArraySpan<,>), MarshalMode.ManagedToUnmanagedOut, typeof(FlexibleArrayMarshaller<,>.WritableSpanMarshaller))]
[CustomMarshaller(typeof(ReadOnlyFlexibleArraySpan<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>))]
[CustomMarshaller(typeof(ReadOnlyFlexibleArraySpan<,>), MarshalMode.ManagedToUnmanagedOut, typeof(FlexibleArrayMarshaller<,>.ReadOnlySpanMarshaller))]
[CustomMarshaller(typeof(FlexibleArrayMemory<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>.MemoryMarshaller))]
[CustomMarshaller(typeof(ReadOnlyFlexibleArrayMemory<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>.MemoryMarshaller))]
public static unsafe class FlexibleArrayMarshaller<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    /// <summary>Converts the writable view into a pointer to its header. No pin is taken.</summary>
    /// <exception cref="InvalidOperationException">
    /// The view is default-constructed and has no backing storage.
    /// </exception>
    public static THeader* ConvertToUnmanaged(FlexibleArraySpan<THeader, T> span)
    {
        ref THeader header = ref span.Header;
        if (Unsafe.IsNullRef(in header))
        {
            throw new InvalidOperationException("A default-constructed FlexibleArraySpan has no backing storage.");
        }

        return (THeader*)Unsafe.AsPointer(ref header);
    }

    /// <summary>Converts the read-only view into a pointer to its header. No pin is taken.</summary>
    /// <exception cref="InvalidOperationException">
    /// The view is default-constructed and has no backing storage.
    /// </exception>
    public static THeader* ConvertToUnmanaged(ReadOnlyFlexibleArraySpan<THeader, T> span)
    {
        ref readonly THeader reference = ref span.Header;
        if (Unsafe.IsNullRef(in reference))
        {
            throw new InvalidOperationException("A default-constructed ReadOnlyFlexibleArraySpan has no backing storage.");
        }

        ref THeader header = ref Unsafe.AsRef(in reference);
        return (THeader*)Unsafe.AsPointer(ref header);
    }

    /// <summary>Receives writable span views from returned native pointers.</summary>
    public static class WritableSpanMarshaller
    {
        /// <summary>Converts a returned pointer into a writable view anchored at that location.</summary>
        public static FlexibleArraySpan<THeader, T> ConvertToManaged(THeader* pointer)
            => pointer is null ? default : new FlexibleArraySpan<THeader, T>(ref Unsafe.AsRef<THeader>(pointer));
    }

    /// <summary>Receives read-only span views from returned native pointers.</summary>
    public static class ReadOnlySpanMarshaller
    {
        /// <summary>Converts a returned pointer into a read-only view anchored at that location.</summary>
        public static ReadOnlyFlexibleArraySpan<THeader, T> ConvertToManaged(THeader* pointer)
            => pointer is null ? default : new ReadOnlyFlexibleArraySpan<THeader, T>(in *pointer);
    }

    /// <summary>
    /// Stateful marshaller that pins either Memory-backed object kind for the duration of a
    /// call: one instance serves the writable or read-only shape through FromManaged
    /// overloads and shares the pin state.
    /// </summary>
    public struct MemoryMarshaller
    {
        private MemoryHandle _handle;
        private THeader* _pointer;

        /// <summary>Pins the writable object's backing storage.</summary>
        /// <exception cref="InvalidOperationException">
        /// The object is default-constructed and has no backing storage.
        /// </exception>
        /// <remarks>Calling this method twice without an intervening Free leaks the first pin;
        /// the source generator invokes each instance exactly once.</remarks>
        public void FromManaged(FlexibleArrayMemory<THeader, T> managed)
        {
            if (managed.Buffer.IsEmpty)
            {
                throw new InvalidOperationException("A default-constructed FlexibleArrayMemory has no backing storage.");
            }

            Pin(managed.Buffer);
        }

        /// <summary>Pins the read-only object's backing storage.</summary>
        /// <exception cref="InvalidOperationException">
        /// The object is default-constructed and has no backing storage.
        /// </exception>
        /// <remarks>Calling this method twice without an intervening Free leaks the first pin;
        /// the source generator invokes each instance exactly once.</remarks>
        public void FromManaged(ReadOnlyFlexibleArrayMemory<THeader, T> managed)
        {
            if (managed.Buffer.IsEmpty)
            {
                throw new InvalidOperationException("A default-constructed ReadOnlyFlexibleArrayMemory has no backing storage.");
            }

            Pin(managed.Buffer);
        }

        /// <summary>Returns the pinned pointer passed to native code.</summary>
        public THeader* ToUnmanaged() => _pointer;

        /// <summary>Releases the pin.</summary>
        public void Free() => _handle.Dispose();

        private void Pin(ReadOnlyMemory<byte> buffer)
        {
            _handle = buffer.Pin();
            _pointer = (THeader*)_handle.Pointer;
        }
    }
}
