using System.Runtime.InteropServices;

namespace FlexibleArray;

// Shared construction core: writes a complete object (header + stamped count + elements)
// and enforces the declared/derived tail length. Both construction families and the
// capacity mutations delegate here, so the stamp-or-validate rule lives in exactly one
// place.
internal static class FlexibleArrayConstruction
{
    // Writes the header at the destination, stamps the tail element count when the header
    // registers a write-back (overwriting whatever the caller's header carried), copies the
    // elements, and verifies the result reads back the element count: a stamped header that
    // disagrees points at a broken With/Get pair, an unstamped one at the caller's count.
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

    // Sets the tail length of an existing object in place: the single mutation point shared
    // by the capacity members. Gates on the write-back capability, stamps, and verifies.
    internal static void SetLength<THeader, T>(ref THeader header, int newLength)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
    {
        if (header is not IConstructibleFlexibleArrayHeader<THeader, T> constructible)
        {
            throw new NotSupportedException(
                "The header registers no tail-length write-back, so its length cannot be changed.");
        }

        header = StampVerified(constructible, newLength);
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
