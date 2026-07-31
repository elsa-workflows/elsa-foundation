using System.Text.Json.Serialization;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public sealed record PublishedWorkflowView(
    string PublicationId,
    string DefinitionId,
    string VersionId,
    string DefinitionVersionId,
    string ArtifactId,
    string SlotName,
    PublicationStatusView Status,
    string SourceReferenceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ActivatedAt,
    DateTimeOffset? RetiredAt,
    string ArtifactVersion,
    string ArtifactHash,
    string RootActivityId,
    int NodeCount,
    [property: JsonIgnore] bool WasCreated)
{
    public static PublishedWorkflowView From(
        WorkflowExecutable executable,
        WorkflowExecutableSourceReference reference,
        PublicationRecord publication,
        bool wasCreated = true) =>
        new(
            publication.PublicationId,
            publication.WorkflowDefinitionId,
            publication.WorkflowDefinitionVersionId,
            executable.Identity.DefinitionVersionId,
            publication.ArtifactId,
            publication.SlotName,
            ToStatusView(publication.Status),
            reference.SourceReferenceId,
            publication.CreatedAt,
            publication.ActivatedAt ?? throw new InvalidOperationException(
                $"Active publication '{publication.PublicationId}' has no activation timestamp."),
            publication.RetiredAt,
            executable.Identity.ArtifactVersion,
            executable.Identity.ArtifactHash,
            executable.RootActivity.ExecutableNodeId,
            executable.Nodes.Count,
            wasCreated);

    private static PublicationStatusView ToStatusView(PublicationStatus status) => status switch
    {
        PublicationStatus.Candidate => PublicationStatusView.Preparing,
        PublicationStatus.PendingProjection => PublicationStatusView.Pending,
        PublicationStatus.Active => PublicationStatusView.Active,
        PublicationStatus.Retired => PublicationStatusView.Retired,
        PublicationStatus.Failed => PublicationStatusView.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported publication status.")
    };
}
