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
/// <see cref="System.Runtime.InteropServices.NativeMemory"/>, exposed as a
/// <see cref="Memory{T}"/> through an internal memory manager, and must be released via
/// <see cref="Dispose"/>. The current prototype registers no finalizer; ownership semantics
/// remain open.
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public sealed unsafe class FlexibleArray<THeader, T> : IDisposable
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly NativeMemoryManager _manager;
    private readonly Memory<byte> _memory;
    private readonly int _length;
    private readonly int _byteLength;

    private FlexibleArray(NativeMemoryManager manager, int length, int byteLength)
    {
        _manager = manager;
        _memory = manager.Memory;
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
            Span<byte> buffer = _memory.Span;
            ref byte firstByte = ref MemoryMarshal.GetReference(buffer);
            return new FlexibleArraySpan<THeader, T>(ref Unsafe.As<byte, THeader>(ref firstByte), _length);
        }
    }

    /// <summary>Gets a Memory-backed writable view that can be held long-term.</summary>
    public FlexibleArrayMemory<THeader, T> Memory => new(_memory, _length);

    /// <summary>
    /// Returns a pointer to the start of the contiguous object memory. The pointer borrows the
    /// native allocation and becomes invalid once the owner is disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public void* DangerousGetPointer()
        => _manager.Pointer;

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
        nuint alignment = FlexibleArrayLayout<THeader, T>.RequiredAlignment;
        void* pointer = NativeMemory.AlignedAlloc((nuint)byteLength, alignment);
        NativeMemoryManager manager = new(pointer, byteLength);

        Span<byte> buffer = manager.Memory.Span;
        ref byte firstByte = ref MemoryMarshal.GetReference(buffer);
        ref THeader written = ref Unsafe.As<byte, THeader>(ref firstByte);
        written = header;
        ref T firstElement = ref THeader.GetFirstElement(ref written);
        elements.CopyTo(MemoryMarshal.CreateSpan(ref firstElement, elements.Length));

        if (THeader.GetTailLength(in written) != elements.Length)
        {
            manager.Dispose();
            throw new ArgumentException(
                $"The header-declared tail length does not match the {elements.Length} elements provided.");
        }

        return new FlexibleArray<THeader, T>(manager, elements.Length, byteLength);
    }

    /// <inheritdoc />
    public void Dispose() => _manager.Dispose();
}
