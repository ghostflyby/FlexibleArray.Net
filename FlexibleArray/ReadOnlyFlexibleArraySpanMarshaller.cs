using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// Entry-point marshaller that turns a native <c>THeader*</c> returned by native code into a
/// read-only view anchored at that location, so a [LibraryImport] signature can declare the
/// return type directly and receive the complete object without copying. The tail length is
/// derived from the header contract and is not validated. Wired as the type-level default via
/// <see cref="NativeMarshallingAttribute"/> on the view.
/// </summary>
/// <remarks>
/// Registered for <see cref="MarshalMode.ManagedToUnmanagedOut"/>, which covers return values
/// and <c>out</c> parameters of P/Invokes. A null pointer converts to a default-constructed
/// view. Runtime-based DllImport ignores custom marshallers; this only applies to
/// [LibraryImport] source generation.
/// </remarks>
[CustomMarshaller(typeof(ReadOnlyFlexibleArraySpan<,>), MarshalMode.ManagedToUnmanagedOut, typeof(ReadOnlyFlexibleArraySpanMarshaller<,>))]
public static unsafe class ReadOnlyFlexibleArraySpanMarshaller<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    /// <summary>Converts a returned pointer into a read-only view anchored at that location.</summary>
    public static ReadOnlyFlexibleArraySpan<THeader, T> ConvertToManaged(THeader* pointer)
        => pointer is null ? default : new ReadOnlyFlexibleArraySpan<THeader, T>(in *pointer);
}
