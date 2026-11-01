using System.Runtime.InteropServices;

namespace FlexibleArray;

// Shared construction core: writes a complete object (header + stamped count + elements)
// and enforces the declared/derived tail length. Both construction families delegate here,
// so the stamp-or-validate rule lives in exactly one place.
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
        destination = constructible is null ? header : constructible.WithTailElementCount(elements.Length);
        elements.CopyTo(MemoryMarshal.CreateSpan(ref THeader.GetFirstElement(ref destination), elements.Length));
        if (THeader.GetTailElementCount(in destination) != elements.Length)
        {
            if (constructible is not null)
            {
                throw new InvalidOperationException(
                    $"Setting the tail element count to {elements.Length} did not read back the same value.");
            }

            throw new ArgumentException(
                $"The header-declared tail length does not match the {elements.Length} elements provided.");
        }
    }
}
