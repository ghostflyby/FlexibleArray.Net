namespace FlexibleArray;

/// <summary>
/// A read-only, long-holdable view of a flexible array object.
/// </summary>
/// <remarks>Lifetime semantics match <see cref="FlexibleArrayMemory{THeader, T}"/>: managed by the held owner.</remarks>
public readonly struct ReadOnlyFlexibleArrayMemory<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly FlexibleArray<THeader, T> _owner;

    internal ReadOnlyFlexibleArrayMemory(FlexibleArray<THeader, T> owner) => _owner = owner;

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _owner?.Length ?? 0;

    /// <summary>Projects a fresh read-only Span view on each call.</summary>
    /// <exception cref="InvalidOperationException">The view is default-constructed and has no backing storage.</exception>
    /// <exception cref="ObjectDisposedException">The owner has been disposed.</exception>
    public ReadOnlyFlexibleArraySpan<THeader, T> Span
    {
        get
        {
            ThrowIfDefault();
            return _owner.Span;
        }
    }

    private void ThrowIfDefault()
    {
        if (_owner is null)
        {
            throw new InvalidOperationException("A default-constructed ReadOnlyFlexibleArrayMemory has no backing storage.");
        }
    }
}
