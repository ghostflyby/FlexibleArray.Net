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
        byte[] buffer = BuildPacket(2, 7, 8);

        Assert.True(ReadOnlyFlexibleArraySpan<PacketHeader, int>.TryParse(
            buffer, out ReadOnlyFlexibleArraySpan<PacketHeader, int> view));
        Assert.Equal(2, view.Length);
        Assert.Equal(new[] { 7, 8 }, view.Elements.ToArray());
        Assert.Equal(2, view.Header.Count);
    }

    [Fact]
    public void TryParse_SentinelWalk_StopsAtTerminatorWithinCapacity()
    {
        byte[] buffer = new byte[FlexibleArrayLayout<SentinelHeader, int>.GetRequiredBytes(3)];
        FlexibleArraySpan<SentinelHeader, int> view = FlexibleArraySpan<SentinelHeader, int>.Create(buffer, 3);
        view.Header = new SentinelHeader { Tag = 1 };
        view.Elements[0] = 1;
        view.Elements[1] = 2;
        view.Elements[2] = 0;

        Assert.True(ReadOnlyFlexibleArraySpan<SentinelHeader, int>.TryParse(
            buffer, out ReadOnlyFlexibleArraySpan<SentinelHeader, int> parsed));
        Assert.Equal(2, parsed.Length);
        Assert.Equal(new[] { 1, 2 }, parsed.Elements.ToArray());
    }

    [Fact]
    public void TryParse_RejectsHeaderCountBeyondCapacity()
    {
        byte[] buffer = BuildPacket(5, 7, 8);
        Assert.False(ReadOnlyFlexibleArraySpan<PacketHeader, int>.TryParse(
            buffer, out ReadOnlyFlexibleArraySpan<PacketHeader, int> view));
        Assert.Equal(0, view.Length);
    }

    [Fact]
    public void TryParse_RejectsNegativeHeaderCount()
    {
        byte[] buffer = BuildPacket(-1, 7, 8);
        Assert.False(ReadOnlyFlexibleArraySpan<PacketHeader, int>.TryParse(
            buffer, out ReadOnlyFlexibleArraySpan<PacketHeader, int> view));
    }

    [Fact]
    public void TryParse_RejectsBufferShorterThanHeader()
    {
        byte[] buffer = new byte[16];
        Assert.False(ReadOnlyFlexibleArraySpan<PacketHeader, int>.TryParse(
            buffer, out ReadOnlyFlexibleArraySpan<PacketHeader, int> view));
    }

    private static byte[] BuildPacket(int count, params int[] elements)
    {
        byte[] buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(elements.Length)];
        FlexibleArraySpan<PacketHeader, int> view = FlexibleArraySpan<PacketHeader, int>.Create(buffer, elements.Length);
        view.Header = new PacketHeader { Count = count };
        elements.AsSpan().CopyTo(view.Elements);
        return buffer;
    }
}
