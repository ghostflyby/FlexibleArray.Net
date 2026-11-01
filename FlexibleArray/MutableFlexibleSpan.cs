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
/// <para>
/// Views created over a buffer carry the buffer's element capacity: <see cref="Append"/>,
/// <see cref="AppendRange"/>, <see cref="Advance"/>, and <see cref="Truncate"/> grow and
/// shrink the committed length inside it, keeping the header's length field stamped in sync.
/// Anchored views (received objects) know no storage extent, so their capacity equals their
/// length and the members cannot extend. Length changes require the header to register a
/// write-back; a header without one throws <see cref="NotSupportedException"/> on the first
/// mutation.
/// </para>
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public ref struct MutableFlexibleSpan<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly ref THeader _header;
    private readonly int _capacity;
    private readonly FlexibleArray<THeader, T>? _owner;
    private int _length;

    /// <summary>
    /// Anchors a complete-object view at the header in place. The tail length is derived from
    /// the header contract and is not validated — the declaration guarantees the object is
    /// valid at this location, matching P/Invoke trust semantics. The storage extent is
    /// unknown, so the capacity equals the declared length.
    /// </summary>
    public MutableFlexibleSpan(ref THeader header)
    {
        _header = ref header;
        _length = THeader.GetTailElementCount(in header);
        _capacity = _length;
    }

    internal MutableFlexibleSpan(ref THeader header, int length, int capacity, FlexibleArray<THeader, T>? owner = null)
    {
        _header = ref header;
        _length = length;
        _capacity = capacity;
        _owner = owner;
    }

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _length;

    /// <summary>
    /// Gets the element capacity of the backing storage. This can exceed the requested or
    /// committed count: header padding may cover element slots, and pool rentals are rounded
    /// up — the surplus is genuinely usable.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>Gets a reference to the header. For the overwrite risk of whole assignments, see the class remarks.</summary>
    public ref THeader Header => ref _header;

    /// <summary>Gets an element view covering the entire tail, including the first-element placeholder field.</summary>
    public Span<T> Elements => MemoryMarshal.CreateSpan(ref THeader.GetFirstElement(ref _header), _length);

    /// <summary>Gets the writable region between the committed length and the capacity, for in-place construction before <see cref="Advance"/>.</summary>
    public Span<T> FreeElements => MemoryMarshal.CreateSpan(ref Unsafe.Add(ref THeader.GetFirstElement(ref _header), _length), _capacity - _length);

    /// <summary>Appends one element and updates the header's length field.</summary>
    /// <exception cref="InvalidOperationException">The view is at capacity.</exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public void Append(T item)
    {
        if (_length == _capacity)
        {
            throw new InvalidOperationException("The flexible array is at capacity.");
        }

        FlexibleArrayConstruction.SetLength<THeader, T>(ref _header, _length + 1);
        Unsafe.Add(ref THeader.GetFirstElement(ref _header), _length) = item;
        _length++;
        _owner?.UpdateLength(_length);
    }

    /// <summary>Appends a range of elements and updates the header's length field.</summary>
    /// <exception cref="InvalidOperationException">The range does not fit in the free capacity.</exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public void AppendRange(ReadOnlySpan<T> items)
    {
        if (items.Length > _capacity - _length)
        {
            throw new InvalidOperationException($"The free capacity is {_capacity - _length} elements but {items.Length} were given.");
        }

        FlexibleArrayConstruction.SetLength<THeader, T>(ref _header, _length + items.Length);
        items.CopyTo(FreeElements);
        _length += items.Length;
        _owner?.UpdateLength(_length);
    }

    /// <summary>
    /// Commits elements that were written in place through <see cref="FreeElements"/>, for
    /// nested objects built at the tail, and updates the header's length field.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="elementCount"/> is negative or exceeds the free capacity.
    /// </exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public void Advance(int elementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        if (elementCount == 0)
        {
            return;
        }

        if (elementCount > _capacity - _length)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount),
                $"The advance is {elementCount} elements but the free capacity is {_capacity - _length}.");
        }

        FlexibleArrayConstruction.SetLength<THeader, T>(ref _header, _length + elementCount);
        _length += elementCount;
        _owner?.UpdateLength(_length);
    }

    /// <summary>
    /// Shrinks the committed length, updating the header's length field. The removal is
    /// logical: the bytes stay in place and are overwritten by later appends; the capacity
    /// does not change.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> is negative or greater than <see cref="Length"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public void Truncate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(length),
                $"The truncate target {length} exceeds the committed length {_length}.");
        }

        if (length == _length)
        {
            return;
        }

        FlexibleArrayConstruction.SetLength<THeader, T>(ref _header, length);
        _length = length;
        _owner?.UpdateLength(_length);
    }

    /// <summary>
    /// Creates a view over an existing buffer. The buffer must be at least as long as the
    /// required byte count; alignment is not validated. The capacity is the buffer's element
    /// capacity.
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
        return new MutableFlexibleSpan<THeader, T>(ref Unsafe.As<byte, THeader>(ref firstByte), length, ElementCapacity(buffer.Length));
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
    /// companion for content that is fully known up front; progressive assembly starts from
    /// the <c>(buffer, header)</c> overload and appends.
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
        return new MutableFlexibleSpan<THeader, T>(ref destination, elements.Length, ElementCapacity(buffer.Length));
    }

    /// <summary>
    /// Starts an empty object at the start of the buffer: writes the header with the tail
    /// length stamped to zero, leaving the whole buffer's element capacity free for
    /// <see cref="Append"/>, <see cref="AppendRange"/>, and <see cref="Advance"/>. This is the
    /// progressive-assembly entry point.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The buffer is shorter than the header, or a header without a registered write-back
    /// declares a nonzero tail length.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public static MutableFlexibleSpan<THeader, T> Create(Span<byte> buffer, in THeader header)
    {
        if (buffer.Length < FlexibleArrayLayout<THeader, T>.HeaderSize)
        {
            throw new ArgumentException($"The buffer needs at least {FlexibleArrayLayout<THeader, T>.HeaderSize} bytes but has {buffer.Length}.", nameof(buffer));
        }

        ref var firstByte = ref MemoryMarshal.GetReference(buffer);
        ref var destination = ref Unsafe.As<byte, THeader>(ref firstByte);
        FlexibleArrayConstruction.WriteObject(ref destination, header, ReadOnlySpan<T>.Empty);
        return new MutableFlexibleSpan<THeader, T>(ref destination, 0, ElementCapacity(buffer.Length));
    }

    // The element capacity a byte length can hold beyond the first element.
    private static int ElementCapacity(int byteLength)
        => (int)((byteLength - FlexibleArrayLayout<THeader, T>.FirstElementOffset) / FlexibleArrayLayout<THeader, T>.ElementSize);

    /// <summary>Converts to a read-only view whose read-onlyness covers both the header and the tail.</summary>
    public static implicit operator FlexibleSpan<THeader, T>(MutableFlexibleSpan<THeader, T> span)
        => new(ref span._header, span._length);
}
