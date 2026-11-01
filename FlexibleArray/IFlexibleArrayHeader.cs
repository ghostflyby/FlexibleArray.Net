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
/// <para>
/// The header's length-related fields are contract state, not free-form data: they must not
/// be mutated through a public header reference. Change lengths only through the dedicated
/// write-back (<see cref="IConstructibleFlexibleArrayHeader{TSelf, T}.WithTailElementCount"/>)
/// and the library's capacity members — those keep every length derived from the header in
/// sync; a hand-written field update does not.
/// </para>
/// </remarks>
public interface IFlexibleArrayHeader<TSelf, T>
    where TSelf : unmanaged, IFlexibleArrayHeader<TSelf, T>
    where T : unmanaged
{
    /// <summary>Returns a reference to the first-element placeholder field of the given header.</summary>
    protected internal static abstract ref T GetFirstElement(ref TSelf header);

    /// <summary>
    /// Returns the logical number of tail elements of the complete object whose header is given.
    /// </summary>
    /// <remarks>
    /// Implementations derive the answer from the header alone: a count field, a byte length
    /// (minus the header, divided by the element size), an end-pointer difference, or a sentinel
    /// walk over the tail reached through <see cref="GetFirstElement"/> — in which case
    /// termination is guaranteed by the producer, like <c>strlen</c>. The header must not be
    /// mutated, and the library performs no bounds checking on the result: interop declarations
    /// are trusted, matching P/Invoke semantics.
    /// </remarks>
    protected internal static abstract int GetTailElementCount(scoped in TSelf header);
}
