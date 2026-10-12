namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational envelope and unique-owner projections for one workflow activation slot.</summary>
public sealed class WorkflowActivationSlotEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string SlotId { get; set; } = null!;
    public string SlotIdHash { get; set; } = null!;
    public string SlotIdOrderKey { get; set; } = null!;
    public string WorkflowDefinitionId { get; set; } = null!;
    public string WorkflowDefinitionIdHash { get; set; } = null!;
    public string WorkflowDefinitionIdOrderKey { get; set; } = null!;
    public string SlotName { get; set; } = null!;
    public string SlotNameHash { get; set; } = null!;
    public string SlotNameOrderKey { get; set; } = null!;
    public string? ActiveActivationId { get; set; }
    public string? ActiveActivationIdHash { get; set; }
    public string? ActiveActivationIdOrderKey { get; set; }
    public string ActiveActivationUniquenessKey { get; set; } = null!;
    public string? SourceKind { get; set; }
    public string? SourceId { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public int UpdatedAtOffsetMinutes { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
