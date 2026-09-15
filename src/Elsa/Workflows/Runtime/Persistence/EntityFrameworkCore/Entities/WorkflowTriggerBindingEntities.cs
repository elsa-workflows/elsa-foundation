namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational envelope and bounded lookup projections for one workflow trigger binding.</summary>
public sealed class WorkflowTriggerBindingEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string TriggerBindingId { get; set; } = null!;
    public string TriggerBindingIdHash { get; set; } = null!;
    public string TriggerBindingIdOrderKey { get; set; } = null!;
    public string ArtifactId { get; set; } = null!;
    public string ArtifactIdHash { get; set; } = null!;
    public string ArtifactIdOrderKey { get; set; } = null!;
    public string DefinitionId { get; set; } = null!;
    public string ArtifactVersion { get; set; } = null!;
    public string ArtifactHash { get; set; } = null!;
    public string ExecutableNodeId { get; set; } = null!;
    public string StimulusType { get; set; } = null!;
    public string StimulusHash { get; set; } = null!;
    public string StimulusLookupKey { get; set; } = null!;
    public string StimulusTypeLookupKey { get; set; } = null!;
    public string? CorrelationScope { get; set; }
    public string? ActivationId { get; set; }
    public string? ActivationIdHash { get; set; }
    public string? ActivationIdOrderKey { get; set; }
    public string? SlotId { get; set; }
    public int Cardinality { get; set; }
    public bool IsActive { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public int CreatedAtOffsetMinutes { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Atomic activation projection marker for trigger-binding preparation and activation.</summary>
public sealed class WorkflowTriggerBindingProjectionStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ActivationId { get; set; } = null!;
    public string ActivationIdHash { get; set; } = null!;
    public string ActivationIdOrderKey { get; set; } = null!;
    public bool IsActive { get; set; }
    public int BindingCount { get; set; }
    public string ProjectionFingerprint { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
