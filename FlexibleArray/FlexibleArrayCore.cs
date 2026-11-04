using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray;

internal sealed unsafe class FlexibleArrayCore<THeader, T>
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

    internal bool IsDisposed => _committedLength < 0;

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_committedLength < 0, this);

    internal MemoryHandle Pin()
    {
        ThrowIfDisposed();
        return _pointer != null ? new MemoryHandle(_pointer) : _memory.Pin();
    }

    internal void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    internal void OnLengthStamped(int value) => _committedLength = value;

    internal void Append(T item)
    {
        ThrowIfDisposed();
        var length = _committedLength;
        if (length == Capacity)
        {
            throw new InvalidOperationException("The flexible array is at capacity.");
        }

        ref var header = ref HeaderReference;
        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, length + 1);
        Unsafe.Add(ref THeader.GetFirstElement(ref header), length) = item;
        _committedLength = length + 1;
    }

    internal void AppendRange(ReadOnlySpan<T> items)
    {
        ThrowIfDisposed();
        if (items.Length > Capacity - _committedLength)
        {
            throw new InvalidOperationException($"The free capacity is {Capacity - _committedLength} elements but {items.Length} were given.");
        }

        ref var header = ref HeaderReference;
        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, _committedLength + items.Length);
        items.CopyTo(MemoryMarshal.CreateSpan(
            ref Unsafe.Add(ref THeader.GetFirstElement(ref header), _committedLength), items.Length));
        _committedLength += items.Length;
    }

    internal void Truncate(int length)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > _committedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length),
                $"The truncate target {length} exceeds the committed length {_committedLength}.");
        }

        if (length == _committedLength)
        {
            return;
        }

        ref var header = ref HeaderReference;
        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, length);
        _committedLength = length;
    }

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
