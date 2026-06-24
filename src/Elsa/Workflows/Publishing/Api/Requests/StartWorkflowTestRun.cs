using Elsa.Workflows.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record StartWorkflowTestRun(string VersionId) : IRequest<WorkflowTestRunView>;

public sealed record StartWorkflowDraftTestRun : IRequest<WorkflowTestRunView>
{
    public StartWorkflowDraftTestRun(
        string DefinitionId,
        string SnapshotId,
        WorkflowDefinitionState State,
        string? ArtifactVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SnapshotId);
        ArgumentNullException.ThrowIfNull(State);

        this.DefinitionId = DefinitionId;
        this.SnapshotId = SnapshotId;
        this.State = State;
        this.ArtifactVersion = ArtifactVersion;
    }

    public string DefinitionId { get; init; }
    public string SnapshotId { get; init; }
    public WorkflowDefinitionState State { get; init; }
    public string? ArtifactVersion { get; init; }
}
