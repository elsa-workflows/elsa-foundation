namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Canonical interest projection only; slot/ref/material remain publication authority.</summary>
internal sealed class MessagePublicationInterestEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? TenantIdHash { get; set; }
    public string ActivationId { get; set; } = null!;
    public string ActivationIdHash { get; set; } = null!;
    public string SlotId { get; set; } = null!;
    public string SlotIdHash { get; set; } = null!;
    public string DescriptorFingerprint { get; set; } = null!;
    public int State { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>One exact MessageScope epoch/control row; no complete-cohort readiness is enabled.</summary>
internal sealed class MessageInterestScopeControlEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? TenantIdHash { get; set; }
    public string FullIdentity { get; set; } = null!;
    public long Epoch { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
