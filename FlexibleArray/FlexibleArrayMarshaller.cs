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
/// <see cref="ClassMarshaller"/> regardless of backing kind; the two kinds share one
/// stateful shape through FromManaged overloads. The memory-backed types register no receive
/// direction: a raw pointer cannot produce a Memory. Default-constructed objects are rejected
/// instead of marshalling to a silent NULL pointer. Runtime-based DllImport ignores custom
/// marshallers; this only applies to [LibraryImport] source generation.
/// </remarks>
[CustomMarshaller(typeof(MutableFlexibleSpan<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>))]
[CustomMarshaller(typeof(MutableFlexibleSpan<,>), MarshalMode.ManagedToUnmanagedOut, typeof(FlexibleArrayMarshaller<,>.WritableSpanMarshaller))]
[CustomMarshaller(typeof(FlexibleSpan<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>))]
[CustomMarshaller(typeof(FlexibleSpan<,>), MarshalMode.ManagedToUnmanagedOut, typeof(FlexibleArrayMarshaller<,>.ReadOnlySpanMarshaller))]
[CustomMarshaller(typeof(MutableFlexibleArray<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>.ClassMarshaller))]
[CustomMarshaller(typeof(MutableFlexibleArray<,>), MarshalMode.ManagedToUnmanagedOut, typeof(MutableFlexibleArrayOwnedMarshaller<,>))]
[CustomMarshaller(typeof(FlexibleArray<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>.ClassMarshaller))]
[CustomMarshaller(typeof(FlexibleArray<,>), MarshalMode.ManagedToUnmanagedOut, typeof(FlexibleArrayOwnedMarshaller<,>))]
public static unsafe class FlexibleArrayMarshaller<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    /// <summary>Converts the writable view into a pointer to its header. No pin is taken.</summary>
    /// <exception cref="InvalidOperationException">
    /// The view is default-constructed and has no backing storage.
    /// </exception>
    public static THeader* ConvertToUnmanaged(MutableFlexibleSpan<THeader, T> span)
    {
        ref var header = ref span.Header;
        if (Unsafe.IsNullRef(in header))
        {
            throw new InvalidOperationException("A default-constructed MutableFlexibleSpan has no backing storage.");
        }

        return (THeader*)Unsafe.AsPointer(ref header);
    }

    /// <summary>Converts the read-only view into a pointer to its header. No pin is taken.</summary>
    /// <exception cref="InvalidOperationException">
    /// The view is default-constructed and has no backing storage.
    /// </exception>
    public static THeader* ConvertToUnmanaged(FlexibleSpan<THeader, T> span)
    {
        ref readonly var reference = ref span.Header;
        if (Unsafe.IsNullRef(in reference))
        {
            throw new InvalidOperationException("A default-constructed FlexibleSpan has no backing storage.");
        }

        return (THeader*)Unsafe.AsPointer(in reference);
    }

    /// <summary>Receives writable span views from returned native pointers.</summary>
    public static class WritableSpanMarshaller
    {
        /// <summary>Converts a returned pointer into a writable view anchored at that location.</summary>
        public static MutableFlexibleSpan<THeader, T> ConvertToManaged(THeader* pointer)
            => pointer is null ? default : new MutableFlexibleSpan<THeader, T>(ref Unsafe.AsRef<THeader>(pointer));
    }

    /// <summary>Receives read-only span views from returned native pointers.</summary>
    public static class ReadOnlySpanMarshaller
    {
        /// <summary>Converts a returned pointer into a read-only view anchored at that location.</summary>
        public static FlexibleSpan<THeader, T> ConvertToManaged(THeader* pointer)
            => pointer is null ? default : new FlexibleSpan<THeader, T>(in *pointer);
    }

    /// <summary>
    /// Stateful marshaller that pins either flexible array object kind for the duration of a
    /// call: one instance serves the writable or read-only shape through FromManaged
    /// overloads and shares the pin state. Adopted native objects pin as a no-op handle.
    /// </summary>
    public struct ClassMarshaller
    {
        private MemoryHandle _handle;
        private THeader* _pointer;

        /// <summary>Pins the writable object's backing storage.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="array"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        /// <remarks>Calling this method twice without an intervening Free leaks the first pin;
        /// the source generator invokes each instance exactly once.</remarks>
        public void FromManaged(MutableFlexibleArray<THeader, T> array)
        {
            ArgumentNullException.ThrowIfNull(array);
            _handle = array.Pin();
            _pointer = (THeader*)_handle.Pointer;
        }

        /// <summary>Pins the read-only object's backing storage.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="array"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public void FromManaged(FlexibleArray<THeader, T> array)
        {
            ArgumentNullException.ThrowIfNull(array);
            _handle = array.Pin();
            _pointer = (THeader*)_handle.Pointer;
        }

        /// <summary>Returns the pinned pointer passed to native code.</summary>
        public THeader* ToUnmanaged() => _pointer;

        /// <summary>Releases the pin.</summary>
        public void Free() => _handle.Dispose();
    }
}
