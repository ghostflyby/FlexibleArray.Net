using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FlexibleArray;

/// <summary>
/// A writable, short-lived view of a complete flexible array object.
/// </summary>
/// <remarks>
/// A flexible array denotes a complete object and offers no slicing; use <see cref="Elements"/>
/// when a slice is needed. Assigning a whole value to <see cref="Header"/> writes the
/// first-element placeholder field together with any trailing header padding, overwriting the
/// region overlapped by the tail. Initialize by writing the header first, then the tail.
/// </remarks>
public readonly ref struct FlexibleArraySpan<THeader, T>
    where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
    where T : unmanaged
{
    private readonly ref THeader _header;
    private readonly int _length;

    internal FlexibleArraySpan(ref THeader header, int length)
    {
        _header = ref header;
        _length = length;
    }

    /// <summary>Gets the logical number of tail elements.</summary>
    public int Length => _length;

    /// <summary>Gets a reference to the header. For the overwrite risk of whole assignments, see the class remarks.</summary>
    public ref THeader Header => ref _header;

    /// <summary>Gets an element view covering the entire tail, including the first-element placeholder field.</summary>
    public Span<T> Elements => MemoryMarshal.CreateSpan(ref THeader.GetFirstElement(ref _header), _length);

    /// <summary>
    /// Creates a view over an existing buffer. The buffer must be at least as long as the
    /// required byte count; alignment is not validated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static FlexibleArraySpan<THeader, T> Create(Span<byte> buffer, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        int requiredBytes = FlexibleArrayLayout<THeader, T>.GetRequiredBytes(length);
        if (buffer.Length < requiredBytes)
        {
            throw new ArgumentException($"The buffer needs at least {requiredBytes} bytes but has {buffer.Length}.", nameof(buffer));
        }

        ref byte firstByte = ref MemoryMarshal.GetReference(buffer);
        return new FlexibleArraySpan<THeader, T>(ref Unsafe.As<byte, THeader>(ref firstByte), length);
    }

    /// <summary>Converts to a read-only view whose read-onlyness covers both the header and the tail.</summary>
    public static implicit operator ReadOnlyFlexibleArraySpan<THeader, T>(FlexibleArraySpan<THeader, T> span)
        => new(ref span._header, span._length);
}
