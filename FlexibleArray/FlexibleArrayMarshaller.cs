using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// Entry-point marshaller that turns <see cref="FlexibleArray{THeader, T}"/> into a native
/// <c>THeader*</c>, so a [LibraryImport] signature can declare the owner directly as an input
/// parameter and the native side receives a pointer to the contiguous object. No copying and no
/// allocation happen. Wired as the type-level default via
/// <see cref="NativeMarshallingAttribute"/> on the owner.
/// </summary>
/// <remarks>
/// Registered for <see cref="MarshalMode.ManagedToUnmanagedIn"/>, which covers by-value and
/// <c>in</c> parameters. The pointer borrows the owner's native memory. The owner registers no
/// finalizer, so even if the instance becomes unreachable mid-call the memory is never freed
/// while the pointer is in flight. Runtime-based DllImport ignores custom marshallers; this
/// only applies to [LibraryImport] source generation. The unmanaged-to-managed direction is
/// deliberately absent: rebuilding a view requires the element count, which is protocol data
/// the caller reads from the header.
/// </remarks>
[CustomMarshaller(typeof(FlexibleArray<,>), MarshalMode.ManagedToUnmanagedIn, typeof(FlexibleArrayMarshaller<,>))]
public static unsafe class FlexibleArrayMarshaller<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    /// <summary>Converts the owner into a pointer to its contiguous object memory.</summary>
    /// <exception cref="ObjectDisposedException">The owner has been disposed.</exception>
    public static THeader* ConvertToUnmanaged(FlexibleArray<THeader, T> array)
        => (THeader*)array.DangerousGetPointer();
}
