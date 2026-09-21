namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;

/// <summary>Lossless relational projection of one scoped publication policy.</summary>
public sealed class PublicationPolicyEntity
{
    public string Id { get; set; } = null!;
    public string PolicyKey { get; set; } = null!;
    public string PolicyKeyHash { get; set; } = null!;
    public string? WorkflowDefinitionId { get; set; }
    public string? WorkflowDefinitionIdHash { get; set; }
    public string? TenantId { get; set; }
    public string TenantIdHash { get; set; } = null!;
    public string DefaultAction { get; set; } = null!;
    public string DefaultSlotName { get; set; } = null!;
    public long Revision { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public int UpdatedAtOffsetMinutes { get; set; }
}
