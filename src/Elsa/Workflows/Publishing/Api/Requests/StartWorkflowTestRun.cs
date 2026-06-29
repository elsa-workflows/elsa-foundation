using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

/// <summary>
/// Starts a test run of a published workflow version. <see cref="Inputs"/> carries caller-supplied workflow
/// inputs (name → JSON value) threaded into the start dispatch so <c>input.*</c> expressions resolve to them
/// (#286); null/empty when the caller supplies none.
/// </summary>
public sealed record StartWorkflowTestRun(string VersionId, IReadOnlyDictionary<string, JsonElement>? Inputs = null)
    : IRequest<WorkflowTestRunView>;

public sealed record StartWorkflowDraftTestRun : IRequest<WorkflowTestRunView>
{
    public StartWorkflowDraftTestRun(
        string DefinitionId,
        string SnapshotId,
        WorkflowDefinitionState State,
        string? ArtifactVersion = null,
        IReadOnlyDictionary<string, JsonElement>? Inputs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SnapshotId);
        ArgumentNullException.ThrowIfNull(State);

        this.DefinitionId = DefinitionId;
        this.SnapshotId = SnapshotId;
        this.State = State;
        this.ArtifactVersion = ArtifactVersion;
        this.Inputs = Inputs;
    }

    public string DefinitionId { get; init; }
    public string SnapshotId { get; init; }
    public WorkflowDefinitionState State { get; init; }
    public string? ArtifactVersion { get; init; }

    /// <summary>
    /// Caller-supplied workflow inputs (name → JSON value) threaded into the start dispatch so <c>input.*</c>
    /// expressions resolve to them (#286); null/empty when the caller supplies none.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Inputs { get; init; }
}
