using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// A writable, long-holdable flexible array object: storage, projections, and — when the
/// backing has one — the lifetime that releases it, in a single object.
/// </summary>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public sealed unsafe class MutableFlexibleArray<THeader, T> : IDisposable
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly FlexibleArrayCore<THeader, T> _core;

    internal MutableFlexibleArray(FlexibleArrayCore<THeader, T> core)
    {
        _core = core;
    }

    /// <summary>Gets the committed number of tail elements.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public int Length
    {
        get
        {
            _core.ThrowIfDisposed();
            return _core.CommittedLength;
        }
    }

    /// <summary>Gets the element capacity of the backing storage; adopted native objects report their committed length.</summary>
    /// <remarks>Can exceed the committed count: header padding and pool-rental rounding are usable.</remarks>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public int Capacity
    {
        get
        {
            _core.ThrowIfDisposed();
            return _core.Capacity;
        }
    }

    /// <summary>Gets the writable region between the committed length and the capacity, for in-place construction before <see cref="Advance"/>.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public Span<T> FreeElements
    {
        get
        {
            _core.ThrowIfDisposed();
            ref var header = ref _core.HeaderReference;
            return MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref THeader.GetFirstElement(ref header), Length), Capacity - Length);
        }
    }

    /// <summary>Appends one element and updates the header's length field.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The object is at capacity.</exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public void Append(T item) => _core.Append(item);

    /// <summary>Appends a range of elements and updates the header's length field.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The range does not fit in the free capacity.</exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public void AppendRange(ReadOnlySpan<T> items) => _core.AppendRange(items);

    /// <summary>
    /// Commits elements that were written in place through <see cref="FreeElements"/>, for
    /// nested objects built at the tail, and updates the header's length field.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="elementCount"/> is negative or exceeds the free capacity.
    /// </exception>
    /// <exception cref="NotSupportedException">The header registers no tail-length write-back.</exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public void Advance(int elementCount) => _core.Advance(elementCount);

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
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    public void Truncate(int length) => _core.Truncate(length);

    /// <summary>
    /// Builds a complete object on the pinned object heap, starting at the required
    /// alignment. The tail length is derived from the element count.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A header without a registered write-back declares a tail length that does not match the
    /// number of elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    /// <exception cref="OverflowException">The aligned backing exceeds <see cref="int"/> bytes.</exception>
    public static MutableFlexibleArray<THeader, T> Create(in THeader header, ReadOnlySpan<T> elements)
        => Build(AllocatePinnedAligned(FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length)), header, elements);

    /// <summary>
    /// Builds a complete object in allocator-provided storage; the alignment and the
    /// storage's lifetime belong to the allocator. The tail length is derived from the
    /// element count.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="allocator"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The allocator returned less storage than required, or a header without a registered
    /// write-back declares a tail length that does not match the number of elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    /// <exception cref="OverflowException">The required backing exceeds <see cref="int"/> bytes.</exception>
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
    /// Builds a complete object over an existing buffer; alignment is the buffer's
    /// responsibility. The tail length is derived from the element count.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The buffer is too short, or a header without a registered write-back declares a tail
    /// length that does not match the number of elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    /// <exception cref="OverflowException">The required backing exceeds <see cref="int"/> bytes.</exception>
    public static MutableFlexibleArray<THeader, T> Create(Memory<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
        => Build(buffer, header, elements);

    /// <summary>
    /// Creates a writable object over a buffer that already holds a complete object: the
    /// buffer is not written, and the tail length is derived from the header contract and
    /// validated against the buffer extent. Alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The header contract derives a negative tail length.</exception>
    /// <exception cref="ArgumentException">
    /// The buffer is too short for the header or for the derived tail length.
    /// </exception>
    public static MutableFlexibleArray<THeader, T> Create(Memory<byte> buffer)
    {
        if (buffer.Length < FlexibleArrayLayout<THeader, T>.HeaderSize)
        {
            throw new ArgumentException(
                $"The buffer needs at least {FlexibleArrayLayout<THeader, T>.HeaderSize} bytes but has {buffer.Length}.", nameof(buffer));
        }

        ref var firstByte = ref MemoryMarshal.GetReference(buffer.Span);
        ref var header = ref Unsafe.As<byte, THeader>(ref firstByte);
        var length = THeader.GetTailElementCount(in header);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (requiredBytes > buffer.Length)
        {
            throw new ArgumentException(
                $"The buffer needs at least {requiredBytes} bytes for the derived tail length but has {buffer.Length}.", nameof(buffer));
        }

        return new MutableFlexibleArray<THeader, T>(new FlexibleArrayCore<THeader, T>(buffer, length, owner: null));
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
    /// <exception cref="InvalidOperationException">The header's length write-back and read-back disagree.</exception>
    /// <exception cref="OverflowException">The aligned backing exceeds <see cref="int"/> bytes.</exception>
    public static MutableFlexibleArray<THeader, T> Create(in THeader header, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        var memory = AllocatePinnedAligned(FlexibleArrayLayout<THeader, T>.GetRequiredBytes(capacity));

        ref var firstByte = ref MemoryMarshal.GetReference(memory.Span);
        ref var destination = ref Unsafe.As<byte, THeader>(ref firstByte);
        FlexibleArrayConstruction.WriteObject(ref destination, header, ReadOnlySpan<T>.Empty);
        return new MutableFlexibleArray<THeader, T>(new FlexibleArrayCore<THeader, T>(memory, 0, owner: null));
    }

    /// <summary>Converts the object to its writable projection; a null object yields a default, empty view.</summary>
    public static implicit operator MutableFlexibleSpan<THeader, T>(MutableFlexibleArray<THeader, T>? array)
        => array is null ? default : array.Span;

    /// <summary>Converts the object to its read-only whole-object view; a null object yields a default, empty view.</summary>
    public static implicit operator FlexibleSpan<THeader, T>(MutableFlexibleArray<THeader, T>? array)
        => array is null ? default : (FlexibleSpan<THeader, T>)array.Span;

    /// <summary>Converts the object to a read-only view of the same implementation.</summary>
    public static implicit operator FlexibleArray<THeader, T>(MutableFlexibleArray<THeader, T>? array)
        => array is null ? null! : new FlexibleArray<THeader, T>(array._core);

    /// <summary>Gets a reference to the header.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public ref THeader Header
    {
        get
        {
            _core.ThrowIfDisposed();
            return ref _core.HeaderReference;
        }
    }

    /// <summary>Gets a writable projection of the committed object.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    /// <remarks>
    /// A snapshot: its capacity is the committed length at the time of the read. Length
    /// changes through it move this object's committed length too.
    /// </remarks>
    public MutableFlexibleSpan<THeader, T> Span
    {
        get
        {
            _core.ThrowIfDisposed();
            return new MutableFlexibleSpan<THeader, T>(
                ref _core.HeaderReference, Length, Length, _core);
        }
    }

    /// <summary>
    /// Returns a copy of this object that releases the given owner's storage when disposed.
    /// </summary>
    internal MutableFlexibleArray<THeader, T> WithOwner(IMemoryOwner<byte> owner)
        => new(new FlexibleArrayCore<THeader, T>(_core.Buffer, Length, owner));

    internal MemoryHandle Pin() => _core.Pin();

    /// <summary>
    /// Releases the backing storage to its source: pool rentals return to their pool, adopted
    /// native objects release through the header-registered policy, ownerless managed storage
    /// collects with the GC.
    /// </summary>
    public void Dispose() => _core.Dispose();

    private static MutableFlexibleArray<THeader, T> Build(Memory<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
    {
        _ = MutableFlexibleSpan<THeader, T>.Create(buffer.Span, header, elements);
        return new MutableFlexibleArray<THeader, T>(new FlexibleArrayCore<THeader, T>(buffer, elements.Length, owner: null));
    }

    // The array never moves, so aligning the window once pins it for the object's lifetime.
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
