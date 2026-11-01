namespace FlexibleArray;

/// <summary>
/// Receive marshaller for read-only flexible array objects whose headers register a native
/// release: a returned <c>THeader*</c> is adopted automatically, so a [LibraryImport]
/// signature can declare <see cref="FlexibleArray{THeader, T}"/> as its return type
/// and receive an object that disposes (and fails over via finalizer) through the header's
/// policy.
/// </summary>
/// <remarks>
/// Constrained to transferable headers: declaring a receive of a non-transferable header
/// fails to compile at the P/Invoke site, so a header without a registered release can never
/// produce a silently leaking declaration.
/// </remarks>
public static unsafe class FlexibleArrayOwnedMarshaller<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    /// <summary>Adopts a returned pointer into a read-only object with automatic release.</summary>
    public static FlexibleArray<THeader, T>? ConvertToManaged(THeader* pointer)
        => pointer is null ? null : FlexibleArrayTransfer.AdoptReadOnly<THeader, T>(pointer);
}
