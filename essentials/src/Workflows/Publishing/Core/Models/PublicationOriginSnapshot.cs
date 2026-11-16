using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>One bounded copied journal association; deliberately excludes arbitrary activation failure text.</summary>
public sealed class PublicationOriginSnapshot(string publicationId, string slotId, string workflowDefinitionId,
    string workflowDefinitionVersionId, string artifactId, string? sourceReferenceId, PublicationStatus status,
    DateTimeOffset? activatedAt, PersistenceScope scope, int storedMetadataBytes)
{
    public string PublicationId { get; } = publicationId;
    public string SlotId { get; } = slotId;
    public string WorkflowDefinitionId { get; } = workflowDefinitionId;
    public string WorkflowDefinitionVersionId { get; } = workflowDefinitionVersionId;
    public string ArtifactId { get; } = artifactId;
    public string? SourceReferenceId { get; } = sourceReferenceId;
    public PublicationStatus Status { get; } = status;
    public DateTimeOffset? ActivatedAt { get; } = activatedAt;
    public PersistenceScope Scope { get; } = scope;
    public int StoredMetadataBytes { get; } = storedMetadataBytes;
}
