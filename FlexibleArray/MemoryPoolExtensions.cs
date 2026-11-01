using System.Buffers;

namespace FlexibleArray;

/// <summary>
/// Pool-based construction for flexible array objects: rents storage for the complete object
/// and returns a single writable object whose disposal returns the rental to the pool.
/// </summary>
public static class MemoryPoolExtensions
{
    /// <summary>
    /// Rents storage for the complete object from the pool and builds it (header first, then
    /// the tail). Disposing the returned object returns the rental — or the finalizer does,
    /// as a leak safety net. Pools guarantee no particular alignment; the shared pool's
    /// arrays happen to be aligned to at least pointer size, and headers requiring stronger
    /// alignment should use the managed construction or an allocator that guarantees it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The header-declared tail length does not match the number of elements.
    /// </exception>
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
