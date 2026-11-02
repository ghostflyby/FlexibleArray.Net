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
/// Projections are invisible to the GC: keep a reference to this object alive while spans
/// are in use.
/// </remarks>
[NativeMarshalling(typeof(FlexibleArrayMarshaller<,>))]
public sealed class FlexibleArray<THeader, T> : IDisposable
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly FlexibleArrayCore<THeader, T> _core;

    internal FlexibleArray(FlexibleArrayCore<THeader, T> core)
    {
        _core = core;
    }

    /// <summary>
    /// Creates a read-only object over an existing buffer; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentException">The buffer is shorter than the header.</exception>
    public static FlexibleArray<THeader, T> Create(ReadOnlyMemory<byte> buffer)
    {
        if (buffer.Length < FlexibleArrayLayout<THeader, T>.HeaderSize)
        {
            throw new ArgumentException(
                $"The buffer needs at least {FlexibleArrayLayout<THeader, T>.HeaderSize} bytes but has {buffer.Length}.", nameof(buffer));
        }

        ref var firstByte = ref MemoryMarshal.GetReference(buffer.Span);
        ref var header = ref Unsafe.As<byte, THeader>(ref firstByte);
        var length = THeader.GetTailElementCount(in header);
        return new FlexibleArray<THeader, T>(new FlexibleArrayCore<THeader, T>(buffer, length, owner: null));
    }

    /// <summary>Gets the tail element count from the header's length field.</summary>
    public int Length => THeader.GetTailElementCount(in _core.HeaderReference);

    /// <summary>Gets a read-only reference to the header.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public ref readonly THeader Header
    {
        get
        {
            _core.ThrowIfDisposed();
            return ref _core.HeaderReference;
        }
    }

    /// <summary>Gets a read-only projection of the complete object.</summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public FlexibleSpan<THeader, T> Span
    {
        get
        {
            _core.ThrowIfDisposed();
            return new FlexibleSpan<THeader, T>(in _core.HeaderReference, Length);
        }
    }

    /// <summary>Converts the object to its whole-object view; a null object yields a default, empty view.</summary>
    public static implicit operator FlexibleSpan<THeader, T>(FlexibleArray<THeader, T>? array) => array is null ? default : array.Span;

    /// <summary>
    /// Pins the backing storage and returns a handle to the object's start — for native APIs
    /// that retain the pointer beyond a single call.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
    public MemoryHandle Pin() => _core.Pin();

    /// <summary>
    /// Releases the backing storage to its source: pool rentals return to their pool, adopted
    /// native objects release through the header-registered policy, ownerless managed storage
    /// collects with the GC.
    /// </summary>
    public void Dispose() => _core.Dispose();
}
