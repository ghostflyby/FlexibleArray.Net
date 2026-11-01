using System.Buffers;

namespace FlexibleArray;

/// <summary>
/// Pool-based construction for flexible array objects: rents storage for the complete object
/// and hands the rental contract back to the caller.
/// </summary>
public static class MemoryPoolExtensions
{
    /// <summary>
    /// Rents storage for the complete object from the pool, builds it (header first, then the
    /// tail), and returns the rental, which the caller must return to the pool via
    /// <see cref="IDisposable.Dispose"/> — the view comes along through
    /// <paramref name="view"/>. Pool buffers are aligned to at least pointer size; headers
    /// requiring stronger alignment should use the managed construction or an allocator that
    /// guarantees it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The header-declared tail length does not match the number of elements.
    /// </exception>
    public static IMemoryOwner<byte> Create<THeader, T>(
        this MemoryPool<byte> pool,
        in THeader header,
        ReadOnlySpan<T> elements,
        out FlexibleArrayMemory<THeader, T> view)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(pool);
        int byteLength = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);

        // Validate before renting: on throw the out argument never reaches the caller, so a
        // post-rental mismatch would leak the rental instead of returning it to the pool.
        if (THeader.GetTailLength(in header) != elements.Length)
        {
            throw new ArgumentException(
                $"The header-declared tail length does not match the {elements.Length} elements provided.");
        }

        IMemoryOwner<byte> owner = pool.Rent(byteLength);
        view = FlexibleArrayMemory<THeader, T>.Create(owner.Memory, header, elements);
        return owner;
    }
}
