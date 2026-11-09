namespace Elsa.Etl.Core;

/// <summary>The exact optional tenant and required application persistence partition for an ETL operation.</summary>
/// <remarks>Adapters derive this address from trusted host context; a null tenant is an exact un-tenanted address, not a global-access fallback.</remarks>
public sealed record EtlScope
{
    public EtlScope(string? tenantId, string persistencePartition)
    {
        if (tenantId is not null)
            Validate(tenantId, nameof(tenantId));
        Validate(persistencePartition, nameof(persistencePartition));
        TenantId = tenantId;
        PersistencePartition = persistencePartition;
    }

    public string? TenantId { get; }
    public string PersistencePartition { get; }

    private static void Validate(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > 256 || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl))
            throw new ArgumentException("An ETL scope component must be a bounded, exact nonblank value.", name);
    }
}
