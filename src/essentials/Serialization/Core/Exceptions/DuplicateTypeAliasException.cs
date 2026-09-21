namespace Elsa.Serialization.Core.Exceptions;

/// <summary>
/// Thrown when a type alias (or its CLR type) is registered more than once in the well-known type
/// registry. Registration is fail-fast: a duplicate alias would silently resolve to the wrong type,
/// so the application refuses to start instead (FR-013 / SC-004).
/// </summary>
public sealed class DuplicateTypeAliasException : Exception
{
    /// <summary>The conflicting alias.</summary>
    public string Alias { get; }

    public DuplicateTypeAliasException(string alias)
        : base($"The type alias '{alias}' is already registered.")
    {
        Alias = alias;
    }

    public DuplicateTypeAliasException(string alias, string message) : base(message)
    {
        Alias = alias;
    }
}
