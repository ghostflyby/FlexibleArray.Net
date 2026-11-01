using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// A writable, long-holdable flexible array object backed by a <see cref="Memory{T}"/> of
/// bytes.
/// </summary>
/// <remarks>
/// Like any <see cref="Memory{T}"/>, the view holds no owner: storage comes from an allocator,
/// from an internal array on the pinned object heap, or from an existing buffer, and its
/// lifetime follows the source. The view can be stored in fields and held across asynchronous
/// boundaries.
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public readonly struct FlexibleArrayMemory<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly Memory<byte> _buffer;
    private readonly int _length;

    internal FlexibleArrayMemory(Memory<byte> buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _length;

    /// <summary>Gets the backing buffer; used by the interop marshaller to pin the object.</summary>
    internal Memory<byte> Buffer => _buffer;

    /// <summary>Projects a fresh Span view on each call.</summary>
    /// <exception cref="InvalidOperationException">The view is default-constructed and has no backing storage.</exception>
    public FlexibleArraySpan<THeader, T> Span
    {
        get
        {
            ThrowIfDefault();
            Span<byte> buffer = _buffer.Span;
            ref byte firstByte = ref MemoryMarshal.GetReference(buffer);
            return new FlexibleArraySpan<THeader, T>(ref Unsafe.As<byte, THeader>(ref firstByte), _length);
        }
    }

    /// <summary>
    /// Creates a view over an existing buffer. The buffer must be at least as long as the
    /// required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static FlexibleArrayMemory<THeader, T> Create(Memory<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        int requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        return new FlexibleArrayMemory<THeader, T>(buffer, length);
    }

    /// <summary>
    /// Builds a complete object in storage obtained from an allocator: the header is written
    /// first, then the tail elements are copied, and the header-declared tail length is
    /// validated against the number of elements. Storage lifetime belongs to the allocator;
    /// the view keeps whatever it returned reachable. Alignment is the allocator's
    /// responsibility.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="allocator"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The allocator returned less storage than required, or the header-declared tail length
    /// does not match the number of elements.
    /// </exception>
    public static FlexibleArrayMemory<THeader, T> Create(Func<int, Memory<byte>> allocator, in THeader header, ReadOnlySpan<T> elements)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        int byteLength = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);
        Memory<byte> buffer = allocator(byteLength);
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
    public static FlexibleArrayMemory<THeader, T> Create(Memory<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
    {
        int byteLength = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);
        if (buffer.Length < byteLength)
        {
            throw new ArgumentException($"The buffer needs at least {byteLength} bytes but has {buffer.Length}.", nameof(buffer));
        }

        return Build(buffer, header, elements);
    }

    /// <summary>
    /// Builds a complete object with managed backing: the array is allocated on the pinned
    /// object heap with extra bytes, and the object starts at the required alignment no matter
    /// where the runtime places the allocation. The backing is garbage-collected normally once
    /// no view keeps it reachable; there is nothing to dispose.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The header-declared tail length does not match the number of elements.
    /// </exception>
    /// <exception cref="OverflowException">The aligned backing exceeds <see cref="int"/> bytes.</exception>
    public static unsafe FlexibleArrayMemory<THeader, T> Create(in THeader header, ReadOnlySpan<T> elements)
    {
        int byteLength = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);
        nuint alignment = FlexibleArrayLayout<THeader, T>.RequiredAlignment;
        long allocatedLength = (long)byteLength + (long)alignment - 1;
        if (allocatedLength > int.MaxValue)
        {
            throw new OverflowException($"The flexible array needs {allocatedLength} bytes of backing, which exceeds int.MaxValue.");
        }

        byte[] array = GC.AllocateArray<byte>((int)allocatedLength, pinned: true);

        // The array never moves, so advancing the window to the first aligned position pins
        // the alignment for the object's whole lifetime.
        ref byte start = ref MemoryMarshal.GetArrayDataReference(array);
        nuint address = (nuint)Unsafe.AsPointer(ref start);
        int offset = (int)(((address + alignment - 1) & ~(alignment - 1)) - address);
        return Build(new Memory<byte>(array, offset, byteLength), header, elements);
    }

    // Builds the complete object at the start of an already-validated buffer: header first,
    // then the tail, then the declared-length consistency check.
    private static FlexibleArrayMemory<THeader, T> Build(Memory<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
    {
        FlexibleArraySpan<THeader, T> view = FlexibleArraySpan<THeader, T>.Create(buffer.Span, elements.Length);
        view.Header = header;
        elements.CopyTo(view.Elements);
        if (THeader.GetTailLength(view.Header) != elements.Length)
        {
            throw new ArgumentException(
                $"The header-declared tail length does not match the {elements.Length} elements provided.");
        }

        return new FlexibleArrayMemory<THeader, T>(buffer, elements.Length);
    }

    /// <summary>Converts to a read-only view.</summary>
    public static implicit operator ReadOnlyFlexibleArrayMemory<THeader, T>(FlexibleArrayMemory<THeader, T> memory)
        => new(memory._buffer, memory._length);

    /// <summary>
    /// Projects a short-lived Span view over the same storage. Implicit because narrowing a
    /// long-holdable view to a stack-only one is always safe under ref safety rules.
    /// </summary>
    public static implicit operator FlexibleArraySpan<THeader, T>(FlexibleArrayMemory<THeader, T> memory)
        => memory.Span;

    private void ThrowIfDefault()
    {
        if (_buffer.IsEmpty)
        {
            throw new InvalidOperationException("A default-constructed FlexibleArrayMemory has no backing storage.");
        }
    }
}
