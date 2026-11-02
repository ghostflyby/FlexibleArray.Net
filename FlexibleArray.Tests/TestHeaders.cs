using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace FlexibleArray.Tests;

/// <summary>Sample header: int + long + int. 64-bit LP64 layout: 0/8/16, size 24.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PacketHeader
    : IConstructibleFlexibleArrayHeader<PacketHeader, int>
{
    public int Count;
    public long Timestamp;
    public int FirstElement;

    public static ref int GetFirstElement(ref PacketHeader header) => ref header.FirstElement;
    public static int GetTailElementCount(scoped in PacketHeader header) => header.Count;
    public PacketHeader WithTailElementCount(int count) => this with { Count = count };
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

    // Layout-only fixture: no self-describing length, so the tail is always empty.
    public static int GetTailElementCount(scoped in DoubleTagOriginal header) => 0;
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

    // Layout-only fixture: no self-describing length, so the tail is always empty.
    public static int GetTailElementCount(scoped in PackedHeader header) => 0;
}

/// <summary>C definition struct { long a; char b; short items[]; }: items at offset 10.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct LongByteShortOriginal : IFlexibleArrayHeader<LongByteShortOriginal, short>
{
    public long A;
    public byte B;
    public short FirstElement;

    public static ref short GetFirstElement(ref LongByteShortOriginal header) => ref header.FirstElement;

    // Layout-only fixture: no self-describing length, so the tail is always empty.
    public static int GetTailElementCount(scoped in LongByteShortOriginal header) => 0;
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

    // Layout-only fixture: no self-describing length, so the tail is always empty.
    public static int GetTailElementCount(scoped in NestedContainerHeader header) => 0;
}

/// <summary>Non-blittable unmanaged element type: managed sizeof(char) = 2, offset 4, header size 8.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CharHeader : IFlexibleArrayHeader<CharHeader, char>
{
    public int Count;
    public char FirstElement;

    public static ref char GetFirstElement(ref CharHeader header) => ref header.FirstElement;
    public static int GetTailElementCount(scoped in CharHeader header) => header.Count;
}

/// <summary>Non-blittable unmanaged element type: managed sizeof(bool) = 1, offset 9, header size 16.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BoolHeader : IFlexibleArrayHeader<BoolHeader, bool>
{
    public long Id;
    public byte Count;
    public bool FirstElement;

    public static ref bool GetFirstElement(ref BoolHeader header) => ref header.FirstElement;
    public static int GetTailElementCount(scoped in BoolHeader header) => header.Count;
}

/// <summary>Sentinel-terminated tail: the logical length comes from a walk over the elements.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SentinelHeader : IFlexibleArrayHeader<SentinelHeader, int>
{
    public byte Tag;
    public int FirstElement;

    public static ref int GetFirstElement(ref SentinelHeader header) => ref header.FirstElement;

    // strlen-style walk: no bound, termination is guaranteed by the producer.
    public static int GetTailElementCount(scoped in SentinelHeader header)
    {
        ref var h = ref Unsafe.AsRef(in header);
        ref var first = ref SentinelHeader.GetFirstElement(ref h);
        var length = 0;
        while (Unsafe.Add(ref first, length) != 0)
        {
            length++;
        }

        return length;
    }
}

/// <summary>A header whose Vector128 field forces 16-byte field alignment.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vector128Header : IFlexibleArrayHeader<Vector128Header, byte>
{
    public int Count;
    public Vector128<byte> Padding;
    public byte FirstElement;

    public static ref byte GetFirstElement(ref Vector128Header header) => ref header.FirstElement;
    public static int GetTailElementCount(scoped in Vector128Header header) => header.Count;
}

/// <summary>A minimal byte-tail header: the whole object is a NUL-terminated byte string.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CStrHeader : IFlexibleArrayHeader<CStrHeader, byte>
{
    public byte FirstElement;

    public static ref byte GetFirstElement(ref CStrHeader header) => ref header.FirstElement;

    // strlen over the byte tail; termination is guaranteed by the producer.
    public static int GetTailElementCount(scoped in CStrHeader header)
    {
        ref var h = ref Unsafe.AsRef(in header);
        ref var first = ref CStrHeader.GetFirstElement(ref h);
        var length = 0;
        while (Unsafe.Add(ref first, length) != 0)
        {
            length++;
        }

        return length;
    }
}

/// <summary>
/// A deliberately asymmetric contract implementation: the write-back adds one, so unified
/// construction must reject the pair instead of building a corrupt object.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BrokenSetHeader
    : IConstructibleFlexibleArrayHeader<BrokenSetHeader, int>
{
    public int Count;
    public int FirstElement;

    public static ref int GetFirstElement(ref BrokenSetHeader header) => ref header.FirstElement;
    public static int GetTailElementCount(scoped in BrokenSetHeader header) => header.Count;
    public BrokenSetHeader WithTailElementCount(int count) => this with { Count = count + 1 };
}
