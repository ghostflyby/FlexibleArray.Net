namespace FlexibleArray;

/// <summary>
/// Optional contract for headers whose tail length field can be written back: construction
/// APIs stamp the element count into the header, so a caller never fills the length field
/// by hand. Implementing this interface registers that write-back with every construction
/// path; the registration is an ordinary interface test at construction time.
/// </summary>
/// <remarks>
/// Capability is part of the header's protocol identity, mirroring
/// <see cref="ITransferableFlexibleArrayHeader{TSelf, T}"/>
/// (<typeparamref name="TSelf"/> is the implementing header type by convention). Count-field
/// headers return a copy with the field set; byte-length headers (nlmsg_len, nla_len)
/// convert the element count to bytes. Headers that do not implement it fall back to
/// declared-length construction: the caller fills the length field and the constructor
/// verifies it against the element count. Sentinel-terminated headers should not implement
/// it: their storage needs one byte beyond the declared tail, which the byte-counting model
/// does not express, and a terminator is never written during construction — so a sentinel
/// header that implements this contract anyway would walk off the tail when its length is
/// read back. Not implementing the interface keeps that hazard out of reach by convention;
/// it is an implementer-side obligation, not an enforced property.
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
