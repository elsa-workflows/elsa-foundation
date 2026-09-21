namespace Elsa.Serialization.Core.Exceptions;

/// <summary>
/// Thrown when a module attempts to register a bare (non-dotted) alias. The bare-alias namespace is
/// reserved for framework primitives; modules must use dotted/reverse-DNS aliases (FR-011 / FR-013).
/// </summary>
public sealed class ReservedAliasNamespaceException : Exception
{
    /// <summary>The offending alias.</summary>
    public string Alias { get; }

    public ReservedAliasNamespaceException(string alias)
        : base($"The alias '{alias}' uses the reserved (bare, non-dotted) framework namespace. Module-contributed types must use a dotted alias.")
    {
        Alias = alias;
    }
}
