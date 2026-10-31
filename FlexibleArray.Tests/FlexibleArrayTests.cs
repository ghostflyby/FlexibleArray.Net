using System.Runtime.InteropServices;

namespace FlexibleArray.Tests;

/// <summary>Verifies the owner layer: construction from existing collections, copy order, N = 0/1 boundaries, and disposal semantics.</summary>
public sealed class FlexibleArrayTests
{
    [Fact]
    public void Create_CopiesHeaderThenElements()
    {
        var source = new List<int> { 10, 20, 30 };
        var header = new PacketHeader { Count = source.Count, Timestamp = 12345, FirstElement = 999 };
        using var array = FlexibleArray<PacketHeader, int>.Create(header, CollectionsMarshal.AsSpan(source));

        Assert.Equal(3, array.Length);
        Assert.Equal(FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3), array.ByteLength);

        FlexibleArraySpan<PacketHeader, int> view = array.Span;
        Assert.Equal(3, view.Header.Count);
        Assert.Equal(12345, view.Header.Timestamp);
        Assert.Equal(new[] { 10, 20, 30 }, view.Elements.ToArray());
        // The tail copy happens after the whole-header write: the placeholder's initial 999 is overwritten by the first element 10.
        Assert.Equal(10, view.Header.FirstElement);
    }

    [Fact]
    public void Create_ZeroElements_PreservesPlaceholderAndKeepsHeaderSize()
    {
        using var array = FlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 0, FirstElement = -1 }, ReadOnlySpan<int>.Empty);

        Assert.Equal(0, array.Length);
        Assert.Equal(24, array.ByteLength);
        Assert.True(array.Span.Elements.IsEmpty);
        Assert.Equal(-1, array.Span.Header.FirstElement);
    }

    [Fact]
    public void Create_OneElement_TailLivesInsideHeaderFootprint()
    {
        using var array = FlexibleArray<PacketHeader, int>.Create(new PacketHeader { Count = 1 }, [77]);

        // requiredBytes = max(24, 16 + 4) = 24: the single element lies entirely within the header footprint.
        Assert.Equal(24, array.ByteLength);
        Assert.Equal(77, array.Span.Elements[0]);
        Assert.Equal(77, array.Span.Header.FirstElement);
    }

    [Fact]
    public void Create_NonBlittableElements_RoundTrip()
    {
        using var chars = FlexibleArray<CharHeader, char>.Create(new CharHeader { Count = 3 }, ['a', 'b', 'c']);
        Assert.Equal(new[] { 'a', 'b', 'c' }, chars.Span.Elements.ToArray());
        Assert.Equal('a', chars.Span.Header.FirstElement);

        using var bools = FlexibleArray<BoolHeader, bool>.Create(new BoolHeader { Id = 7 }, [true, false, true]);
        Assert.Equal(new[] { true, false, true }, bools.Span.Elements.ToArray());
        Assert.Equal(7, bools.Span.Header.Id);
    }

    [Fact]
    public void Create_LargeTail_RoundTrip()
    {
        int[] source = Enumerable.Range(0, 1000).ToArray();
        using var array = FlexibleArray<PacketHeader, int>.Create(new PacketHeader { Count = source.Length }, source);

        Assert.Equal(1000, array.Length);
        Assert.Equal(16 + 4000, array.ByteLength);
        Assert.Equal(source, array.Span.Elements.ToArray());
    }

    [Fact]
    public void Span_ThrowsAfterDispose_AndDisposeIsIdempotent()
    {
        FlexibleArray<PacketHeader, int> array = FlexibleArray<PacketHeader, int>.Create(default, ReadOnlySpan<int>.Empty);
        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Span; });
        array.Dispose();
    }
}
