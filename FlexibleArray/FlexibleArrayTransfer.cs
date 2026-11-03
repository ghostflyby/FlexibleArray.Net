namespace FlexibleArray;

/// <summary>
/// Adopts natively allocated complete objects transferred to the caller, exposing them as
/// flexible array objects whose disposal routes through the header-registered release.
/// </summary>
public static class FlexibleArrayTransfer
{
    /// <summary>
    /// Adopts the transferred object at the given location as a writable object. Disposal —
    /// and the finalizer, as a leak safety net — release it via the header's registered
    /// <see cref="ITransferableFlexibleArrayHeader{TSelf, T}.Release"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is null.</exception>
    public static unsafe MutableFlexibleArray<THeader, T> Adopt<THeader, T>(THeader* pointer)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(pointer);
        var length = THeader.GetTailElementCount(in *pointer);
        return new MutableFlexibleArray<THeader, T>(new AdoptedCore<THeader, T>(pointer, length));
    }

    /// <summary>
    /// Adopts the transferred object at the given location as a read-only object. Disposal —
    /// and the finalizer, as a leak safety net — release it via the header's registered
    /// <see cref="ITransferableFlexibleArrayHeader{TSelf, T}.Release"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is null.</exception>
    public static unsafe FlexibleArray<THeader, T> AdoptReadOnly<THeader, T>(THeader* pointer)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(pointer);
        var length = THeader.GetTailElementCount(in *pointer);
        return new FlexibleArray<THeader, T>(new AdoptedCore<THeader, T>(pointer, length));
    }

    /// <summary>
    /// Core for adopted objects: the transferable constraint lets disposal address the
    /// header-registered release directly with the view the contract declares.
    /// </summary>
    internal sealed unsafe class AdoptedCore<THeader, T> : FlexibleArrayCore<THeader, T>
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        public AdoptedCore(THeader* pointer, int length)
            : base(pointer, length)
        {
        }

        protected override void ReleaseStorage()
            => THeader.Release(new FlexibleSpan<THeader, T>(in *Pointer));
    }
}
