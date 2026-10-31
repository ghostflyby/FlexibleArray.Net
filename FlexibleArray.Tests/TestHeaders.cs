using System.Runtime.InteropServices;

namespace FlexibleArray.Tests;

/// <summary>Sample header: int + long + int. 64-bit LP64 layout: 0/8/16, size 24.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PacketHeader : IFlexibleArrayHeader<PacketHeader, int>
{
    public int Count;
    public long Timestamp;
    public int FirstElement;

    public static ref int GetFirstElement(ref PacketHeader header) => ref header.FirstElement;
}

/// <summary>
/// The C counterpart of the placeholder-field model: struct { double value; char tag; int items[]; } with items at offset 12.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DoubleTagOriginal : IFlexibleArrayHeader<DoubleTagOriginal, int>
{
    public double Value;
    public byte Tag;
    public int FirstElement;

    public static ref int GetFirstElement(ref DoubleTagOriginal header) => ref header.FirstElement;
}

/// <summary>The split-off plain header: double + byte; trailing padding rounds the size to 16.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DoubleTagHeader
{
    public double Value;
    public byte Tag;
}

/// <summary>The concatenation-model counterexample: nested header + separate first field; the first element drifts to 16.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DoubleTagReconstructed
{
    public DoubleTagHeader Header;
    public int FirstElement;
}

/// <summary>Pack = 1: long + byte + int, first element at offset 9, size 13 (the element start is legally unaligned).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedHeader : IFlexibleArrayHeader<PackedHeader, int>
{
    public long Value;
    public byte Tag;
    public int FirstElement;

    public static ref int GetFirstElement(ref PackedHeader header) => ref header.FirstElement;
}

/// <summary>C definition struct { long a; char b; short items[]; }: items at offset 10.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct LongByteShortOriginal : IFlexibleArrayHeader<LongByteShortOriginal, short>
{
    public long A;
    public byte B;
    public short FirstElement;

    public static ref short GetFirstElement(ref LongByteShortOriginal header) => ref header.FirstElement;
}

/// <summary>A nested struct with trailing padding: long + byte, size 16.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct LongByteInner
{
    public long A;
    public byte B;
}

/// <summary>Placeholder-field model with a nested struct inside the header: inner struct + int placeholder.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NestedContainerHeader : IFlexibleArrayHeader<NestedContainerHeader, int>
{
    public LongByteInner Inner;
    public int FirstElement;

    public static ref int GetFirstElement(ref NestedContainerHeader header) => ref header.FirstElement;
}

/// <summary>Non-blittable unmanaged element type: managed sizeof(char) = 2, offset 4, header size 8.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CharHeader : IFlexibleArrayHeader<CharHeader, char>
{
    public int Count;
    public char FirstElement;

    public static ref char GetFirstElement(ref CharHeader header) => ref header.FirstElement;
}

/// <summary>Non-blittable unmanaged element type: managed sizeof(bool) = 1, offset 8, header size 16.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BoolHeader : IFlexibleArrayHeader<BoolHeader, bool>
{
    public long Id;
    public bool FirstElement;

    public static ref bool GetFirstElement(ref BoolHeader header) => ref header.FirstElement;
}
