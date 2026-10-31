namespace FlexibleArray;

/// <summary>
/// A writable, long-holdable view of a flexible array object.
/// </summary>
/// <remarks>
/// The prototype implementation holds the owning object directly, so its lifetime is managed
/// by the owner: accessing <see cref="Span"/> after the owner is disposed throws
/// <see cref="ObjectDisposedException"/>.
/// </remarks>
public readonly struct FlexibleArrayMemory<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly FlexibleArray<THeader, T> _owner;

    internal FlexibleArrayMemory(FlexibleArray<THeader, T> owner) => _owner = owner;

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _owner?.Length ?? 0;

    /// <summary>Projects a fresh Span view on each call.</summary>
    /// <exception cref="InvalidOperationException">The view is default-constructed and has no backing storage.</exception>
    /// <exception cref="ObjectDisposedException">The owner has been disposed.</exception>
    public FlexibleArraySpan<THeader, T> Span
    {
        get
        {
            ThrowIfDefault();
            return _owner.Span;
        }
    }

    /// <summary>Converts to a read-only view.</summary>
    public static implicit operator ReadOnlyFlexibleArrayMemory<THeader, T>(FlexibleArrayMemory<THeader, T> memory)
        => new(memory._owner);

    private void ThrowIfDefault()
    {
        if (_owner is null)
        {
            throw new InvalidOperationException("A default-constructed FlexibleArrayMemory has no backing storage.");
        }
    }
}
