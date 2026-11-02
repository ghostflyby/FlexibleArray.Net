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
    private readonly void* _pointer;
    private readonly delegate* managed<void*, void> _release;
    private bool _disposed;
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

    internal FlexibleArrayCore(void* pointer, delegate* managed<void*, void> release, int length)
    {
        _pointer = pointer;
        _release = release;
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
                return ref *(THeader*)_pointer;
            }

            return ref Unsafe.As<byte, THeader>(ref MemoryMarshal.GetReference(_memory.Span));
        }
    }

    internal bool IsDisposed => _disposed;

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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

    internal void Advance(int elementCount)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        if (elementCount == 0)
        {
            return;
        }

        if (elementCount > Capacity - _committedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount),
                $"The advance is {elementCount} elements but the free capacity is {Capacity - _committedLength}.");
        }

        ref var header = ref HeaderReference;
        FlexibleArrayConstruction.SetLength<THeader, T>(ref header, _committedLength + elementCount);
        _committedLength += elementCount;
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_owner is not null)
        {
            _owner.Dispose();
        }
        else if (_release != null)
        {
            _release(_pointer);
        }
    }
}
