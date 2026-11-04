using System.Buffers.Binary;

namespace FlexibleArray.Tests;

/// <summary>
/// Verifies the buffer-wrapping view entry: the tail length derives from the header contract
/// and is validated against the buffer extent.
/// </summary>
public sealed class FlexibleSpanBufferTests
{
    [Fact]
    public void Wrap_CountField_ReadsLengthFromHeader()
    {
        var buffer = BuildPacket(3, 7, 8, 9);

        var view = FlexibleSpan<PacketHeader, int>.Wrap(buffer);
        Assert.Equal(3, view.Length);
        Assert.Equal(FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3), view.ByteLength);
        Assert.NotEqual(FlexibleArrayLayout<PacketHeader, int>.HeaderSize, view.ByteLength);
        Assert.Equal(new[] { 7, 8, 9 }, view.Elements.ToArray());
        Assert.Equal(3, view.Header.Count);
    }

    [Fact]
    public void Wrap_RejectsNegativeHeaderCount()
    {
        var buffer = BuildPacket(-1, 7, 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexibleSpan<PacketHeader, int>.Wrap(buffer));
    }

    [Fact]
    public void Wrap_RejectsHeaderCountBeyondCapacity()
    {
        var buffer = BuildPacket(5, 7, 8);
        Assert.Throws<ArgumentException>(() => FlexibleSpan<PacketHeader, int>.Wrap(buffer));
    }

    [Fact]
    public void Wrap_RejectsBufferShorterThanHeader()
    {
        var buffer = new byte[8];
        Assert.Throws<ArgumentException>(() => FlexibleSpan<PacketHeader, int>.Wrap(buffer));
    }

    [Fact]
    public void Wrap_SentinelWalk_DerivesLengthFromTheHeader()
    {
        // A sentinel producer writes the bytes itself: tag, then elements up to and including
        // the zero terminator — which is why the buffer is sized for three elements.
        Span<byte> buffer = new byte[FlexibleArrayLayout<SentinelHeader, int>.GetRequiredBytes(3)];
        buffer[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[(int)FlexibleArrayLayout<SentinelHeader, int>.FirstElementOffset..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[((int)FlexibleArrayLayout<SentinelHeader, int>.FirstElementOffset + 4)..], 2);

        var view = FlexibleSpan<SentinelHeader, int>.Wrap(buffer);

        Assert.Equal(2, view.Length);
        Assert.Equal(new[] { 1, 2 }, view.Elements.ToArray());
    }

    private static byte[] BuildPacket(int count, params int[] elements)
    {
        var buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(elements.Length)];
        var view = new MutableFlexibleSpan<PacketHeader, int>(buffer, new PacketHeader(), elements);
        view.Header = new PacketHeader { Count = count };
        elements.AsSpan().CopyTo(view.Elements);
        return buffer;
    }
}
