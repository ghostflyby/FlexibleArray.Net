using System.Runtime.InteropServices;

namespace FlexibleArray;

internal static class FlexibleArrayConstruction
{
    internal static void WriteObject<THeader, T>(ref THeader destination, in THeader header, ReadOnlySpan<T> elements)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        var constructible = header as IConstructibleFlexibleArrayHeader<THeader, T>;
        destination = constructible is null ? header : StampVerified(constructible, elements.Length);
        elements.CopyTo(MemoryMarshal.CreateSpan(ref THeader.GetFirstElement(ref destination), elements.Length));
        if (constructible is null && THeader.GetTailElementCount(in destination) != elements.Length)
        {
            throw new ArgumentException(
                $"The header-declared tail length does not match the {elements.Length} elements provided.");
        }
    }

    internal static void SetLength<THeader, T>(ref THeader header, int newLength)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>, IConstructibleFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        var stamped = header.WithTailElementCount(newLength);
        if (THeader.GetTailElementCount(in stamped) != newLength)
        {
            throw new InvalidOperationException(
                $"Setting the tail element count to {newLength} did not read back the same value.");
        }

        header = stamped;
    }

    private static THeader StampVerified<THeader, T>(
        IConstructibleFlexibleArrayHeader<THeader, T> constructible,
        int count)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        var stamped = constructible.WithTailElementCount(count);
        if (THeader.GetTailElementCount(in stamped) != count)
        {
            throw new InvalidOperationException(
                $"Setting the tail element count to {count} did not read back the same value.");
        }

        return stamped;
    }
}
