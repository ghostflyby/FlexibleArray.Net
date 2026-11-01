using System.Runtime.CompilerServices;

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
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader { Timestamp = 12345 }, [2, 3]);

        FlexibleSpan<PacketHeader, int> readOnly = view;
        Assert.Equal(2, readOnly.Length);
        Assert.Equal(2, readOnly.Header.Count);
        Assert.Equal(12345, readOnly.Header.Timestamp);
        Assert.Equal(new[] { 2, 3 }, readOnly.Elements.ToArray());
        Assert.True(FlexibleSpan<PacketHeader, int>.Create(buffer, 2).Elements.SequenceEqual(readOnly.Elements));
    }

    [Fact]
    public void ZeroLength_KeepsFullHeaderAndEmptyElements()
    {
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(0)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader { Timestamp = 12345, FirstElement = -1 }, ReadOnlySpan<int>.Empty);

        Assert.Equal(0, view.Length);
        Assert.True(view.Elements.IsEmpty);
        // With N = 0 no tail write happens; the placeholder field keeps the value from the header assignment.
        Assert.Equal(-1, view.Header.FirstElement);
        Assert.Equal(12345, view.Header.Timestamp);
        Assert.Equal(0, view.Header.Count);
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

    [Fact]
    public void Create_InfersCountFromElements()
    {
        // No Count anywhere on the call site: the length field is stamped and the elements
        // joined in one step.
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader { Timestamp = 7 }, [5, 6]);

        Assert.Equal(2, view.Length);
        Assert.Equal(2, view.Header.Count);
        Assert.Equal(7, view.Header.Timestamp);
        Assert.Equal(new[] { 5, 6 }, view.Elements.ToArray());

        // A stale count in the passed-in header is overwritten, not trusted.
        var stale = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader { Count = 99, Timestamp = 7 }, [5, 6]);
        Assert.Equal(2, stale.Header.Count);
    }

    [Fact]
    public void Create_RejectsAsymmetricWriteBack()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<BrokenSetHeader, int>.GetRequiredBytes(1)];
            MutableFlexibleSpan<BrokenSetHeader, int>.Create(buffer, new BrokenSetHeader(), [1]);
        });
    }

    [Fact]
    public void Create_RejectsShortBuffer()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> buffer = new byte[16];
            _ = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), new int[3]);
        });
    }

    [Fact]
    public void Create_WithoutWriteBack_ValidatesDeclaredLength()
    {
        // DoubleTagOriginal registers no write-back, so the declared length is the caller's
        // responsibility and the constructor checks it against the element count.
        Span<byte> buffer = new byte[FlexibleArrayLayout<DoubleTagOriginal, int>.GetRequiredBytes(0)];
        var view = MutableFlexibleSpan<DoubleTagOriginal, int>.Create(
            buffer, new DoubleTagOriginal { Value = 1.5 }, ReadOnlySpan<int>.Empty);
        Assert.Equal(0, view.Length);
        Assert.Equal(1.5, view.Header.Value);

        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> small = new byte[FlexibleArrayLayout<DoubleTagOriginal, int>.GetRequiredBytes(1)];
            MutableFlexibleSpan<DoubleTagOriginal, int>.Create(small, new DoubleTagOriginal(), [1]);
        });
    }

    [Fact]
    public void Capacity_IsDerivedFromTheBuffer()
    {
        // A buffer larger than the required byte count contributes its surplus as capacity.
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2) + 4 * sizeof(int)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, 2);
        Assert.Equal(2, view.Length);
        Assert.Equal(6, view.Capacity);
        Assert.Equal(4, view.FreeElements.Length);

        // Anchored views know no storage extent: capacity collapses to the declared length.
        ref var headerRef = ref Unsafe.As<byte, PacketHeader>(ref buffer[0]);
        var anchored = new MutableFlexibleSpan<PacketHeader, int>(ref headerRef);
        Assert.Equal(anchored.Length, anchored.Capacity);
    }

    [Fact]
    public void Create_EmptyStart_StampsZeroAndDerivesCapacity()
    {
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader { Count = 99, Timestamp = 7 });

        Assert.Equal(0, view.Length);
        Assert.Equal(0, view.Header.Count);
        Assert.Equal(7, view.Header.Timestamp);
        Assert.Equal(3, view.Capacity);
        Assert.True(view.FreeElements.Length == 3);
    }

    [Fact]
    public void Append_GrowsAndStampsTheHeader()
    {
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader { Timestamp = 7 });

        view.Append(1);
        view.AppendRange([2, 3]);

        Assert.Equal(3, view.Length);
        Assert.Equal(3, view.Header.Count);
        Assert.Equal(new[] { 1, 2, 3 }, view.Elements.ToArray());
        Assert.Equal(3, ((FlexibleSpan<PacketHeader, int>)view).Header.Count);
    }

    [Fact]
    public void Advance_CommitsInPlaceWrites()
    {
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader { Timestamp = 7 });

        // Write through the free region as if building a nested object, then commit.
        view.FreeElements[0] = 5;
        view.FreeElements[1] = 6;
        view.Advance(2);

        Assert.Equal(2, view.Length);
        Assert.Equal(2, view.Header.Count);
        Assert.Equal(new[] { 5, 6 }, view.Elements.ToArray());
    }

    [Fact]
    public void Truncate_ShrinksLogically_AndReappendOverwrites()
    {
        Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3)];
        var view = MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader { Timestamp = 7 });
        view.AppendRange([1, 2, 3]);

        view.Truncate(1);

        Assert.Equal(1, view.Length);
        Assert.Equal(1, view.Header.Count);
        Assert.Equal(new[] { 1 }, view.Elements.ToArray());
        Assert.Equal(3, view.Capacity);

        // The removed bytes are overwritten in place by later appends.
        view.AppendRange([9, 8]);
        Assert.Equal(new[] { 1, 9, 8 }, view.Elements.ToArray());
    }

    [Fact]
    public void Mutations_AreBoundedByCapacity()
    {
        // Ref-struct views cannot be captured in lambdas, so each check rebuilds the object
        // inside. A 24-byte buffer holds exactly two committed elements: the 24-byte header
        // overlaps both slots, so the derived capacity equals the committed length.
        Assert.Throws<InvalidOperationException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [1, 2]).Append(3);
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [1, 2]).AppendRange([3]);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [1, 2]).Advance(1);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [1, 2]).Advance(-1);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [1, 2]).Truncate(-1);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [1, 2]).Truncate(3);
        });
    }

    [Fact]
    public void Mutations_RequireAWriteBack()
    {
        // Reading a header without a write-back is fine; changing its length is not. The raw
        // form keeps a committed length of 1 with capacity to spare, past the bounds checks.
        Assert.Throws<NotSupportedException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<DoubleTagOriginal, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<DoubleTagOriginal, int>.Create(buffer, 1).Append(1);
        });
        Assert.Throws<NotSupportedException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<DoubleTagOriginal, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<DoubleTagOriginal, int>.Create(buffer, 1).Advance(1);
        });
        Assert.Throws<NotSupportedException>(() =>
        {
            Span<byte> buffer = new byte[FlexibleArrayLayout<DoubleTagOriginal, int>.GetRequiredBytes(2)];
            MutableFlexibleSpan<DoubleTagOriginal, int>.Create(buffer, 1).Truncate(0);
        });
    }
}
