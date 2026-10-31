using System.Runtime.CompilerServices;

namespace FlexibleArray;

/// <summary>
/// Caches managed-layout measurements for a given header/element pair: first-element offset,
/// header and element sizes, and the required allocation size in bytes.
/// </summary>
/// <remarks>
/// Measurements are taken from the runtime managed layout (the <c>Unsafe</c> APIs), not from
/// the marshaling layout, so they also hold for non-blittable unmanaged types such as
/// <c>bool</c> and <c>char</c>. The first-element offset is measured directly as the byte
/// distance between the header's start and the field reference returned by the interface; it
/// is never inferred as <c>sizeof(THeader) - sizeof(T)</c>.
/// </remarks>
public static class FlexibleArrayLayout<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    /// <summary>Gets the byte offset of the first-element placeholder field relative to the header's start.</summary>
    public static nint FirstElementOffset { get; }

    /// <summary>Gets the managed size of the header in bytes.</summary>
    public static int HeaderSize { get; }

    /// <summary>Gets the managed size of a single tail element in bytes.</summary>
    public static int ElementSize { get; }

    static FlexibleArrayLayout()
    {
        HeaderSize = Unsafe.SizeOf<THeader>();
        ElementSize = Unsafe.SizeOf<T>();

        THeader temp = default;
        ref byte origin = ref Unsafe.As<THeader, byte>(ref temp);
        ref byte firstElement = ref Unsafe.As<T, byte>(ref THeader.GetFirstElement(ref temp));
        FirstElementOffset = Unsafe.ByteOffset(ref origin, ref firstElement);

        // Only range validation is possible for the contract: the offset must fall inside the
        // header with the whole element contained by it. A negative value means the interface
        // returned memory elsewhere; a value above the bound means no such field fits in H.
        if (FirstElementOffset < 0 || FirstElementOffset > HeaderSize - ElementSize)
        {
            throw new InvalidOperationException(
                $"{typeof(THeader)}.GetFirstElement() returned a reference outside the header: " +
                $"offset {FirstElementOffset}, header size {HeaderSize}, element size {ElementSize}.");
        }
    }

    /// <summary>
    /// Computes the number of bytes required for a complete object holding the given number of
    /// elements: <c>max(sizeof(THeader), O + length * sizeof(T))</c>. A <paramref name="length"/> of
    /// 0 still reserves room for the full header.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="OverflowException">The required byte count exceeds <see cref="int"/>.</exception>
    public static int GetRequiredBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        checked
        {
            long tailBytes = FirstElementOffset + (long)length * ElementSize;
            long requiredBytes = Math.Max(HeaderSize, tailBytes);
            if (requiredBytes > int.MaxValue)
            {
                throw new OverflowException($"The flexible array requires {requiredBytes} bytes, which exceeds int.MaxValue.");
            }

            return (int)requiredBytes;
        }
    }
}
