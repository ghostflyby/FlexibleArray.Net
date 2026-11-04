using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray;

internal sealed unsafe class FlexibleArrayCore<THeader, T> : IDisposable
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly ReadOnlyMemory<byte> _memory;
    private readonly IMemoryOwner<byte>? _owner;
    private readonly Action<FlexibleSpan<THeader, T>>? _release;
    private readonly THeader* _pointer;

    // Negative stores the bitwise complement of the committed length, which also marks the
    // object as disposed; zero is a valid length, so the sign is the only free channel.
    private int _committedLength;

    internal FlexibleArrayCore(ReadOnlyMemory<byte> buffer, int length, IMemoryOwner<byte>? owner)
    {
        _memory = buffer;
        _committedLength = length;
        _owner = owner;
        if (owner is null)
        {
            GC.SuppressFinalize(this);
        }
    }

    internal FlexibleArrayCore(THeader* pointer, Action<FlexibleSpan<THeader, T>> release, int length)
    {
        _release = release;
        _pointer = pointer;
        _committedLength = length;
    }

    internal int CommittedLength => _committedLength;

    internal ReadOnlyMemory<byte> Buffer => _memory;

    // Adopted native objects have no backing memory: their storage extent is unknown.
    internal int Capacity => _memory.IsEmpty
        ? _committedLength
        : (int)((_memory.Length - FlexibleArrayLayout<THeader, T>.FirstElementOffset) / FlexibleArrayLayout<THeader, T>.ElementSize);

    internal ref THeader HeaderReference
    {
        get
        {
            if (_pointer != null)
            {
                return ref *_pointer;
            }

            return ref Unsafe.As<byte, THeader>(ref MemoryMarshal.GetReference(_memory.Span));
        }
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_committedLength < 0, this);

    internal MemoryHandle Pin()
    {
        ThrowIfDisposed();
        return _pointer != null ? new MemoryHandle(_pointer) : _memory.Pin();
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    internal void OnLengthStamped(int value) => _committedLength = value;

    ~FlexibleArrayCore()
    {
        Release();
    }

    private void Release()
    {
        if (_committedLength < 0)
        {
            return;
        }

        _committedLength = ~_committedLength;
        if (_owner is not null)
        {
            _owner.Dispose();
        }
        else if (_release is not null)
        {
            _release.Invoke(new FlexibleSpan<THeader, T>(in *_pointer));
        }
    }
}
