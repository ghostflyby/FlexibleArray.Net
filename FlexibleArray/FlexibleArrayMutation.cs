using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray;

// Length-changing work over a header the caller has already established as constructible: the
// public entry points carry that constraint, so nothing here tests for it at run time.
internal static class FlexibleArrayMutation
{
    internal static int Append<THeader, T>(ref THeader header, int length, int capacity, T item)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        if (length == capacity)
        {
            throw new InvalidOperationException("The flexible array is at capacity.");
        }

        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, length + 1);
        Unsafe.Add(ref THeader.GetFirstElement(ref header), length) = item;
        return length + 1;
    }

    internal static int AppendRange<THeader, T>(
        ref THeader header,
        int length,
        int capacity,
        ReadOnlySpan<T> items)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        if (items.Length > capacity - length)
        {
            throw new InvalidOperationException($"The free capacity is {capacity - length} elements but {items.Length} were given.");
        }

        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, length + items.Length);
        items.CopyTo(MemoryMarshal.CreateSpan(
            ref Unsafe.Add(ref THeader.GetFirstElement(ref header), length), items.Length));
        return length + items.Length;
    }

    internal static int Truncate<THeader, T>(ref THeader header, int length, int target)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegative(target);
        if (target > length)
        {
            throw new ArgumentOutOfRangeException(nameof(target),
                $"The truncate target {target} exceeds the committed length {length}.");
        }

        if (target == length)
        {
            return target;
        }

        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, target);
        return target;
    }
}
