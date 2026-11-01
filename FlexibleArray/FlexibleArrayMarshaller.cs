using System.Buffers;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// Entry-point marshaller that hands native code a <c>THeader*</c> to the complete object, so
/// a [LibraryImport] signature can declare <see cref="FlexibleArrayMemory{THeader, T}"/>
/// directly as an input parameter. The backing is pinned for the duration of the call and
/// released afterwards; no copying happens.
/// </summary>
/// <remarks>
/// Registered for <see cref="MarshalMode.ManagedToUnmanagedIn"/>, which covers by-value and
/// <c>in</c> parameters. The stateful shape keeps the pin alive from <see cref="FromManaged"/>
/// to <see cref="Free"/> regardless of the backing kind. Alignment is the declaration's
/// responsibility. Runtime-based DllImport ignores custom marshallers; this only applies to
/// [LibraryImport] source generation.
/// </remarks>
[CustomMarshaller(typeof(FlexibleArrayMemory<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>))]
public unsafe struct FlexibleArrayMarshaller<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private MemoryHandle _handle;
    private THeader* _pointer;

    /// <summary>Pins the backing storage of the given object.</summary>
    public void FromManaged(FlexibleArrayMemory<THeader, T> managed)
    {
        _handle = managed.Buffer.Pin();
        _pointer = (THeader*)_handle.Pointer;
    }

    /// <summary>Returns the pinned pointer passed to native code.</summary>
    public THeader* ToUnmanaged() => _pointer;

    /// <summary>Releases the pin.</summary>
    public void Free() => _handle.Dispose();
}
