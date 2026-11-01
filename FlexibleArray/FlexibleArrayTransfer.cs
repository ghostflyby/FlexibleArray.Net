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
    public static unsafe FlexibleArray<THeader, T> Adopt<THeader, T>(THeader* pointer)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(pointer);
        var length = THeader.GetTailLength(in *pointer);
        return new FlexibleArray<THeader, T>(pointer, &ReleaseThunk<THeader, T>.Invoke, length);
    }

    /// <summary>
    /// Adopts the transferred object at the given location as a read-only object. Disposal —
    /// and the finalizer, as a leak safety net — release it via the header's registered
    /// <see cref="ITransferableFlexibleArrayHeader{TSelf, T}.Release"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is null.</exception>
    public static unsafe ReadOnlyFlexibleArray<THeader, T> AdoptReadOnly<THeader, T>(THeader* pointer)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(pointer);
        var length = THeader.GetTailLength(in *pointer);
        return new ReadOnlyFlexibleArray<THeader, T>(pointer, &ReleaseThunk<THeader, T>.Invoke, length);
    }

    // Non-generic method in a generic type: function pointers cannot target generic methods,
    // but a closed generic type's static method has a concrete instantiation.
    private static unsafe class ReleaseThunk<THeader, T>
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, ITransferableFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        public static void Invoke(void* pointer)
            => THeader.Release(ref *(THeader*)pointer);
    }
}
