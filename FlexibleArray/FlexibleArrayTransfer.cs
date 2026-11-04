namespace FlexibleArray;

/// <summary>
/// The marshal-facing conversion between native pointers and flexible array objects: this is
/// the only place a raw <c>THeader*</c> is adopted, and it writes nothing — the object is
/// assumed to have been formed by its native producer. The disposal routes through the
/// header-registered release, with the finalizer as a leak safety net.
/// </summary>
internal static unsafe class FlexibleArrayTransfer
{
    /// <summary>Adopts the transferred object at the given location as a writable object.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The derived tail length is negative.</exception>
    internal static MutableFlexibleArray<THeader, T> Adopt<THeader, T>(THeader* pointer)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(pointer);
        var length = THeader.GetTailElementCount(in *pointer);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new MutableFlexibleArray<THeader, T>(new FlexibleArrayCore<THeader, T>(
            pointer,
            static view => THeader.Release(view),
            length));
    }

    /// <summary>Adopts the transferred object at the given location as a read-only object.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The derived tail length is negative.</exception>
    internal static FlexibleArray<THeader, T> AdoptReadOnly<THeader, T>(THeader* pointer)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(pointer);
        var length = THeader.GetTailElementCount(in *pointer);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new FlexibleArray<THeader, T>(new FlexibleArrayCore<THeader, T>(
            pointer,
            static view => THeader.Release(view),
            length));
    }
}
