using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// A writable, long-holdable flexible array object: storage, projections, and — when the
/// backing has one — the lifetime that releases it, in a single object.
/// </summary>
/// <remarks>
/// Lifetime semantics are inherited from <see cref="FlexibleArray{THeader,T}"/>;
/// construction happens here because writing requires the writable shape.
/// <para>
/// Objects with storage-backed memory carry its element capacity:
/// <see cref="Append"/>, <see cref="AppendRange"/>, <see cref="Advance"/>, and
/// <see cref="Truncate"/> grow and shrink the committed length inside it by stamping the
/// header's length field. Every stamp — including one through a held writable projection —
/// is reported back to this object, so the committed length stays in lockstep with the
/// header. Adopted native objects know no storage extent, so their capacity equals their
/// length. Length changes require the header to register a write-back; a header without one
/// throws <see cref="NotSupportedException"/> on the first mutation. Instances are not
/// thread-safe.
/// </para>
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public sealed unsafe class MutableFlexibleArray<THeader, T> : FlexibleArray<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private MutableFlexibleArray(ReadOnlyMemory<byte> buffer, int length, IMemoryOwner<byte>? owner)
        : base(buffer, length, owner)
    {
    }

    internal MutableFlexibleArray(void* pointer, delegate* managed<void*, void> release, int length)
        : base(pointer, release, length)
    {
    }

    /// <summary>
    /// Gets the element capacity of the backing storage: its element capacity when
    /// memory-backed, or the committed length for adopted native objects. This can exceed the
    /// requested or committed count: header padding may cover element slots, and pool rentals
    /// are rounded up — the surplus is genuinely usable.
    /// </summary>
    public int Capacity => Buffer.IsEmpty
        ? Length
        : (int)((Buffer.Length - FlexibleArrayLayout<THeader, T>.FirstElementOffset) / FlexibleArrayLayout<THeader, T>.ElementSize);

    /// <summary>Gets the writable region between the committed length and the capacity, for in-place construction before <see cref="Advance"/>.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public Span<T> FreeElements
    {
        get
        {
            ThrowIfDisposed();
            ref var header = ref Unsafe.AsRef(in GetHeaderReference());
            return MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref THeader.GetFirstElement(ref header), Length), Capacity - Length);
        }
    }

    /// <summary>Appends one element and updates the header's length field.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The object is at capacity.</exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public void Append(T item)
    {
        ThrowIfDisposed();
        if (Length == Capacity)
        {
            throw new InvalidOperationException("The flexible array is at capacity.");
        }

        // The length is cached, so it keeps the pre-stamp value until UpdateLength runs.
        ref var header = ref Unsafe.AsRef(in GetHeaderReference());
        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, Length + 1);
        Unsafe.Add(ref THeader.GetFirstElement(ref header), Length) = item;
        UpdateLength(Length + 1);
    }

    /// <summary>Appends a range of elements and updates the header's length field.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The range does not fit in the free capacity.</exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public void AppendRange(ReadOnlySpan<T> items)
    {
        ThrowIfDisposed();
        if (items.Length > Capacity - Length)
        {
            throw new InvalidOperationException($"The free capacity is {Capacity - Length} elements but {items.Length} were given.");
        }

        ref var header = ref Unsafe.AsRef(in GetHeaderReference());
        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, Length + items.Length);
        items.CopyTo(FreeElements);
        UpdateLength(Length + items.Length);
    }

    /// <summary>
    /// Commits elements that were written in place through <see cref="FreeElements"/>, for
    /// nested objects built at the tail, and updates the header's length field.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="elementCount"/> is negative or exceeds the free capacity.
    /// </exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public void Advance(int elementCount)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        if (elementCount == 0)
        {
            return;
        }

        if (elementCount > Capacity - Length)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount),
                $"The advance is {elementCount} elements but the free capacity is {Capacity - Length}.");
        }

        ref var header = ref Unsafe.AsRef(in GetHeaderReference());
        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, Length + elementCount);
        UpdateLength(Length + elementCount);
    }

    /// <summary>
    /// Shrinks the committed length, updating the header's length field. The removal is
    /// logical: the bytes stay in place and are overwritten by later appends; the capacity
    /// does not change.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> is negative or greater than the committed length.
    /// </exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public void Truncate(int length)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length),
                $"The truncate target {length} exceeds the committed length {Length}.");
        }

        if (length == Length)
        {
            return;
        }

        ref var header = ref Unsafe.AsRef(in GetHeaderReference());
        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, length);
        UpdateLength(length);
    }

    /// <summary>
    /// Builds a complete object with managed backing: the array is allocated on the pinned
    /// object heap with extra bytes, and the object starts at the required alignment no matter
    /// where the runtime places the allocation. The tail length is derived from the element
    /// count — stamped when the header registers a write-back, validated against the declared
    /// field otherwise.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A header without a registered write-back declares a tail length that does not match the
    /// number of elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    /// <exception cref="OverflowException">The aligned backing exceeds <see cref="int"/> bytes.</exception>
    public static MutableFlexibleArray<THeader, T> Create(in THeader header, ReadOnlySpan<T> elements)
        => Build(AllocatePinnedAligned(FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length)), header, elements);

    /// <summary>
    /// Builds a complete object in storage obtained from an allocator. Storage lifetime
    /// belongs to the allocator; the view keeps whatever it returned reachable. Alignment is
    /// the allocator's responsibility. The tail length is derived from the element count —
    /// stamped when the header registers a write-back, validated against the declared field
    /// otherwise.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="allocator"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The allocator returned less storage than required, or a header without a registered
    /// write-back declares a tail length that does not match the number of elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public static MutableFlexibleArray<THeader, T> Create(Func<int, Memory<byte>> allocator, in THeader header, ReadOnlySpan<T> elements)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        var byteLength = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);
        var buffer = allocator(byteLength);
        if (buffer.Length < byteLength)
        {
            throw new ArgumentException($"The allocator returned {buffer.Length} bytes but at least {byteLength} are required.", nameof(allocator));
        }

        return Build(buffer, header, elements);
    }

    /// <summary>
    /// Builds a complete object over an existing buffer: header first, then the tail, then the
    /// count verification. The tail length is derived from the element count — stamped when
    /// the header registers a write-back, validated against the declared field otherwise.
    /// Alignment is the buffer's responsibility.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The buffer is too short, or a header without a registered write-back declares a tail
    /// length that does not match the number of elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    public static MutableFlexibleArray<THeader, T> Create(Memory<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
        => Build(buffer, header, elements);

    /// <summary>
    /// Creates a writable view over an existing buffer. The buffer must be at least as long as
    /// the required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static MutableFlexibleArray<THeader, T> Create(Memory<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        return new MutableFlexibleArray<THeader, T>(buffer, length, owner: null);
    }

    /// <summary>
    /// Returns a copy of this object that releases the given owner's storage when disposed.
    /// </summary>
    internal MutableFlexibleArray<THeader, T> WithOwner(IMemoryOwner<byte> owner)
        => new MutableFlexibleArray<THeader, T>(Buffer, Length, owner);

    /// <summary>Converts the object to its writable projection; a null object yields a default, empty view.</summary>
    public static implicit operator MutableFlexibleSpan<THeader, T>(MutableFlexibleArray<THeader, T>? array) => array is null ? default : array.Span;

    /// <summary>Gets a reference to the header.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public new ref THeader Header
    {
        get
        {
            ThrowIfDisposed();
            return ref Unsafe.AsRef(in GetHeaderReference());
        }
    }

    /// <summary>Gets a writable projection of the committed object.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <remarks>
    /// The projection is a snapshot: its capacity is the committed length at the time of the
    /// read, and its own length reads keep the captured value — after the view itself
    /// truncates, re-appending fits within the snapshot. Length changes through it stamp the
    /// shared header and are reported back to this object, so the committed length stays in
    /// lockstep; take a fresh projection to see the new length in the view itself. A held
    /// projection is invalid once this object is disposed: the storage may be released.
    /// </remarks>
    public new MutableFlexibleSpan<THeader, T> Span
    {
        get
        {
            ThrowIfDisposed();
            return new MutableFlexibleSpan<THeader, T>(ref Unsafe.AsRef(in GetHeaderReference()), Length, Length, this);
        }
    }

    // Builds the complete object at the start of the buffer through the span constructor,
    // which owns the buffer check, the stamp-or-validate rule, and the count verification.
    private static MutableFlexibleArray<THeader, T> Build(Memory<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
    {
        _ = MutableFlexibleSpan<THeader, T>.Create(buffer.Span, header, elements);
        return new MutableFlexibleArray<THeader, T>(buffer, elements.Length, owner: null);
    }

    /// <summary>
    /// Allocates managed backing on the pinned object heap with the byte length pre-sized for
    /// a tail capacity, and starts an empty object in it: the header is written with the tail
    /// length stamped to zero, leaving the whole capacity free for
    /// <see cref="Append"/>, <see cref="AppendRange"/>, and <see cref="Advance"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// A header without a registered write-back declares a nonzero tail length.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The header's <c>WithTailElementCount</c> and <c>GetTailElementCount</c> disagree.
    /// </exception>
    /// <exception cref="OverflowException">The aligned backing exceeds <see cref="int"/> bytes.</exception>
    public static MutableFlexibleArray<THeader, T> Create(in THeader header, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        var memory = AllocatePinnedAligned(FlexibleArrayLayout<THeader, T>.GetRequiredBytes(capacity));

        ref var firstByte = ref MemoryMarshal.GetReference(memory.Span);
        ref var destination = ref Unsafe.As<byte, THeader>(ref firstByte);
        FlexibleArrayConstruction.WriteObject(ref destination, header, ReadOnlySpan<T>.Empty);
        return new MutableFlexibleArray<THeader, T>(memory, 0, owner: null);
    }

    // Allocates on the pinned object heap with alignment slack and returns the window starting
    // at the first aligned position. The array never moves, so the alignment pins for the
    // object's whole lifetime.
    private static Memory<byte> AllocatePinnedAligned(int byteLength)
    {
        var alignment = FlexibleArrayLayout<THeader, T>.RequiredAlignment;
        var allocatedLength = byteLength + (long)alignment - 1;
        if (allocatedLength > int.MaxValue)
        {
            throw new OverflowException($"The flexible array needs {allocatedLength} bytes of backing, which exceeds int.MaxValue.");
        }

        var array = GC.AllocateArray<byte>((int)allocatedLength, pinned: true);
        ref var start = ref MemoryMarshal.GetArrayDataReference(array);
        var address = (nuint)Unsafe.AsPointer(ref start);
        var offset = (int)(((address + alignment - 1) & ~(alignment - 1)) - address);
        return new Memory<byte>(array, offset, byteLength);
    }
}
