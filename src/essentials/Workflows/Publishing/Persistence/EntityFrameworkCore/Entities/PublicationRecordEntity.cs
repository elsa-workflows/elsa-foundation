namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;

/// <summary>Lossless relational projection of one scoped publication record and its lifecycle status.</summary>
public sealed class PublicationRecordEntity
{
    public string Id { get; set; } = null!;
    public string PublicationId { get; set; } = null!;
    public string PublicationIdHash { get; set; } = null!;
    public string SlotId { get; set; } = null!;
    public string SlotIdHash { get; set; } = null!;
    public string SlotName { get; set; } = null!;
    public string WorkflowDefinitionId { get; set; } = null!;
    public string WorkflowDefinitionVersionId { get; set; } = null!;
    public string ArtifactId { get; set; } = null!;
    public string? SourceReferenceId { get; set; }
    public long ExpectedSlotRevision { get; set; }
    public string Status { get; set; } = null!;
    public long CreatedAtUtcTicks { get; set; }
    public int CreatedAtOffsetMinutes { get; set; }
    public long? ActivatedAtUtcTicks { get; set; }
    public int? ActivatedAtOffsetMinutes { get; set; }
    public long? RetiredAtUtcTicks { get; set; }
    public int? RetiredAtOffsetMinutes { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? TenantId { get; set; }
    public string TenantIdHash { get; set; } = null!;
    public long Revision { get; set; }
}
