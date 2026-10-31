using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray;

/// <summary>
/// A writable, long-holdable view of a flexible array object backed by a
/// <see cref="Memory{T}"/> of bytes.
/// </summary>
/// <remarks>
/// Like any <see cref="Memory{T}"/>, the view can be stored in fields and held across
/// asynchronous boundaries, and can be created over the owner's allocation or over
/// user-provided managed memory via <see cref="Create(Memory{byte}, int)"/>. If the backing
/// storage comes from <see cref="FlexibleArray{THeader, T}"/>, disposing the owner invalidates
/// the view: accessing <see cref="Span"/> then throws <see cref="ObjectDisposedException"/>.
/// </remarks>
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

    /// <summary>Projects a fresh Span view on each call.</summary>
    /// <exception cref="InvalidOperationException">The view is default-constructed and has no backing storage.</exception>
    /// <exception cref="ObjectDisposedException">The backing owner has been disposed.</exception>
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
