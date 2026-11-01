namespace FlexibleArray.Tests;

/// <summary>
/// Verifies self-describing parsing: the header contract derives the logical tail length from
/// the complete object, either from a count field or from a capacity-bounded sentinel walk.
/// </summary>
public sealed class FlexibleArrayParseTests
{
    [Fact]
    public void TryParse_CountField_ReadsLengthFromHeader()
    {
        var buffer = BuildPacket(2, 7, 8);

        Assert.True(ReadOnlyFlexibleArraySpan<PacketHeader, int>.TryParse(
            buffer, out var view));
        Assert.Equal(2, view.Length);
        Assert.Equal(new[] { 7, 8 }, view.Elements.ToArray());
        Assert.Equal(2, view.Header.Count);
    }

    [Fact]
    public void TryParse_SentinelWalk_StopsAtTerminatorWithinCapacity()
    {
        var buffer = new byte[FlexibleArrayLayout<SentinelHeader, int>.GetRequiredBytes(3)];
        var view = FlexibleArraySpan<SentinelHeader, int>.Create(buffer, 3);
        view.Header = new SentinelHeader { Tag = 1 };
        view.Elements[0] = 1;
        view.Elements[1] = 2;
        view.Elements[2] = 0;

        Assert.True(ReadOnlyFlexibleArraySpan<SentinelHeader, int>.TryParse(
            buffer, out var parsed));
        Assert.Equal(2, parsed.Length);
        Assert.Equal(new[] { 1, 2 }, parsed.Elements.ToArray());
    }

    [Fact]
    public void TryParse_RejectsHeaderCountBeyondCapacity()
    {
        var buffer = BuildPacket(5, 7, 8);
        Assert.False(ReadOnlyFlexibleArraySpan<PacketHeader, int>.TryParse(
            buffer, out var view));
        Assert.Equal(0, view.Length);
    }

    [Fact]
    public void TryParse_RejectsNegativeHeaderCount()
    {
        var buffer = BuildPacket(-1, 7, 8);
        Assert.False(ReadOnlyFlexibleArraySpan<PacketHeader, int>.TryParse(
            buffer, out _));
    }

    [Fact]
    public void TryParse_RejectsBufferShorterThanHeader()
    {
        var buffer = new byte[16];
        Assert.False(ReadOnlyFlexibleArraySpan<PacketHeader, int>.TryParse(
            buffer, out _));
    }

    private static byte[] BuildPacket(int count, params int[] elements)
    {
        var buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(elements.Length)];
        var view = FlexibleArraySpan<PacketHeader, int>.Create(buffer, elements.Length);
        view.Header = new PacketHeader { Count = count };
        elements.AsSpan().CopyTo(view.Elements);
        return buffer;
    }
}
