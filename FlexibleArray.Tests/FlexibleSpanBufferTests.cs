using System.Buffers.Binary;

namespace FlexibleArray.Tests;

/// <summary>
/// Verifies the buffer-wrapping view entry: the tail length derives from the header contract
/// and is validated against the buffer extent.
/// </summary>
public sealed class FlexibleSpanBufferTests
{
    [Fact]
    public void Create_CountField_ReadsLengthFromHeader()
    {
        var buffer = BuildPacket(2, 7, 8);

        var view = FlexibleSpan<PacketHeader, int>.Create(buffer);
        Assert.Equal(2, view.Length);
        Assert.Equal(new[] { 7, 8 }, view.Elements.ToArray());
        Assert.Equal(2, view.Header.Count);
    }

    [Fact]
    public void Create_RejectsNegativeHeaderCount()
    {
        var buffer = BuildPacket(-1, 7, 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexibleSpan<PacketHeader, int>.Create(buffer));
    }

    [Fact]
    public void Create_RejectsHeaderCountBeyondCapacity()
    {
        var buffer = BuildPacket(5, 7, 8);
        Assert.Throws<ArgumentException>(() => FlexibleSpan<PacketHeader, int>.Create(buffer));
    }

    [Fact]
    public void Create_RejectsBufferShorterThanHeader()
    {
        var buffer = new byte[8];
        Assert.Throws<ArgumentException>(() => FlexibleSpan<PacketHeader, int>.Create(buffer));
    }

    [Fact]
    public void Create_SentinelWalk_DerivesLengthFromTheHeader()
    {
        // A sentinel producer writes the bytes itself: tag, then elements up to and including
        // the zero terminator — which is why the buffer is sized for three elements.
        Span<byte> buffer = new byte[FlexibleArrayLayout<SentinelHeader, int>.GetRequiredBytes(3)];
        buffer[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[(int)FlexibleArrayLayout<SentinelHeader, int>.FirstElementOffset..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[((int)FlexibleArrayLayout<SentinelHeader, int>.FirstElementOffset + 4)..], 2);

        var view = FlexibleSpan<SentinelHeader, int>.Create(buffer);

        Assert.Equal(2, view.Length);
        Assert.Equal(new[] { 1, 2 }, view.Elements.ToArray());
    }

    private static byte[] BuildPacket(int count, params int[] elements)
    {
        var buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(elements.Length)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), elements);
        view.Header = new PacketHeader { Count = count };
        elements.AsSpan().CopyTo(view.Elements);
        return buffer;
    }
}
