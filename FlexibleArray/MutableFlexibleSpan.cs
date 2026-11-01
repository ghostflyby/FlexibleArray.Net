using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// A writable, short-lived view of a complete flexible array object.
/// </summary>
/// <remarks>
/// A flexible array denotes a complete object and offers no slicing; use <see cref="Elements"/>
/// when a slice is needed. Assigning a whole value to <see cref="Header"/> writes the
/// first-element placeholder field together with any trailing header padding, overwriting the
/// region overlapped by the tail. Initialize by writing the header first, then the tail.
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public readonly ref struct MutableFlexibleSpan<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly ref THeader _header;
    private readonly int _length;

    /// <summary>
    /// Anchors a complete-object view at the header in place. The tail length is derived from
    /// the header contract and is not validated — the declaration guarantees the object is
    /// valid at this location, matching P/Invoke trust semantics.
    /// </summary>
    public MutableFlexibleSpan(ref THeader header)
    {
        _header = ref header;
        _length = THeader.GetTailElementCount(in header);
    }

    internal MutableFlexibleSpan(ref THeader header, int length)
    {
        _header = ref header;
        _length = length;
    }

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _length;

    /// <summary>Gets a reference to the header. For the overwrite risk of whole assignments, see the class remarks.</summary>
    public ref THeader Header => ref _header;

    /// <summary>Gets an element view covering the entire tail, including the first-element placeholder field.</summary>
    public Span<T> Elements => MemoryMarshal.CreateSpan(ref THeader.GetFirstElement(ref _header), _length);

    /// <summary>
    /// Creates a view over an existing buffer. The buffer must be at least as long as the
    /// required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static MutableFlexibleSpan<THeader, T> Create(Span<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        ref var firstByte = ref MemoryMarshal.GetReference(buffer);
        return new MutableFlexibleSpan<THeader, T>(ref Unsafe.As<byte, THeader>(ref firstByte), length);
    }

    /// <summary>
    /// Builds a complete object at the start of the buffer: writes the header and copies the
    /// elements after it, deriving the tail length from the element count. When the header
    /// registers a write-back (<see cref="IConstructibleFlexibleArrayHeader{TSelf, T}"/>), the
    /// count is stamped into the length field, overwriting whatever the passed-in header
    /// carried; otherwise the header's own length field must already declare the element
    /// count.
    /// </summary>
    /// <remarks>
    /// The derived or declared count is verified against
    /// <see cref="IFlexibleArrayHeader{TSelf, T}.GetTailElementCount"/> after the copy, so an
    /// asymmetric write-back or a stale declared length fails here instead of producing a
    /// corrupt object. Alignment is the buffer's responsibility. This is the raw form's
    /// companion for content that is fully known up front; progressive assembly keeps the
    /// <c>(buffer, length)</c> overload.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The buffer is too short, or a header without a registered write-back declares a tail
    /// length that differs from the number of elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    /// <exception cref="OverflowException">The required backing exceeds <see cref="int"/> bytes.</exception>
    public static MutableFlexibleSpan<THeader, T> Create(Span<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
    {
        var requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        ref var firstByte = ref MemoryMarshal.GetReference(buffer);
        ref var destination = ref Unsafe.As<byte, THeader>(ref firstByte);
        FlexibleArrayConstruction.WriteObject(ref destination, header, elements);
        return new MutableFlexibleSpan<THeader, T>(ref destination, elements.Length);
    }

    /// <summary>Converts to a read-only view whose read-onlyness covers both the header and the tail.</summary>
    public static implicit operator FlexibleSpan<THeader, T>(MutableFlexibleSpan<THeader, T> span)
        => new(ref span._header, span._length);
}
