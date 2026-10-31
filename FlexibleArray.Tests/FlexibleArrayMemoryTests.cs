namespace FlexibleArray.Tests;

/// <summary>Verifies the Memory layer's projection and its coupling with the owner lifetime.</summary>
public sealed class FlexibleArrayMemoryTests
{
    [Fact]
    public void Span_ProjectsOverSameStorage()
    {
        using var array = FlexibleArray<PacketHeader, int>.Create(new PacketHeader { Count = 2 }, [1, 2]);
        FlexibleArrayMemory<PacketHeader, int> memory = array.Memory;

        FlexibleArraySpan<PacketHeader, int> view = memory.Span;
        view.Elements[1] = 99;
        Assert.Equal(2, memory.Length);
        Assert.Equal(99, array.Span.Elements[1]);
    }

    [Fact]
    public void Span_ThrowsAfterOwnerDisposed()
    {
        FlexibleArray<PacketHeader, int> array = FlexibleArray<PacketHeader, int>.Create(default, ReadOnlySpan<int>.Empty);
        FlexibleArrayMemory<PacketHeader, int> memory = array.Memory;
        array.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = memory.Span; });
        Assert.Throws<ObjectDisposedException>(() => { _ = array.Memory.Span; });
    }

    [Fact]
    public void ImplicitConversion_ToReadOnlyMemory()
    {
        using var array = FlexibleArray<PacketHeader, int>.Create(new PacketHeader { Count = 2 }, [5, 6]);
        ReadOnlyFlexibleArrayMemory<PacketHeader, int> readOnly = array.Memory;

        Assert.Equal(2, readOnly.Length);
        Assert.Equal(new[] { 5, 6 }, readOnly.Span.Elements.ToArray());
        Assert.Equal(5, readOnly.Span.Header.FirstElement);
    }

    [Fact]
    public void DefaultMemory_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => { _ = default(FlexibleArrayMemory<PacketHeader, int>).Span; });
        Assert.Throws<InvalidOperationException>(() => { _ = default(ReadOnlyFlexibleArrayMemory<PacketHeader, int>).Span; });
    }
}
