namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;

/// <summary>Flat durable representation of an immutable publication snapshot-review authority.</summary>
public sealed class PublicationSnapshotReviewEntity
{
    public string PreflightToken { get; set; } = "";
    public string CandidateHash { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Action { get; set; } = "";
    public string SlotName { get; set; } = "";
    public string PolicySource { get; set; } = "";
    public long? PolicyRevision { get; set; }
    public string? RequestedAction { get; set; }
    public string? RequestedSlotName { get; set; }
    public string? RequestedExpectedPublicationId { get; set; }
    public long SlotRevision { get; set; }
    public string? ActivePublicationId { get; set; }
    public string? TenantId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
