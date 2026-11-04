namespace FlexibleArray.Tests;

/// <summary>Builds complete objects into buffers the tests supply.</summary>
internal static class TestBuffers
{
    internal static void Build<THeader, T>(Span<byte> buffer, in THeader header, ReadOnlySpan<T> elements)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
        => _ = new MutableFlexibleSpan<THeader, T>(buffer, header, elements);

    internal static void Build<THeader, T>(Span<byte> buffer, in THeader header)
        where THeader : unmanaged, IFlexibleArrayHeader<THeader, T>
        where T : unmanaged
        => _ = new MutableFlexibleSpan<THeader, T>(buffer, header);
}
