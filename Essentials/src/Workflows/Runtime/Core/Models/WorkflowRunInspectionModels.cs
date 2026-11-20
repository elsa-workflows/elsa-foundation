using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A read of one exact Run (<see cref="Contracts.IWorkflowRunInspection"/>).</summary>
/// <param name="WorkflowExecutionId">The Run's Workflow Instance identity; never "the latest run" of a definition.</param>
/// <param name="ActivityExecutionId">The selected activity execution of that Run, or none.</param>
/// <param name="ActivityPageSize">The activity execution page size, clamped to 1..<see cref="MaxActivityPageSize"/>.</param>
/// <param name="ActivityContinuationToken">The continuation token of the previous activity execution page, or none for the first.</param>
/// <param name="ResolveEvidenceIds">
/// The value evidence of the selected activity execution to resolve through the run endpoints' separately authorized and
/// audited payload read (at most 32 per read). Only these are read, so a consumer that discloses only part of a read
/// resolves, and the payload read audits, only what it discloses. Every other value is reported by its evidence only.
/// </param>
public sealed record WorkflowRunInspectionRequest(
    string WorkflowExecutionId,
    string? ActivityExecutionId = null,
    int ActivityPageSize = WorkflowRunInspectionRequest.DefaultActivityPageSize,
    string? ActivityContinuationToken = null,
    IReadOnlySet<string>? ResolveEvidenceIds = null)
{
    /// <summary>The activity execution page size when none is requested.</summary>
    public const int DefaultActivityPageSize = 25;

    /// <summary>The largest activity execution page a read returns.</summary>
    public const int MaxActivityPageSize = 50;
}

/// <summary>Why a part of a Run, or a value, is not disclosed. It is reported, never filled in.</summary>
public enum WorkflowRunUnavailableReason
{
    /// <summary>The runtime did not capture it (its capture policy, or the activity keeps no such state).</summary>
    NotCaptured,

    /// <summary>It was recorded for this Run but is no longer retained.</summary>
    Pruned,

    /// <summary>This backend does not provide it.</summary>
    Unsupported,

    /// <summary>The caller's run access does not cover it.</summary>
    NotPermitted
}

/// <summary>How one value of the selected activity execution is disclosed. The default (zero) value is <see cref="Withheld"/>, so an unset disclosure fails closed.</summary>
public enum WorkflowRunValueDisclosure
{
    /// <summary>
    /// The value is withheld: by execution evidence (a secret reference, or a value whose policy requires encryption), or
    /// because it is sensitive, which the runtime does not capture by default and this read never releases even when a
    /// capture policy captured it. Only its marker is disclosed.
    /// </summary>
    Withheld = 0,

    /// <summary>The value is not disclosed for the reported <see cref="WorkflowRunUnavailableReason"/>.</summary>
    Unavailable = 1,

    /// <summary>
    /// The value was captured and the caller may resolve it, but this read did not ask for it
    /// (<see cref="WorkflowRunInspectionRequest.ResolveEvidenceIds"/>), or the read's resolution bound was reached.
    /// </summary>
    Resolvable = 2,

    /// <summary>The captured value was released by the authorized, audited payload read.</summary>
    Resolved = 3
}

/// <summary>One exact Run as the Runtime inspection reads it.</summary>
/// <param name="Instance">The Workflow Instance, with the Executable (artifact id and Artifact Hash) it runs.</param>
/// <param name="Incidents">The Run's recorded incidents, oldest first.</param>
/// <param name="Activities">One bounded page of the Run's activity executions.</param>
/// <param name="Selection">The selected activity execution, when one was requested.</param>
/// <param name="CanInspectValues">Whether the caller may inspect the Run's sensitive values (incident messages, metadata, values).</param>
/// <param name="CanResolveValues">Whether the caller may resolve the Run's captured values through the payload read.</param>
/// <param name="PersistenceMode">The persistence mode the Run executed under.</param>
/// <param name="InspectionGranularity">Whether activity evidence is exact (<c>activity-level</c>) or folded to commit boundaries (<c>boundary-level</c>).</param>
/// <param name="ReadAt">When the read began, before its first store read; an in-progress Run is reported as of this time.</param>
public sealed record WorkflowRunInspectionDetails(
    WorkflowRunInspectionInstance Instance,
    IReadOnlyList<WorkflowRunInspectionIncident> Incidents,
    WorkflowRunInspectionActivityPage Activities,
    WorkflowRunInspectionSelection? Selection,
    bool CanInspectValues,
    bool CanResolveValues,
    string PersistenceMode,
    string InspectionGranularity,
    DateTimeOffset ReadAt);

/// <summary>The Workflow Instance of a Run. <see cref="IsTerminal"/> is false while the Run is still executing.</summary>
public sealed record WorkflowRunInspectionInstance(
    string WorkflowExecutionId,
    string ArtifactId,
    string ArtifactHash,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string Status,
    string? SubStatus,
    string RunKind,
    bool IsTerminal,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? ParentWorkflowExecutionId,
    long ActivityCount,
    int IncidentCount,
    int? ActiveIncidentCount,
    int? BlockingIncidentCount,
    string? SourceReferenceId,
    string? PublicationId);

/// <summary>
/// One recorded incident. Without value inspection its <see cref="Message"/> is the run endpoints' redaction text and
/// <see cref="MessageRedacted"/> is true. When the incident's activity has a sensitive input, or its non-sensitivity cannot be
/// established from both the Executable's input bindings and the activity execution's own value evidence, for the activity
/// it names and for the causal child it propagated from (an incident without an activity execution, a causal child named only
/// partly, an unreadable Executable or execution), or when a child-control consumer raised it or it records poisoned
/// scheduler work (its text may echo another Run's batches or an arbitrary handler's exception text), <see cref="SensitiveInputs"/>
/// is true and its message is withheld even with value inspection; the run page's own incident view still shows it.
/// </summary>
public sealed record WorkflowRunInspectionIncident(
    string IncidentId,
    string? ActivityExecutionId,
    string? ExecutableNodeId,
    string Severity,
    string Status,
    string FailureType,
    string Message,
    bool MessageRedacted,
    bool SensitiveInputs,
    bool IsBlocking,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>One page of a Run's activity executions, in the run endpoints' order.</summary>
public sealed record WorkflowRunInspectionActivityPage(
    IReadOnlyList<WorkflowRunInspectionActivitySummary> Items,
    long TotalCount,
    string? NextContinuationToken);

/// <summary>One activity execution of a Run, as the run's activity list summarizes it.</summary>
public sealed record WorkflowRunInspectionActivitySummary(
    string ActivityExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> OutcomeNames,
    int BookmarkCount,
    int IncidentCount,
    int ValueSnapshotCount);

/// <summary>
/// The selected activity execution: read (<see cref="Execution"/>), or not read for <see cref="UnavailableReason"/>. When
/// both are null, the Run holds no activity execution with this identity.
/// </summary>
public sealed record WorkflowRunInspectionSelection(
    string ActivityExecutionId,
    WorkflowRunInspectionActivityExecution? Execution,
    WorkflowRunUnavailableReason? UnavailableReason);

/// <summary>One exact activity execution, with its bookmarks, incidents, value evidence and state view.</summary>
public sealed record WorkflowRunInspectionActivityExecution(
    WorkflowRunInspectionActivitySummary Summary,
    string? ParentActivityExecutionId,
    string? SchedulingActivityExecutionId,
    int? AttemptNumber,
    IReadOnlyList<WorkflowRunInspectionBookmark> Bookmarks,
    IReadOnlyList<WorkflowRunInspectionIncident> Incidents,
    IReadOnlyList<WorkflowRunInspectionValue> Values,
    WorkflowRunInspectionStateView StateView);

/// <summary>One bookmark the activity execution waits on.</summary>
public sealed record WorkflowRunInspectionBookmark(string BookmarkId, string StimulusType, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

/// <summary>
/// One value evidence record (an input, an output or a container variable) of the selected activity execution. Its
/// <see cref="Value"/> is present only when <see cref="Disclosure"/> is <see cref="WorkflowRunValueDisclosure.Resolved"/>.
/// A withheld value carries only its marker (<see cref="WithheldKind"/>, and a secret reference's name, which is not a value).
/// </summary>
public sealed record WorkflowRunInspectionValue(
    string EvidenceId,
    string Name,
    string Subject,
    string? InputKey,
    string CaptureMode,
    string CaptureState,
    string AccessState,
    bool IsSensitive,
    DateTimeOffset CapturedAt,
    string CaptureReason,
    string? TypeKind,
    string? TypeId,
    string? WithheldKind,
    string? SecretReferenceName,
    WorkflowRunValueDisclosure Disclosure,
    WorkflowRunUnavailableReason? UnavailableReason,
    JsonElement? Value);

/// <summary>
/// The activity execution's state view: available with its module's projection, unavailable by reason, or
/// <see cref="Withheld"/> (with no view) because the activity has a sensitive input binding whose value a projection may echo.
/// </summary>
public sealed record WorkflowRunInspectionStateView(
    bool Available,
    WorkflowRunUnavailableReason? UnavailableReason,
    string? ViewKind,
    int? ViewVersion,
    JsonElement? View,
    bool Withheld = false);
