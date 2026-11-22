namespace Elsa3.Mapping.Exceptions;

/// <summary>
/// A host replaces one of the two Elsa 3 converters (<c>IElsa3DefinitionConverter</c>, <c>IElsa3NodeConverter</c>) and
/// leaves the other on a default that does not follow it, so node and definition conversion would follow different
/// policies. Thrown at shell activation, so the host does not start with a converter set that disagrees.
/// </summary>
public sealed class PartialElsa3ConverterReplacementException(Type replaced, string implementation, Type missing)
    : Exception(
        $"The host replaces {replaced.Name} with {implementation}, but {missing.Name} is still the Elsa3Mapping " +
        $"default, which does not follow it. Node and definition conversion follow one policy: register a " +
        $"{missing.Name} alongside it, or implement both contracts on one class.")
{
    /// <summary>The converter contract the host replaced.</summary>
    public Type Replaced { get; } = replaced;

    /// <summary>The host's registration of <see cref="Replaced"/>.</summary>
    public string Implementation { get; } = implementation;

    /// <summary>The converter contract left on its default.</summary>
    public Type Missing { get; } = missing;
}
