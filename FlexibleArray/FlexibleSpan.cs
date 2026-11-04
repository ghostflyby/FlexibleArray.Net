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

    /// <summary>Gets the byte extent of the committed object, header included.</summary>
    /// <remarks>
    /// Counts the full header even when the tail is empty, so consecutive objects laid out at
    /// this boundary do not overlap. Protocol alignment between objects is not included.
    /// </remarks>
    public int ByteLength => FlexibleArrayLayout<THeader, T>.GetRequiredBytes(_length);

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
    /// Wraps a buffer that already holds a complete object: the
    /// buffer is not written, and the tail length is derived from the header contract and
    /// validated against the buffer extent. Alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The header contract derives a negative tail length.</exception>
    /// <exception cref="ArgumentException">
    /// The buffer is too short for the header or for the derived tail length.
    /// </exception>
    public static FlexibleSpan<THeader, T> Wrap(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < FlexibleArrayLayout<THeader, T>.HeaderSize)
        {
            throw new ArgumentException(
                $"The buffer needs at least {FlexibleArrayLayout<THeader, T>.HeaderSize} bytes but has {buffer.Length}.", nameof(buffer));
        }

        var headerBuffer = MemoryMarshal.Cast<byte, THeader>(buffer);
        ref var header = ref MemoryMarshal.GetReference(headerBuffer);
        var length = THeader.GetTailElementCount(in header);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (requiredBytes > buffer.Length)
        {
            throw new ArgumentException(
                $"The buffer needs at least {requiredBytes} bytes for the derived tail length but has {buffer.Length}.", nameof(buffer));
        }

        return new FlexibleSpan<THeader, T>(in header, length);
    }
}
