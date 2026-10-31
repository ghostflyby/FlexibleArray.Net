using System.Buffers;
using System.Runtime.InteropServices;

namespace FlexibleArray;

/// <summary>
/// Wraps an aligned native allocation as a <see cref="MemoryManager{T}"/> of bytes so that
/// Memory-based views can be held long-term.
/// </summary>
/// <remarks>
/// The wrapped memory never moves, so pinning is a no-op. Disposal frees the allocation and
/// makes every further projection throw <see cref="ObjectDisposedException"/>.
/// </remarks>
internal sealed unsafe class NativeMemoryManager : MemoryManager<byte>
{
    private void* _pointer;
    private readonly int _length;

    public NativeMemoryManager(void* pointer, int length)
    {
        _pointer = pointer;
        _length = length;
    }

    public void* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_pointer is null, this);
            return _pointer;
        }
    }

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_pointer is null, this);
        return new Span<byte>(_pointer, _length);
    }

    public override MemoryHandle Pin(int elementIndex)
    {
        ObjectDisposedException.ThrowIf(_pointer is null, this);
        return new MemoryHandle((byte*)_pointer + elementIndex);
    }

    public override void Unpin()
    {
    }

    // MemoryManager<T> implements IDisposable explicitly; expose the disposal entry.
    public void Dispose() => Dispose(disposing: true);

    protected override void Dispose(bool disposing)
    {
        void* pointer = _pointer;
        if (pointer is not null)
        {
            _pointer = null;
            NativeMemory.AlignedFree(pointer);
        }
    }
}
