using System.Buffers;
using System.Runtime.CompilerServices;

namespace FlexibleArray.Tests;

/// <summary>
/// Verifies the flexible array objects: construction paths, projections, disposal routing,
/// and the writable-is-a-read-only reference conversion.
/// </summary>
public sealed class MutableFlexibleArrayTests
{
    [Fact]
    public void Create_WithManagedBacking_BuildsCompleteObject()
    {
        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 3, Timestamp = 5 }, [7, 8, 9]);

        Assert.Equal(3, array.Length);
        Assert.Equal(5, array.Header.Timestamp);
        Assert.Equal(new[] { 7, 8, 9 }, array.Span.Elements.ToArray());
        // The tail copy landed in the placeholder field.
        Assert.Equal(7, array.Header.FirstElement);
    }

    [Fact]
    public void Create_WithAllocator_UsesProvidedStorage()
    {
        var requested = 0;
        Memory<byte> storage = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            byteLength =>
            {
                requested = byteLength;
                return storage;
            },
            new PacketHeader { Count = 2 }, [3, 4]);

        Assert.Equal(FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2), requested);
        Assert.Equal(new[] { 3, 4 }, array.Span.Elements.ToArray());
        // The object was built directly in the caller's storage; element 0 lives at offset 16.
        Assert.Equal(3, BitConverter.ToInt32(storage.Span[16..]));
    }

    [Fact]
    public void Create_OverExistingMemory_BuildsContent()
    {
        Memory<byte> buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            buffer, new PacketHeader { Count = 2 }, [8, 9]);

        Assert.Equal(new[] { 8, 9 }, array.Span.Elements.ToArray());
        Assert.Equal(8, BitConverter.ToInt32(buffer.Span[16..]));
    }

    [Fact]
    public void Create_RejectsInvalidAllocatorResultsAndCountMismatch()
    {
        Assert.Throws<ArgumentException>(() => MutableFlexibleArray<PacketHeader, int>.Create(
            static _ => Memory<byte>.Empty, new PacketHeader { Count = 1 }, [1]));
        // Declared-length rejection belongs to headers without a registered write-back:
        // a constructible header's count is stamped from the elements instead.
        Assert.Throws<ArgumentException>(() => MutableFlexibleArray<DoubleTagOriginal, int>.Create(
            new DoubleTagOriginal(), [1]));
        Assert.Throws<ArgumentException>(() => MutableFlexibleArray<PacketHeader, int>.Create(new byte[8], 1));
    }

    [Fact]
    public void Create_WithZeroElements_PreservesPlaceholder()
    {
        using var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 0, FirstElement = -1 }, ReadOnlySpan<int>.Empty);

        Assert.Equal(0, array.Length);
        Assert.True(array.Span.Elements.IsEmpty);
        Assert.Equal(-1, array.Header.FirstElement);
    }

    [Fact]
    public void Create_WithNonBlittableElements_RoundTrips()
    {
        using var chars = MutableFlexibleArray<CharHeader, char>.Create(
            new CharHeader { Count = 3 }, ['a', 'b', 'c']);
        Assert.Equal(new[] { 'a', 'b', 'c' }, chars.Span.Elements.ToArray());
        Assert.Equal('a', chars.Header.FirstElement);

        using var bools = MutableFlexibleArray<BoolHeader, bool>.Create(
            new BoolHeader { Id = 7, Count = 3 }, [true, false, true]);
        Assert.Equal(new[] { true, false, true }, bools.Span.Elements.ToArray());
        Assert.Equal(7, bools.Header.Id);
    }

    [Fact]
    public unsafe void Create_WithManagedBacking_GuaranteesRequiredAlignment()
    {
        using var array = MutableFlexibleArray<Vector128Header, byte>.Create(
            new Vector128Header { Count = 3 }, [1, 2, 3]);

        var view = array.Span;
        var address = (nuint)Unsafe.AsPointer(ref view.Header);
        // sizeof(Vector128Header) = 48, so the conservative policy demands 64-byte alignment.
        Assert.True(FlexibleArrayLayout<Vector128Header, byte>.RequiredAlignment == 64);
        Assert.True(address % FlexibleArrayLayout<Vector128Header, byte>.RequiredAlignment == 0);
        Assert.Equal(new byte[] { 1, 2, 3 }, array.Span.Elements.ToArray());
    }

    [Fact]
    public void ReadOnlyCreate_OverExistingMemory_ReadsTail()
    {
        var buffer = new byte[FlexibleArrayLayout<PacketHeader, int>.GetRequiredBytes(2)];
        MutableFlexibleSpan<PacketHeader, int>.Create(buffer, new PacketHeader(), [4, 5]);

        using var memory =
            FlexibleArray<PacketHeader, int>.Create(buffer, 2);

        Assert.Equal(2, memory.Length);
        Assert.Equal(new[] { 4, 5 }, memory.Span.Elements.ToArray());
    }

    [Fact]
    public void ReferenceConversion_WritableIsAReadOnly()
    {
        var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 2 }, [5, 6]);
        FlexibleArray<PacketHeader, int> readOnly = array;

        Assert.Equal(2, readOnly.Length);
        Assert.Equal(new[] { 5, 6 }, readOnly.Span.Elements.ToArray());
        Assert.Equal(5, readOnly.Header.FirstElement);

        // The same object: disposing through the read-only reference releases it once.
        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = readOnly.Span; });
    }

    [Fact]
    public void Create_FromPool_DisposeReturnsTheRental()
    {
        var array = MemoryPool<byte>.Shared.Create(
            new PacketHeader { Count = 3, Timestamp = 4 }, [1, 2, 3]);

        Assert.Equal(3, array.Length);
        Assert.Equal(new[] { 1, 2, 3 }, array.Span.Elements.ToArray());

        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Span; });
        array.Dispose();
    }

    [Fact]
    public void Create_StampsCountAcrossAllStoragePaths()
    {
        // Every storage path derives the tail length from the element count when the header
        // registers a write-back: no Count on any call site.
        using var managed = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Timestamp = 1 }, [1, 2]);
        using var pooled = MemoryPool<byte>.Shared.Create(
            new PacketHeader { Timestamp = 2 }, [3]);

        Assert.Equal(2, managed.Header.Count);
        Assert.Equal(1, pooled.Header.Count);
    }

    [Fact]
    public void Dispose_OfManagedObject_CollectsWithTheGc()
    {
        var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 1 }, [1]);
        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Span; });
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Header; });
    }

    [Fact]
    public unsafe void Pin_ReturnsStableObjectPointer()
    {
        var array = MutableFlexibleArray<PacketHeader, int>.Create(
            new PacketHeader { Count = 2, Timestamp = 9 }, [4, 5]);

        using var handle = array.Pin();
        ref var header = ref Unsafe.AsRef<PacketHeader>(handle.Pointer);
        Assert.Equal(9, header.Timestamp);
        Assert.Equal(4, PacketHeader.GetFirstElement(ref header));

        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => array.Pin());
    }
}
