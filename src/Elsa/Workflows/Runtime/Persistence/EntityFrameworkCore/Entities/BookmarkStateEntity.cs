namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational projection of one scoped durable bookmark.</summary>
public sealed class BookmarkStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string BookmarkId { get; set; } = null!;
    public string BookmarkIdHash { get; set; } = null!;
    public string BookmarkIdOrderKey { get; set; } = null!;
    public string ActivityExecutionId { get; set; } = null!;
    public string ExecutableNodeId { get; set; } = null!;
    public string ResumeTargetId { get; set; } = null!;
    public string StimulusType { get; set; } = null!;
    public string StimulusHash { get; set; } = null!;
    public string StimulusLookupKey { get; set; } = null!;
    public string StimulusTypeLookupKey { get; set; } = null!;
    public string? PayloadJson { get; set; }
    public string ContentJson { get; set; } = null!;
    public string MetadataJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long CreatedAtUtcTicks { get; set; }
    public int CreatedAtOffsetMinutes { get; set; }
    public long? ExpiresAtUtcTicks { get; set; }
    public int? ExpiresAtOffsetMinutes { get; set; }
    public long Revision { get; set; }
    public string IncarnationId { get; set; } = null!;
}
