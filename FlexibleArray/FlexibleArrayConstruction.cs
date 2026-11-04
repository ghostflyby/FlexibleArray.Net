using System.Runtime.InteropServices;

namespace FlexibleArray;

internal static class FlexibleArrayConstruction
{
    internal static void WriteObject<THeader, T>(out THeader destination, in THeader header, ReadOnlySpan<T> elements)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        if (header is IConstructibleFlexibleArrayHeader<THeader, T> constructible)
        {
            WriteStampedObject(out destination, constructible, elements);
        }
        else
        {
            WriteDeclaredObject(out destination, header, elements);
        }
    }

    // The header registers a write-back, so the library authors the length from the element
    // count and overwrites whatever the incoming header declared.
    private static void WriteStampedObject<THeader, T>(
        out THeader destination,
        IConstructibleFlexibleArrayHeader<THeader, T> constructible,
        ReadOnlySpan<T> elements)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        destination = StampVerified(constructible, elements.Length);
        elements.CopyTo(MemoryMarshal.CreateSpan(ref THeader.GetFirstElement(ref destination), elements.Length));
    }

    // Without a write-back the declared length is the truth, so a request whose element count
    // disagrees is rejected. The check runs after the copy because the length may be derived
    // from tail content — a sentinel walk over unwritten bytes would reject a valid request.
    private static void WriteDeclaredObject<THeader, T>(
        out THeader destination,
        in THeader header,
        ReadOnlySpan<T> elements)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        destination = header;
        elements.CopyTo(MemoryMarshal.CreateSpan(ref THeader.GetFirstElement(ref destination), elements.Length));
        if (THeader.GetTailElementCount(in destination) != elements.Length)
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
