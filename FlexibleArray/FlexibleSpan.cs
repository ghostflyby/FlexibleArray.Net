using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// A read-only, short-lived view of a complete flexible array object.
/// </summary>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public readonly ref struct FlexibleSpan<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly ref readonly THeader _header;
    private readonly int _length;

    /// <summary>
    /// Anchors a complete-object view at the header in place. The tail length is derived from
    /// the header contract and is not validated — the declaration guarantees the object is
    /// valid at this location, matching P/Invoke trust semantics.
    /// </summary>
    public FlexibleSpan(in THeader header)
    {
        _header = ref header;
        _length = THeader.GetTailElementCount(in header);
    }

    internal FlexibleSpan(ref readonly THeader header, int length)
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
            // Span.
            ref var header = ref Unsafe.AsRef(in _header);
            return MemoryMarshal.CreateReadOnlySpan(ref THeader.GetFirstElement(ref header), _length);
        }
    }

    /// <summary>
    /// Creates a read-only view over an existing buffer. The buffer must be at least as long
    /// as the required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static FlexibleSpan<THeader, T> Create(ReadOnlySpan<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        ref var firstByte = ref MemoryMarshal.GetReference(buffer);
        return new FlexibleSpan<THeader, T>(ref Unsafe.As<byte, THeader>(ref firstByte), length);
    }

    /// <summary>
    /// Creates a read-only view over a buffer that already holds a complete object: the
    /// buffer is not written, and the tail length is derived from the header contract.
    /// Alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentException">The buffer is shorter than the header.</exception>
    public static FlexibleSpan<THeader, T> Create(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < FlexibleArrayLayout<THeader, T>.HeaderSize)
        {
            throw new ArgumentException(
                $"The buffer needs at least {FlexibleArrayLayout<THeader, T>.HeaderSize} bytes but has {buffer.Length}.", nameof(buffer));
        }

        var headerBuffer = MemoryMarshal.Cast<byte, THeader>(buffer);
        ref var header = ref MemoryMarshal.GetReference(headerBuffer);
        return new FlexibleSpan<THeader, T>(in header);
    }

    /// <summary>
    /// Parses a self-describing object from a buffer: reads the header and asks the header
    /// contract for the logical tail length.
    /// </summary>
    /// <returns>
    /// false when the buffer is shorter than the full header, or when the derived tail length
    /// is negative or exceeds the buffer's element capacity.
    /// </returns>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out FlexibleSpan<THeader, T> view)
    {
        view = default;
        if (buffer.Length < FlexibleArrayLayout<THeader, T>.HeaderSize)
        {
            return false;
        }

        var headerBuf = MemoryMarshal.Cast<byte, THeader>(buffer);
        ref var header = ref MemoryMarshal.GetReference(headerBuf);
        var length = THeader.GetTailElementCount(in header);
        long capacity = (buffer.Length - FlexibleArrayLayout<THeader, T>.FirstElementOffset)
            / FlexibleArrayLayout<THeader, T>.ElementSize;
        if ((uint)length > (uint)capacity)
        {
            return false;
        }

        view = Create(buffer, length);
        return true;
    }
}
