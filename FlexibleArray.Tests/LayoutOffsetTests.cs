using System.Runtime.InteropServices;

namespace FlexibleArray.Tests;

/// <summary>Verifies first-element offsets and managed-layout measurements under various layout shapes.</summary>
public sealed class LayoutOffsetTests
{
    [Fact]
    public void Sequential_PacketHeader()
    {
        Assert.Equal(16, FlexibleArrayLayout<PacketHeader, int>.FirstElementOffset);
        Assert.Equal(24, FlexibleArrayLayout<PacketHeader, int>.HeaderSize);
        Assert.Equal(4, FlexibleArrayLayout<PacketHeader, int>.ElementSize);
    }

    [Fact]
    public void PlaceholderModel_MatchesInlineCLayout_WhileReconstructionDrifts()
    {
        // Placeholder-field model: items @12, matching the C inline declaration.
        Assert.Equal(12, FlexibleArrayLayout<DoubleTagOriginal, int>.FirstElementOffset);
        Assert.Equal(16, FlexibleArrayLayout<DoubleTagOriginal, int>.HeaderSize);

        // Concatenation model: the split-off header carries trailing padding (9 → 16) and the
        // first element drifts to 16.
        Assert.Equal(new IntPtr(12), Marshal.OffsetOf<DoubleTagOriginal>(nameof(DoubleTagOriginal.FirstElement)));
        Assert.Equal(new IntPtr(16), Marshal.OffsetOf<DoubleTagReconstructed>(nameof(DoubleTagReconstructed.FirstElement)));
    }

    [Fact]
    public void Pack1_UnalignedFirstElement()
    {
        Assert.Equal(9, FlexibleArrayLayout<PackedHeader, int>.FirstElementOffset);
        Assert.Equal(13, FlexibleArrayLayout<PackedHeader, int>.HeaderSize);
    }

    [Fact]
    public void NestedShortTail_MatchesInlineCLayout()
    {
        // C: struct { long a; char b; short items[]; } → items @10 (one pad byte after char to reach short alignment).
        Assert.Equal(10, FlexibleArrayLayout<LongByteShortOriginal, short>.FirstElementOffset);
        Assert.Equal(16, FlexibleArrayLayout<LongByteShortOriginal, short>.HeaderSize);
        Assert.Equal(2, FlexibleArrayLayout<LongByteShortOriginal, short>.ElementSize);
    }

    [Fact]
    public void NestedContainer_PlaceholderAfterNestedStruct()
    {
        // The inner struct occupies [0,16) including trailing padding; the placeholder field sits at 16 and the header size is 24.
        Assert.Equal(16, FlexibleArrayLayout<NestedContainerHeader, int>.FirstElementOffset);
        Assert.Equal(24, FlexibleArrayLayout<NestedContainerHeader, int>.HeaderSize);
    }

    [Fact]
    public void NonBlittableUnmanaged_CharAndBool()
    {
        // Managed-layout measurements bypass the marshaling layer: sizeof(char)=2, sizeof(bool)=1.
        Assert.Equal(4, FlexibleArrayLayout<CharHeader, char>.FirstElementOffset);
        Assert.Equal(8, FlexibleArrayLayout<CharHeader, char>.HeaderSize);
        Assert.Equal(2, FlexibleArrayLayout<CharHeader, char>.ElementSize);

        Assert.Equal(8, FlexibleArrayLayout<BoolHeader, bool>.FirstElementOffset);
        Assert.Equal(16, FlexibleArrayLayout<BoolHeader, bool>.HeaderSize);
        Assert.Equal(1, FlexibleArrayLayout<BoolHeader, bool>.ElementSize);
    }

    [Fact]
    public void GetRequiredBytes_CoversHeaderAndTail_WithOverflowChecks()
    {
        // N = 0 still reserves the full header.
        Assert.Equal(24, FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(0));
        // Tail exceeds the header: 16 + 3*4 = 28.
        Assert.Equal(28, FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3));
        // The header size wins when the tail does not exceed it.
        Assert.Equal(13, FlexibleArrayLayout<PackedHeader, int>.GetRequiredBytes(1));
        // Offset plus tail just crosses the header padding: 10 + 5*2 = 20 > 16.
        Assert.Equal(20, FlexibleArrayLayout<LongByteShortOriginal, short>.GetRequiredBytes(5));
    }

    [Fact]
    public void GetRequiredBytes_RejectsInvalidLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(-1));
        Assert.Throws<OverflowException>(() => FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(int.MaxValue));
    }
}
