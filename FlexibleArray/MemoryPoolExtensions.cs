using System.Buffers;

namespace FlexibleArray;

/// <summary>
/// Pool-based construction for flexible array objects: rents storage for the complete object
/// and returns a single writable object whose disposal returns the rental to the pool.
/// </summary>
public static class MemoryPoolExtensions
{
    /// <summary>
    /// Rents storage from the pool, builds the complete object in it, and returns a writable
    /// object whose disposal returns the rental — or the finalizer does, as a leak safety
    /// net. The tail length is derived from the element count.
    /// </summary>
    /// <remarks>
    /// Pools guarantee no particular alignment: headers requiring more than the shared pool's
    /// pointer-size floor should use the managed construction or an aligning allocator.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A header without a registered write-back declares a tail length that does not match the
    /// number of elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    /// <exception cref="OverflowException">The required backing exceeds <see cref="int"/> bytes.</exception>
    public static MutableFlexibleArray<THeader, T> Create<THeader, T>(
        this MemoryPool<byte> pool,
        in THeader header,
        ReadOnlySpan<T> elements)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(pool);
        var byteLength = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);

        var rental = pool.Rent(byteLength);
        try
        {
            return MutableFlexibleArray<THeader, T>.Create(rental.Memory, header, elements).WithOwner(rental);
        }
        catch
        {
            // Nothing escapes on throw, so return the rental here.
            rental.Dispose();
            throw;
        }
    }
}
