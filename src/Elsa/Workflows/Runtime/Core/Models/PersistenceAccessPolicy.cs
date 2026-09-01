namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Authorization level required by a persistence operation, independent of its storage scope.</summary>
public enum PersistenceAccessPolicy
{
    Ordinary,
    Privileged
}

/// <summary>A bounded, operator-meaningful purpose required whenever privileged access is acquired.</summary>
public sealed record PersistenceAccessPurpose
{
    public PersistenceAccessPurpose(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Persistence access purposes cannot have leading or trailing whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
