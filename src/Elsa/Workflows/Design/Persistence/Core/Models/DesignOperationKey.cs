namespace Elsa.Workflows.Design.Persistence.Core.Models;

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

    /// <summary>
    /// Resolves an operation key from an optional caller-supplied value, generating a fresh random key when the
    /// caller did not supply one. A generated key makes the mutation a distinct (non-deduplicated) operation —
    /// the pre-idempotency default — so first-party clients that do not participate in retry-dedup (e.g. the
    /// Studio create/update dialogs) still succeed, while callers that supply a stable key keep exactly-once
    /// replay semantics. This keeps the idempotency token optional on the HTTP surface without weakening the
    /// non-empty invariant a real key must satisfy.
    /// </summary>
    public static DesignOperationKey CreateOrGenerate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new DesignOperationKey(Guid.NewGuid().ToString("N"))
            : new DesignOperationKey(value);
}
