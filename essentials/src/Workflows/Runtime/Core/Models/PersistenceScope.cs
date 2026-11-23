namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Identifies one application persistence partition. The value is opaque to domain code and never
/// grants global access merely because tenant context is absent.
/// </summary>
public sealed record PersistenceScope
{
    public const string DefaultValue = "default";

    public PersistenceScope(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Persistence scope values cannot have leading or trailing whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
