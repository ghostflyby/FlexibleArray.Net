namespace FlexibleArray;

/// <summary>
/// Optional contract for headers whose tail length field can be written back: construction
/// APIs stamp the element count into the header, so a caller never fills the length field
/// by hand.
/// </summary>
/// <remarks>
/// Count-field headers return a copy with the field set; byte-length headers (nlmsg_len,
/// nla_len) convert the element count to bytes. Headers that do not implement it fall back
/// to declared-length construction: the caller fills the length field and the constructor
/// verifies it against the element count. Sentinel-terminated headers must not implement
/// it: their storage needs one byte beyond the declared tail, and no terminator is written
/// during construction, so a length read-back would walk off the tail.
/// <typeparamref name="TSelf"/> is the implementing header type by convention; unlike
/// <see cref="ITransferableFlexibleArrayHeader{TSelf, T}"/> the contract cannot enforce it,
/// because construction tests the capability at run time.
/// </remarks>
public interface IConstructibleFlexibleArrayHeader<TSelf, T>
    : IFlexibleArrayHeader<TSelf, T>
    where TSelf : unmanaged, IFlexibleArrayHeader<TSelf, T>
    where T : unmanaged
{
    /// <summary>Returns a copy of this header with the tail element count in its length field.</summary>
    /// <remarks>
    /// Paired with <see cref="IFlexibleArrayHeader{TSelf, T}.GetTailElementCount"/>: construction
    /// APIs stamp the count through this member and then verify the pair agrees, so an
    /// asymmetric implementation fails loudly instead of corrupting the object.
    /// </remarks>
    protected internal TSelf WithTailElementCount(int count);
}
