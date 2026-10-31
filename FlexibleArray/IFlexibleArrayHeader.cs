namespace FlexibleArray;

/// <summary>
/// Flexible array header contract. The implementing type H must declare a first-element
/// placeholder field of type <typeparamref name="T"/> at the desired layout position and
/// return a reference to it from <see cref="GetFirstElement"/>; the tail element sequence
/// starts at that field's position.
/// </summary>
/// <remarks>
/// The contract locates the first element and derives the logical tail length. The interface
/// itself cannot verify that the returned reference points to an instance field of H, that the
/// field type matches the T in use, that the field sits at the end of the layout, or that H's
/// layout is compatible with the target platform's C ABI. Those constraints belong to the
/// consumer and to a dedicated analyzer.
/// <para>
/// The members are static abstract because C# forbids struct instance members from returning
/// instance fields by reference (CS8170); taking the header by <c>ref</c> keeps the returned
/// reference tied to the caller's storage.
/// </para>
/// </remarks>
public interface IFlexibleArrayHeader<TSelf, T>
    where TSelf : unmanaged, IFlexibleArrayHeader<TSelf, T>
    where T : unmanaged
{
    /// <summary>Returns a reference to the first-element placeholder field of the given header.</summary>
    static abstract ref T GetFirstElement(ref TSelf header);

    /// <summary>
    /// Returns the logical number of tail elements of the complete object. During parsing the
    /// given view is capacity-bounded: its <see cref="ReadOnlyFlexibleArraySpan{THeader, T}.Length"/>
    /// is the element capacity of the backing buffer, not necessarily the logical count.
    /// </summary>
    /// <remarks>
    /// Implementations must not mutate the view and must return a value in [0, array.Length].
    /// This models the real-world forms of self-describing tails: a count field, a byte length
    /// (minus the header, divided by the element size), an end-pointer difference, or a
    /// sentinel walk bounded by the view capacity.
    /// </remarks>
    static abstract int GetTailLength(ReadOnlyFlexibleArraySpan<TSelf, T> array);
}
