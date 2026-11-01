namespace FlexibleArray;

/// <summary>
/// Optional contract for headers whose complete objects are native-allocated and transferred
/// to the caller, who must release them through a protocol-specific call. Implementing this
/// interface registers that release with the flexible array transfer factory.
/// </summary>
/// <remarks>
/// Lifetime policy is part of the header's protocol identity: two headers with identical
/// layout but different lifetimes (a borrowed static string versus a malloc'd copy) are
/// distinct header types, and only the transferable one implements this contract. The
/// registered release must free the whole object at the given header's location.
/// </remarks>
public interface ITransferableFlexibleArrayHeader<TSelf, T>
    where TSelf : unmanaged, IFlexibleArrayHeader<TSelf, T>, ITransferableFlexibleArrayHeader<TSelf, T>
    where T : unmanaged
{
    /// <summary>Releases the complete object whose header is at the given location.</summary>
    /// <remarks>The storage is invalid after this call returns.</remarks>
    static abstract void Release(scoped ref TSelf header);
}
