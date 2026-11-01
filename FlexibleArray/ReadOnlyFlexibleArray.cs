using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FlexibleArray;

/// <summary>
/// A read-only, long-holdable flexible array object: storage, projections, and — when the
/// backing has one — the lifetime that releases it, in a single object.
/// </summary>
/// <remarks>
/// Storage comes from an aligned managed array, an allocator, an existing buffer, a pool
/// rental, or an adopted native allocation; disposal routes it back to its source. The
/// writable <see cref="FlexibleArray{THeader, T}"/> derives from this type, so handing out a
/// read-only reference is a free reference conversion. Ownerless managed storage has its
/// finalizer suppressed at construction; pool rentals and adopted native objects keep one as
/// a leak safety net. Projections are invisible to the GC: keep a reference to this object
/// alive while spans are in use.
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public unsafe class ReadOnlyFlexibleArray<THeader, T> : IDisposable
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly int _length;
    private readonly ReadOnlyMemory<byte> _memory;
    private readonly IMemoryOwner<byte>? _owner;
    private readonly void* _pointer;
    private readonly delegate* managed<void*, void> _release;
    private bool _disposed;

    internal ReadOnlyFlexibleArray(ReadOnlyMemory<byte> buffer, int length, IMemoryOwner<byte>? owner)
    {
        _length = length;
        _memory = buffer;
        _owner = owner;
        if (owner is null)
        {
            GC.SuppressFinalize(this);
        }
    }

    internal ReadOnlyFlexibleArray(void* pointer, delegate* managed<void*, void> release, int length)
    {
        _length = length;
        _pointer = pointer;
        _release = release;
    }

    /// <summary>
    /// Creates a read-only view over an existing buffer. The buffer must be at least as long
    /// as the required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static ReadOnlyFlexibleArray<THeader, T> Create(ReadOnlyMemory<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        int requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        return new ReadOnlyFlexibleArray<THeader, T>(buffer, length, owner: null);
    }

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _length;

    /// <summary>Gets the backing buffer; used to carry owners through construction.</summary>
    internal ReadOnlyMemory<byte> Buffer => _memory;

    /// <summary>Gets a read-only reference to the header.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public ref readonly THeader Header
    {
        get
        {
            ThrowIfDisposed();
            return ref GetHeaderReference();
        }
    }

    /// <summary>Gets a read-only projection of the complete object.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public ReadOnlyFlexibleArraySpan<THeader, T> ReadOnlySpan
    {
        get
        {
            ThrowIfDisposed();
            ReadOnlyFlexibleArraySpan<THeader, T> view = new(in GetHeaderReference(), _length);
            return view;
        }
    }

    /// <summary>
    /// Pins the backing storage and returns a handle to the object's start — for native APIs
    /// that retain the pointer beyond a single call.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public MemoryHandle Pin()
    {
        ThrowIfDisposed();
        return _pointer != null ? new MemoryHandle(_pointer) : _memory.Pin();
    }

    /// <summary>
    /// Releases the backing storage to its source: pool rentals return to their pool, adopted
    /// native objects release through the header-registered policy, ownerless managed storage
    /// collects with the GC.
    /// </summary>
    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the backing storage; the finalizer safety net for missing Dispose.</summary>
    ~ReadOnlyFlexibleArray()
    {
        Release();
    }

    private protected ref readonly THeader GetHeaderReference()
    {
        if (_pointer != null)
        {
            return ref *(THeader*)_pointer;
        }

        return ref Unsafe.As<byte, THeader>(ref MemoryMarshal.GetReference(_memory.Span));
    }

    private protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

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
