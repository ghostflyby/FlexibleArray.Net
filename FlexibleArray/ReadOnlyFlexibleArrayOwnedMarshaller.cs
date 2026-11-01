namespace FlexibleArray;

/// <summary>
/// Receive marshaller for read-only flexible array objects whose headers register a native
/// release: a returned <c>THeader*</c> is adopted automatically, so a [LibraryImport]
/// signature can declare <see cref="ReadOnlyFlexibleArray{THeader, T}"/> as its return type
/// and receive an object that disposes (and fails over via finalizer) through the header's
/// policy.
/// </summary>
/// <remarks>
/// Constrained to transferable headers, mirroring
/// <see cref="FlexibleArrayOwnedMarshaller{THeader, T}"/>.
/// </remarks>
public static unsafe class ReadOnlyFlexibleArrayOwnedMarshaller<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    /// <summary>Adopts a returned pointer into a read-only object with automatic release.</summary>
    public static ReadOnlyFlexibleArray<THeader, T>? ConvertToManaged(THeader* pointer)
        => pointer is null ? null : FlexibleArrayTransfer.AdoptReadOnly<THeader, T>(pointer);
}
