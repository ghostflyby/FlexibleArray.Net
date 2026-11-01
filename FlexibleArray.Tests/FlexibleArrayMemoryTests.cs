using System.Buffers;
using System.Runtime.CompilerServices;

namespace FlexibleArray.Tests;

/// <summary>Verifies the Memory-backed construction, projection, and conversion semantics.</summary>
public sealed class FlexibleArrayMemoryTests
{
    [Fact]
    public void Span_ProjectsOverSameStorage()
    {
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(
            new PacketHeader { Count = 2 }, [1, 2]);

        FlexibleArraySpan<PacketHeader, int> view = memory.Span;
        view.Elements[1] = 99;
        Assert.Equal(2, memory.Length);
        Assert.Equal(99, memory.Span.Elements[1]);
    }

    [Fact]
    public void Create_WithZeroElements_PreservesPlaceholder()
    {
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(
            new PacketHeader { Count = 0, FirstElement = -1 }, ReadOnlySpan<int>.Empty);

        Assert.Equal(0, memory.Length);
        Assert.True(memory.Span.Elements.IsEmpty);
        Assert.Equal(-1, memory.Span.Header.FirstElement);
    }

    [Fact]
    public void Create_WithNonBlittableElements_RoundTrips()
    {
        FlexibleArrayMemory<CharHeader, char> chars = FlexibleArrayMemory<CharHeader, char>.Create(
            new CharHeader { Count = 3 }, ['a', 'b', 'c']);
        Assert.Equal(new[] { 'a', 'b', 'c' }, chars.Span.Elements.ToArray());
        Assert.Equal('a', chars.Span.Header.FirstElement);

        FlexibleArrayMemory<BoolHeader, bool> bools = FlexibleArrayMemory<BoolHeader, bool>.Create(
            new BoolHeader { Id = 7, Count = 3 }, [true, false, true]);
        Assert.Equal(new[] { true, false, true }, bools.Span.Elements.ToArray());
        Assert.Equal(7, bools.Span.Header.Id);
    }

    [Fact]
    public void ImplicitConversion_ToReadOnlyMemory()
    {
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(
            new PacketHeader { Count = 2 }, [5, 6]);
        ReadOnlyFlexibleArrayMemory<PacketHeader, int> readOnly = memory;

        Assert.Equal(2, readOnly.Length);
        Assert.Equal(new[] { 5, 6 }, readOnly.Span.Elements.ToArray());
        Assert.Equal(5, readOnly.Span.Header.FirstElement);
    }

    [Fact]
    public void ImplicitConversion_MemoryNarrowsToSpanSemantics()
    {
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(
            new PacketHeader { Count = 2 }, [1, 2]);

        // Long-holdable narrows to stack-only; writable keeps its span semantics.
        FlexibleArraySpan<PacketHeader, int> span = memory;
        span.Elements[0] = 42;
        Assert.Equal(42, memory.Span.Elements[0]);

        // The read-only lattice edge composes with the writable-to-read-only edge.
        ReadOnlyFlexibleArrayMemory<PacketHeader, int> readOnlyMemory = memory;
        ReadOnlyFlexibleArraySpan<PacketHeader, int> readOnlySpan = readOnlyMemory;
        Assert.Equal(new[] { 42, 2 }, readOnlySpan.Elements.ToArray());
    }

    [Fact]
    public void Create_WithManagedBacking_BuildsCompleteObject()
    {
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(
            new PacketHeader { Count = 3, Timestamp = 5 }, [7, 8, 9]);

        Assert.Equal(3, memory.Length);
        Assert.Equal(5, memory.Span.Header.Timestamp);
        Assert.Equal(new[] { 7, 8, 9 }, memory.Span.Elements.ToArray());
        // The tail copy landed in the placeholder field, as in the owner construction.
        Assert.Equal(7, memory.Span.Header.FirstElement);
    }

    [Fact]
    public void Create_WithAllocator_UsesProvidedStorage()
    {
        int requested = 0;
        Memory<byte> storage = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(
            byteLength =>
            {
                requested = byteLength;
                return storage;
            },
            new PacketHeader { Count = 2 }, [3, 4]);

        Assert.Equal(FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2), requested);
        Assert.Equal(new[] { 3, 4 }, memory.Span.Elements.ToArray());
        // The object was built directly in the caller's storage; element 0 lives at offset 16.
        Assert.Equal(3, BitConverter.ToInt32(storage.Span[16..]));
    }

    [Fact]
    public void Create_RejectsInvalidAllocatorResultsAndCountMismatch()
    {
        Assert.Throws<ArgumentException>(() => FlexibleArrayMemory<PacketHeader, int>.Create(
            static _ => Memory<byte>.Empty, new PacketHeader { Count = 1 }, [1]));
        Assert.Throws<ArgumentException>(() => FlexibleArrayMemory<PacketHeader, int>.Create(
            new PacketHeader { Count = 5 }, [1, 2]));
    }

    [Fact]
    public unsafe void Create_WithManagedBacking_GuaranteesRequiredAlignment()
    {
        FlexibleArrayMemory<Vector128Header, byte> memory = FlexibleArrayMemory<Vector128Header, byte>.Create(
            new Vector128Header { Count = 3 }, [1, 2, 3]);

        FlexibleArraySpan<Vector128Header, byte> view = memory.Span;
        nuint address = (nuint)Unsafe.AsPointer(ref view.Header);
        // sizeof(Vector128Header) = 48, so the conservative policy demands 64-byte alignment.
        Assert.True(FlexibleArrayLayout<Vector128Header, byte>.RequiredAlignment == 64);
        Assert.True(address % FlexibleArrayLayout<Vector128Header, byte>.RequiredAlignment == 0);
        Assert.Equal(new byte[] { 1, 2, 3 }, memory.Span.Elements.ToArray());
    }

    [Fact]
    public void DefaultMemory_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => { _ = default(FlexibleArrayMemory<PacketHeader, int>).Span; });
        Assert.Throws<InvalidOperationException>(() => { _ = default(ReadOnlyFlexibleArrayMemory<PacketHeader, int>).Span; });
    }

    [Fact]
    public void Create_OverManagedMemory_WritesThroughToTheArray()
    {
        Memory<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(3)];
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(buffer, 3);

        FlexibleArraySpan<PacketHeader, int> view = memory.Span;
        view.Header = new PacketHeader { Count = 3 };
        view.Elements[0] = 1;
        view.Elements[2] = 9;

        Assert.Equal(new[] { 1, 0, 9 }, memory.Span.Elements.ToArray());
        // The underlying managed array sees the same bytes: element 2 lives at offset 24.
        Assert.Equal(9, BitConverter.ToInt32(buffer.Span[24..]));
    }

    [Fact]
    public void Create_OverReadOnlyMemory_ReadsTail()
    {
        byte[] buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        FlexibleArraySpan<PacketHeader, int> view = FlexibleArraySpan<PacketHeader, int>.Create(buffer, 2);
        view.Header = new PacketHeader { Count = 2 };
        view.Elements[0] = 4;
        view.Elements[1] = 5;

        ReadOnlyFlexibleArrayMemory<PacketHeader, int> memory =
            ReadOnlyFlexibleArrayMemory<PacketHeader, int>.Create(buffer, 2);

        Assert.Equal(2, memory.Length);
        Assert.Equal(new[] { 4, 5 }, memory.Span.Elements.ToArray());
    }

    [Fact]
    public void Create_RejectsTooShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => FlexibleArrayMemory<PacketHeader, int>.Create(new byte[8], 1));
        Assert.Throws<ArgumentException>(() => ReadOnlyFlexibleArrayMemory<PacketHeader, int>.Create(new byte[8], 1));
    }

    [Fact]
    public void Create_OverExistingMemory_BuildsContent()
    {
        Memory<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        FlexibleArrayMemory<PacketHeader, int> memory = FlexibleArrayMemory<PacketHeader, int>.Create(
            buffer, new PacketHeader { Count = 2 }, [8, 9]);

        Assert.Equal(new[] { 8, 9 }, memory.Span.Elements.ToArray());
        // The object was built directly in the caller's storage; element 0 lives at offset 16.
        Assert.Equal(8, BitConverter.ToInt32(buffer.Span[16..]));
    }

    [Fact]
    public void Create_FromPool_RentsBuildsAndHandsOutTheRental()
    {
        using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Create(
            new PacketHeader { Count = 3, Timestamp = 4 }, [1, 2, 3], out FlexibleArrayMemory<PacketHeader, int> view);

        Assert.Equal(3, view.Length);
        Assert.Equal(4, view.Span.Header.Timestamp);
        Assert.Equal(new[] { 1, 2, 3 }, view.Span.Elements.ToArray());
    }

    [Fact]
    public void Create_FromPool_RejectsCountMismatchBeforeRenting()
    {
        Assert.Throws<ArgumentException>(() => MemoryPool<byte>.Shared.Create(
            new PacketHeader { Count = 5 }, [1, 2], out _));
    }
}
