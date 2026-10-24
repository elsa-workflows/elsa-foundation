using Elsa.Workflows.Publishing.Api.Models;
using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Requests;

/// <summary>
/// Starts a test run of a published workflow version. <see cref="Inputs"/> carries caller-supplied workflow
/// inputs (name → JSON value) threaded into the start dispatch so <c>input.*</c> expressions resolve to them
/// (#286); null/empty when the caller supplies none.
/// </summary>
public sealed record StartWorkflowTestRun(
    string VersionId,
    IReadOnlyDictionary<string, JsonElement>? Inputs = null,
    bool AcknowledgeUnavailableExpressionValidation = false)
    : IRequest<WorkflowTestRunView>;

public sealed record StartWorkflowDraftTestRun : IRequest<WorkflowTestRunView>
{
    public StartWorkflowDraftTestRun(
        string DefinitionId,
        string SnapshotId,
        WorkflowDefinitionState State,
        string? ArtifactVersion = null,
        IReadOnlyDictionary<string, JsonElement>? Inputs = null,
        IReadOnlyCollection<ActivityPresentationRecord>? ActivityPresentation = null,
        bool AcknowledgeUnavailableExpressionValidation = false,
        WorkflowTestRunStartSelection? Start = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SnapshotId);
        ArgumentNullException.ThrowIfNull(State);

        this.DefinitionId = DefinitionId;
        this.SnapshotId = SnapshotId;
        this.State = State;
        this.ArtifactVersion = ArtifactVersion;
        this.Inputs = Inputs;
        this.ActivityPresentation =
            ActivityPresentationRecord.NormalizeCollection(ActivityPresentation);
        this.AcknowledgeUnavailableExpressionValidation = AcknowledgeUnavailableExpressionValidation;
        this.Start = Start;
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

    public IReadOnlyCollection<ActivityPresentationRecord> ActivityPresentation { get; init; }
    public bool AcknowledgeUnavailableExpressionValidation { get; init; }
    public WorkflowTestRunStartSelection? Start { get; init; }
}

/// <summary>Module-neutral selection for a direct start or one discovered trigger option.</summary>
public sealed record WorkflowTestRunStartSelection(
    string Kind,
    string? OptionId = null,
    string? ModeId = null,
    JsonElement? Data = null)
{
    public const string Direct = "direct";
    public const string Trigger = "trigger";
}

/// <summary>Captured draft state for read-only trigger and declared-input discovery.</summary>
public sealed record DraftTestRunOptionsRequest : IRequest<DraftTestRunOptionsView>
{
    public DraftTestRunOptionsRequest(
        string definitionId,
        string snapshotId,
        WorkflowDefinitionState state,
        string? artifactVersion = null,
        IReadOnlyCollection<ActivityPresentationRecord>? activityPresentation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        ArgumentNullException.ThrowIfNull(state);
        DefinitionId = definitionId;
        SnapshotId = snapshotId;
        State = state;
        ArtifactVersion = artifactVersion;
        ActivityPresentation = ActivityPresentationRecord.NormalizeCollection(activityPresentation);
    }

    public string DefinitionId { get; }
    public string SnapshotId { get; }
    public WorkflowDefinitionState State { get; }
    public string? ArtifactVersion { get; }
    public IReadOnlyCollection<ActivityPresentationRecord> ActivityPresentation { get; }
}

public sealed record GetWorkflowTestRun(string TestRunId) : IRequest<WorkflowTestRunView>;

public sealed record StopWorkflowTestRun(string TestRunId) : IRequest<WorkflowTestRunView>;
