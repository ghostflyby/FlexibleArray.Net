namespace FlexibleArray.Tests;

/// <summary>Verifies the ref-returning interface member, header/tail aliasing, and the overlap behavior of whole-header writes.</summary>
public sealed class MutableFlexibleSpanTests
{
    [Fact]
    public void Elements_AliasHeaderPlaceholderField()
    {
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, 3);

        // The interface-returned field reference and the first element are the same memory: aliasing in both directions.
        view.Elements[0] = 42;
        Assert.Equal(42, view.Header.FirstElement);
        Assert.Equal(42, BitConverter.ToInt32(buffer[16..]));

        view.Header.FirstElement = 7;
        Assert.Equal(7, view.Elements[0]);
        Assert.Equal(7, BitConverter.ToInt32(buffer[16..]));
    }

    [Fact]
    public void ReadOnlyView_ExposesSameData()
    {
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, 3);
        view.Header = new PacketHeader { Count = 3, Timestamp = 12345, FirstElement = 1 };
        view.Elements[1] = 2;
        view.Elements[2] = 3;

        FlexibleSpan<PacketHeader, int> readOnly = view;
        Assert.Equal(3, readOnly.Length);
        Assert.Equal(3, readOnly.Header.Count);
        Assert.Equal(12345, readOnly.Header.Timestamp);
        Assert.Equal(new[] { 1, 2, 3 }, readOnly.Elements.ToArray());
        Assert.True(FlexibleSpan<PacketHeader, int>.Create(buffer, 3).Elements.SequenceEqual(readOnly.Elements));
    }

    [Fact]
    public void ZeroLength_KeepsFullHeaderAndEmptyElements()
    {
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(0)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, 0);

        view.Header = new PacketHeader { Count = 0, Timestamp = 12345, FirstElement = -1 };
        Assert.Equal(0, view.Length);
        Assert.True(view.Elements.IsEmpty);
        // With N = 0 no tail write happens; the placeholder field keeps the value from the header assignment.
        Assert.Equal(-1, view.Header.FirstElement);
        Assert.Equal(12345, view.Header.Timestamp);
    }

    [Fact]
    public void WholeHeaderWrite_OverwritesOverlappedElements()
    {
        // PacketHeader: sizeof(THeader)=24, offset=16, element size=4. The header covers
        // [0,24), overlapping e0 ([16,20)) and e1 ([20,24)).
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, 2);
        view.Elements[0] = 10;
        view.Elements[1] = 20;

        // The temporary header on the right side is zero-initialized via localsinit, so its
        // padding bytes are 0; the struct assignment copies sizeof(THeader) bytes wholesale:
        // e0 receives the placeholder field's value and e1 lands on padding bytes.
        view.Header = new PacketHeader { Count = 1, Timestamp = 2, FirstElement = 99 };

        Assert.Equal(1, view.Header.Count);
        Assert.Equal(2, view.Header.Timestamp);
        Assert.Equal(99, view.Elements[0]);
        Assert.Equal(0, view.Elements[1]);
    }

    [Fact]
    public void Create_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            Span<byte> buffer = new byte[32];
            _ = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, -1);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> buffer = new byte[16];
            _ = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, 3);
        });
    }
}
