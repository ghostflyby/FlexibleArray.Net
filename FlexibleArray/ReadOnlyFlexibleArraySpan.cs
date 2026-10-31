using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray;

/// <summary>
/// A read-only, short-lived view of a complete flexible array object.
/// </summary>
public readonly ref struct ReadOnlyFlexibleArraySpan<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly ref readonly THeader _header;
    private readonly int _length;

    internal ReadOnlyFlexibleArraySpan(ref readonly THeader header, int length)
    {
        _header = ref header;
        _length = length;
    }

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _length;

    /// <summary>Gets a read-only reference to the header.</summary>
    public ref readonly THeader Header => ref _header;

    /// <summary>Gets a read-only element view covering the entire tail, including the first-element placeholder field.</summary>
    public ReadOnlySpan<T> Elements
    {
        get
        {
            // Recover a writable reference via Unsafe.AsRef so the header can be passed by ref
            // to the static contract member; read-onlyness is enforced by the returned
            // ReadOnlySpan.
            ref THeader header = ref Unsafe.AsRef(in _header);
            return MemoryMarshal.CreateReadOnlySpan(ref THeader.GetFirstElement(ref header), _length);
        }
    }

    /// <summary>
    /// Creates a read-only view over an existing buffer. The buffer must be at least as long
    /// as the required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static ReadOnlyFlexibleArraySpan<THeader, T> Create(ReadOnlySpan<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        int requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        ref byte firstByte = ref MemoryMarshal.GetReference(buffer);
        return new ReadOnlyFlexibleArraySpan<THeader, T>(ref Unsafe.As<byte, THeader>(ref firstByte), length);
    }

    /// <summary>
    /// Parses a self-describing object from a buffer: builds a capacity-bounded view, asks the
    /// header contract for the logical tail length, and re-creates the view with that length.
    /// </summary>
    /// <returns>
    /// false when the buffer is shorter than the full header, or when the derived tail length
    /// is negative or exceeds the buffer's element capacity.
    /// </returns>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out ReadOnlyFlexibleArraySpan<THeader, T> view)
    {
        view = default;
        if (buffer.Length < FlexibleArrayLayout<THeader, T>.HeaderSize)
        {
            return false;
        }

        long capacity = (buffer.Length - FlexibleArrayLayout<THeader, T>.FirstElementOffset)
            / FlexibleArrayLayout<THeader, T>.ElementSize;
        ReadOnlyFlexibleArraySpan<THeader, T> capacityView = Create(buffer, (int)capacity);
        int length = THeader.GetTailLength(capacityView);
        if ((uint)length > (uint)capacity)
        {
            return false;
        }

        view = Create(buffer, length);
        return true;
    }
}
