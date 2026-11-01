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
/// Lifetime semantics are inherited from <see cref="ReadOnlyFlexibleArray{THeader, T}"/>;
/// construction happens here because writing requires the writable shape.
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public sealed unsafe class FlexibleArray<THeader, T> : ReadOnlyFlexibleArray<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private FlexibleArray(ReadOnlyMemory<byte> buffer, int length, IMemoryOwner<byte>? owner)
        : base(buffer, length, owner)
    {
    }

    internal FlexibleArray(void* pointer, delegate* managed<void*, void> release, int length)
        : base(pointer, release, length)
    {
    }

    /// <summary>
    /// Builds a complete object with managed backing: the array is allocated on the pinned
    /// object heap with extra bytes, and the object starts at the required alignment no matter
    /// where the runtime places the allocation.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The header-declared tail length does not match the number of elements.
    /// </exception>
    /// <exception cref="OverflowException">The aligned backing exceeds <see cref="int"/> bytes.</exception>
    public static FlexibleArray<THeader, T> Create(in THeader header, ReadOnlySpan<T> elements)
    {
        var byteLength = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);
        var alignment = FlexibleArrayLayout<THeader, T>.RequiredAlignment;
        var allocatedLength = byteLength + (long)alignment - 1;
        if (allocatedLength > int.MaxValue)
        {
            throw new OverflowException($"The flexible array needs {allocatedLength} bytes of backing, which exceeds int.MaxValue.");
        }

        var array = GC.AllocateArray<byte>((int)allocatedLength, pinned: true);

        // The array never moves, so advancing the window to the first aligned position pins
        // the alignment for the object's whole lifetime.
        ref var start = ref MemoryMarshal.GetArrayDataReference(array);
        var address = (nuint)Unsafe.AsPointer(ref start);
        var offset = (int)(((address + alignment - 1) & ~(alignment - 1)) - address);
        return Build(new Memory<byte>(array, offset, byteLength), header, elements);
    }

    /// <summary>
    /// Builds a complete object in storage obtained from an allocator. Storage lifetime
    /// belongs to the allocator; the view keeps whatever it returned reachable. Alignment is
    /// the allocator's responsibility.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="allocator"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The allocator returned less storage than required, or the header-declared tail length
    /// does not match the number of elements.
    /// </exception>
    public static FlexibleArray<THeader, T> Create(Func<int, Memory<byte>> allocator, in THeader header, ReadOnlySpan<T> elements)
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
    /// declared-length consistency check. Alignment is the buffer's responsibility.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The buffer is too short, or the header-declared tail length does not match the number of
    /// elements.
    /// </exception>
    public static FlexibleArray<THeader, T> Create(Memory<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
        => Build(buffer, header, elements);

    /// <summary>
    /// Creates a writable view over an existing buffer. The buffer must be at least as long as
    /// the required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static FlexibleArray<THeader, T> Create(Memory<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        return new FlexibleArray<THeader, T>(buffer, length, owner: null);
    }

    /// <summary>
    /// Returns a copy of this object that releases the given owner's storage when disposed.
    /// </summary>
    internal FlexibleArray<THeader, T> WithOwner(IMemoryOwner<byte> owner)
        => new FlexibleArray<THeader, T>(Buffer, Length, owner);

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

    /// <summary>Gets a writable projection of the complete object.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public FlexibleArraySpan<THeader, T> Span
    {
        get
        {
            ThrowIfDisposed();
            return new FlexibleArraySpan<THeader, T>(ref Unsafe.AsRef(in GetHeaderReference()), Length);
        }
    }

    // Builds the complete object at the start of an already-validated buffer: header first,
    // then the tail, then the declared-length consistency check.
    private static FlexibleArray<THeader, T> Build(Memory<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
    {
        var view = FlexibleArraySpan<THeader, T>.Create(buffer.Span, elements.Length);
        view.Header = header;
        elements.CopyTo(view.Elements);
        if (THeader.GetTailLength(view.Header) != elements.Length)
        {
            throw new ArgumentException(
                $"The header-declared tail length does not match the {elements.Length} elements provided.");
        }

        return new FlexibleArray<THeader, T>(buffer, elements.Length, owner: null);
    }
}
