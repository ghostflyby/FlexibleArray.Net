using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// A fixed-length flexible array object owning contiguous unmanaged storage; header and tail
/// follow the first-element placeholder field contract.
/// </summary>
/// <remarks>
/// Storage is an aligned unmanaged allocation from
/// <see cref="System.Runtime.InteropServices.NativeMemory"/> and must be released via
/// <see cref="Dispose"/>. The current prototype registers no finalizer; ownership semantics
/// remain open.
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public sealed unsafe class FlexibleArray<THeader, T> : IDisposable
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private void* _pointer;
    private readonly int _length;
    private readonly int _byteLength;

    private FlexibleArray(void* pointer, int length, int byteLength)
    {
        _pointer = pointer;
        _length = length;
        _byteLength = byteLength;
    }

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _length;

    /// <summary>Gets the total number of allocated bytes.</summary>
    public int ByteLength => _byteLength;

    /// <summary>Gets a writable view of the complete object.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public FlexibleArraySpan<THeader, T> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_pointer is null, this);
            return new FlexibleArraySpan<THeader, T>(ref Unsafe.AsRef<THeader>(_pointer), _length);
        }
    }

    /// <summary>Gets a writable view that can be held long-term.</summary>
    public FlexibleArrayMemory<THeader, T> Memory => new(this);

    /// <summary>
    /// Returns a pointer to the start of the contiguous object memory. The pointer borrows the
    /// native allocation and becomes invalid once the owner is disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public void* DangerousGetPointer()
    {
        ObjectDisposedException.ThrowIf(_pointer is null, this);
        return _pointer;
    }

    /// <summary>
    /// Creates a complete object from a header and a tail. The initialization order is fixed:
    /// the header (including the placeholder field) is written first, then the tail elements
    /// are copied, so the header write never overwrites elements already in place. Afterwards
    /// the header-declared tail length is validated to match the number of elements provided.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The header-declared tail length does not match the number of elements.
    /// </exception>
    public static FlexibleArray<THeader, T> Create(in THeader header, ReadOnlySpan<T> elements)
    {
        int byteLength = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(elements.Length);
        nuint alignment = ComputeAlignment();
        void* pointer = NativeMemory.AlignedAlloc((nuint)byteLength, alignment);

        *(THeader*)pointer = header;
        ref T firstElement = ref THeader.GetFirstElement(ref *(THeader*)pointer);
        elements.CopyTo(MemoryMarshal.CreateSpan(ref firstElement, elements.Length));

        FlexibleArraySpan<THeader, T> created = new(ref Unsafe.AsRef<THeader>(pointer), elements.Length);
        if (THeader.GetTailLength(created) != elements.Length)
        {
            NativeMemory.AlignedFree(pointer);
            throw new ArgumentException(
                $"The header-declared tail length does not match the {elements.Length} elements provided.");
        }

        return new FlexibleArray<THeader, T>(pointer, elements.Length, byteLength);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        void* pointer = _pointer;
        if (pointer is not null)
        {
            _pointer = null;
            NativeMemory.AlignedFree(pointer);
        }
    }

    // Alignment requirements cannot be queried at runtime; allocate conservatively at the
    // smallest power of two not less than max(sizeof(THeader), sizeof(T)), with a floor of 8.
    // No field of the header can require more alignment than that, and the elements sit at
    // legal field offsets within the header, so they are aligned as well.
    private static nuint ComputeAlignment()
    {
        int maxSize = Math.Max(Unsafe.SizeOf<THeader>(), Unsafe.SizeOf<T>());
        return Math.Max(8, BitOperations.RoundUpToPowerOf2((uint)maxSize));
    }
}
