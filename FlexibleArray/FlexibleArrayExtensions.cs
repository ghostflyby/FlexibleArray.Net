namespace FlexibleArray;

/// <summary>
/// Length-changing operations, available only for headers that register a tail-length
/// write-back: a header without one cannot express a new length, so the constraint makes the
/// unsupported call a compile error instead of a run-time refusal.
/// </summary>
public static class FlexibleArrayExtensions
{
    /// <summary>Appends one element and updates the header's length field.</summary>
    /// <exception cref="InvalidOperationException">The view is at capacity.</exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public static void Append<THeader, T>(this ref MutableFlexibleSpan<THeader, T> span, T item)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        var length = FlexibleArrayMutation.Append(ref span.Header, span.Length, span.Capacity, item);
        span.SetLengthField(length);
        span.NotifyLengthStamped();
    }

    /// <summary>Appends a range of elements and updates the header's length field.</summary>
    /// <exception cref="InvalidOperationException">The range does not fit in the free capacity.</exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public static void AppendRange<THeader, T>(this ref MutableFlexibleSpan<THeader, T> span, ReadOnlySpan<T> items)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        var length = FlexibleArrayMutation.AppendRange(ref span.Header, span.Length, span.Capacity, items);
        span.SetLengthField(length);
        span.NotifyLengthStamped();
    }

    /// <summary>
    /// Shrinks the committed length, updating the header's length field. The removal is
    /// logical: the bytes stay in place and are overwritten by later appends; the capacity
    /// does not change.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> is negative or greater than <see cref="MutableFlexibleSpan{THeader, T}.Length"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public static void Truncate<THeader, T>(this ref MutableFlexibleSpan<THeader, T> span, int length)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        var truncated = FlexibleArrayMutation.Truncate<THeader, T>(ref span.Header, span.Length, length);
        span.SetLengthField(truncated);
        span.NotifyLengthStamped();
    }

    /// <summary>Appends one element and updates the header's length field.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The object is at capacity.</exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public static void Append<THeader, T>(this MutableFlexibleArray<THeader, T> array, T item)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        array.ThrowIfDisposedInternal();
        var length = FlexibleArrayMutation.Append(ref array.HeaderReference, array.LengthValue, array.CapacityValue, item);
        array.SetCommittedLength(length);
    }

    /// <summary>Appends a range of elements and updates the header's length field.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The range does not fit in the free capacity.</exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public static void AppendRange<THeader, T>(this MutableFlexibleArray<THeader, T> array, ReadOnlySpan<T> items)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        array.ThrowIfDisposedInternal();
        var length = FlexibleArrayMutation.AppendRange(ref array.HeaderReference, array.LengthValue, array.CapacityValue, items);
        array.SetCommittedLength(length);
    }

    /// <summary>
    /// Shrinks the committed length, updating the header's length field. The removal is
    /// logical: the bytes stay in place and are overwritten by later appends; the capacity
    /// does not change.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> is negative or greater than the committed length.
    /// </exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public static void Truncate<THeader, T>(this MutableFlexibleArray<THeader, T> array, int length)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        array.ThrowIfDisposedInternal();
        var truncated = FlexibleArrayMutation.Truncate<THeader, T>(ref array.HeaderReference, array.LengthValue, length);
        array.SetCommittedLength(truncated);
    }
}
