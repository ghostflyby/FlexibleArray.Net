using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray;

/// <summary>
/// A read-only, long-holdable view of a flexible array object backed by a
/// <see cref="ReadOnlyMemory{T}"/> of bytes.
/// </summary>
/// <remarks>Lifetime semantics match <see cref="FlexibleArrayMemory{THeader, T}"/>.</remarks>
public readonly struct ReadOnlyFlexibleArrayMemory<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly ReadOnlyMemory<byte> _buffer;
    private readonly int _length;

    internal ReadOnlyFlexibleArrayMemory(ReadOnlyMemory<byte> buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _length;

    /// <summary>Projects a fresh read-only Span view on each call.</summary>
    /// <exception cref="InvalidOperationException">The view is default-constructed and has no backing storage.</exception>
    public ReadOnlyFlexibleArraySpan<THeader, T> Span
    {
        get
        {
            ThrowIfDefault();
            ReadOnlySpan<byte> buffer = _buffer.Span;
            ref byte firstByte = ref MemoryMarshal.GetReference(buffer);
            return new ReadOnlyFlexibleArraySpan<THeader, T>(ref Unsafe.As<byte, THeader>(ref firstByte), _length);
        }
    }

    /// <summary>
    /// Projects a short-lived read-only Span view over the same storage. Implicit because
    /// narrowing a long-holdable view to a stack-only one is always safe under ref safety
    /// rules.
    /// </summary>
    public static implicit operator ReadOnlyFlexibleArraySpan<THeader, T>(ReadOnlyFlexibleArrayMemory<THeader, T> memory)
        => memory.Span;

    /// <summary>
    /// Creates a read-only view over an existing buffer. The buffer must be at least as long
    /// as the required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static ReadOnlyFlexibleArrayMemory<THeader, T> Create(ReadOnlyMemory<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        int requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        return new ReadOnlyFlexibleArrayMemory<THeader, T>(buffer, length);
    }

    private void ThrowIfDefault()
    {
        if (_buffer.IsEmpty)
        {
            throw new InvalidOperationException("A default-constructed ReadOnlyFlexibleArrayMemory has no backing storage.");
        }
    }
}
