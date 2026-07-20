namespace Elsa.Persistence.Core.Design;

/// <summary>
/// Identifies one logical design mutation across retries. Callers create the key before dispatch and
/// preserve its exact value until the mutation reaches a definitive outcome.
/// </summary>
public sealed record DesignOperationKey
{
    public DesignOperationKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The caller-supplied opaque identity. Elsa does not normalize or derive this value.</summary>
    public string Value { get; }
}
