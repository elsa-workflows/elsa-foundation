using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.ExecutionEvidence.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.ExecutionEvidence.Tests;

/// <summary>
/// Builders for the synthetic runtime shapes the evidence module observes. Every knob has a default, so a test names
/// only the one thing it is about; the repository's runtime tests hand-roll these envelopes per file and there is no
/// shared fixture to reuse, so this is the module-local equivalent.
/// </summary>
internal static class EvidenceFactory
{
    public const string WorkflowId = "wf-1";

    public static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly ValueTypeDescriptor StringType = new("String");

    /// <summary>Ordinary durable-inline protection: nothing withheld.</summary>
    private static readonly ValueProtectionPolicy InlinePolicy = ValueProtectionPolicy.InstanceInline;

    /// <summary>The one policy flag the enricher reads, so redaction has something real to react to.</summary>
    private static readonly ValueProtectionPolicy SensitivePolicy =
        new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);

    private static readonly ValueProtectionPolicy ExternalPolicy =
        new(DurableValueLifecycle.Instance, DurableValueStorage.External);

    private static readonly WorkflowExecutableIdentity Identity =
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    public static RuntimeCheckpointCommit Commit(
        string checkpointId = "cp-1",
        string name = RuntimeCheckpointNames.WorkflowStarted,
        string workflowExecutionId = WorkflowId,
        WorkflowExecutionStatus status = WorkflowExecutionStatus.Running,
        string? correlationId = null,
        IReadOnlyDictionary<string, ValueEnvelope>? variables = null,
        bool withWorkflowExecution = true,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>>? inspections = null,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>>? incidents = null) =>
        new(
            CommitId: $"commit-{checkpointId}",
            Checkpoint: Checkpoint(checkpointId, name, workflowExecutionId),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: withWorkflowExecution
                    ? new RuntimeStateChange<WorkflowExecutionState>(
                        workflowExecutionId,
                        RuntimeStateChangeOperation.Upsert,
                        WorkflowState(workflowExecutionId, status, correlationId, variables),
                        new Dictionary<string, string>())
                    : null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: incidents ?? [],
                operational: [],
                activityExecutionInspections: inspections ?? []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    public static RuntimeCheckpoint Checkpoint(
        string checkpointId = "cp-1",
        string name = RuntimeCheckpointNames.WorkflowStarted,
        string workflowExecutionId = WorkflowId) =>
        new(checkpointId, name, workflowExecutionId, Now, [], new Dictionary<string, string>());

    public static WorkflowExecutionState WorkflowState(
        string workflowExecutionId = WorkflowId,
        WorkflowExecutionStatus status = WorkflowExecutionStatus.Running,
        string? correlationId = null,
        IReadOnlyDictionary<string, ValueEnvelope>? variables = null) =>
        new(
            workflowExecutionId,
            Identity,
            status,
            null,
            Now,
            Now,
            Now,
            null,
            correlationId,
            null,
            null,
            new Dictionary<string, string>())
        {
            RootVariableFrame = variables is null ? null : RootFrame(workflowExecutionId, variables)
        };

    public static VariableFrameState RootFrame(
        string workflowExecutionId,
        IReadOnlyDictionary<string, ValueEnvelope> variables) =>
        new("root", "workflow", workflowExecutionId, null, VariableFrameKind.Root, variables);

    // ---- value envelopes: one builder per disposition the enricher has to tell apart --------------------------------

    public static ValueEnvelope Inline(string value) =>
        ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), InlinePolicy);

    public static ValueEnvelope Secret(string value) =>
        ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), SensitivePolicy);

    public static ValueEnvelope External(string locator = "blob-1", string storageProfile = "profile-1") =>
        ValueEnvelope.External(
            StringType,
            new DurableValueExternalReference(storageProfile, locator, new Dictionary<string, string>()),
            ExternalPolicy);

    public static ValueEnvelope NullValue() => ValueEnvelope.Null(StringType, InlinePolicy);

    public static ValueEnvelope AbsentValue() => ValueEnvelope.Absent(StringType, InlinePolicy);

    /// <summary>Root frame contents keyed by variable name, for the disposition-specific cases.</summary>
    public static IReadOnlyDictionary<string, ValueEnvelope> Variables(
        params (string Name, ValueEnvelope Envelope)[] variables) =>
        variables.ToDictionary(variable => variable.Name, variable => variable.Envelope, StringComparer.Ordinal);

    /// <summary>Root frame contents for the common case where every variable is a plain inline string.</summary>
    public static IReadOnlyDictionary<string, ValueEnvelope> InlineVariables(
        params (string Name, string Value)[] variables) =>
        Variables(variables.Select(variable => (variable.Name, Inline(variable.Value))).ToArray());

    public static RuntimeStateChange<ActivityExecutionInspectionProjection> Inspection(
        string activityExecutionId = "act-1",
        string workflowExecutionId = WorkflowId,
        string activityType = "Elsa.WriteLine",
        string authoredActivityId = "authored-1",
        ActivityExecutionStatus status = ActivityExecutionStatus.Completed,
        IReadOnlyCollection<string>? outcomes = null,
        long executionSequence = 1) =>
        new(
            activityExecutionId,
            RuntimeStateChangeOperation.Upsert,
            new ActivityExecutionInspectionProjection(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: workflowExecutionId,
                ExecutableNodeId: "node-1",
                AuthoredActivityId: authoredActivityId,
                ActivityType: activityType,
                ActivityTypeVersion: "1",
                Status: status,
                SubStatus: null,
                ExecutionSequence: executionSequence,
                ScheduledAt: Now,
                StartedAt: Now,
                CompletedAt: Now,
                FirstCheckpointId: "cp-1",
                LastCheckpointId: "cp-1",
                LastCommittedAt: Now,
                Provenance: ActivitySchedulingProvenance.Empty,
                OutcomeNames: outcomes ?? ["Done"],
                Bookmarks: [],
                Incidents: [],
                ValueSnapshots: [],
                Metadata: new Dictionary<string, string>()),
            new Dictionary<string, string>());

    public static RuntimeStateChange<IncidentState> Incident(
        string incidentId = "incident-1",
        string workflowExecutionId = WorkflowId,
        string activityExecutionId = "act-1",
        string message = "Boom.") =>
        new(
            incidentId,
            RuntimeStateChangeOperation.Upsert,
            new IncidentState(
                incidentId,
                workflowExecutionId,
                activityExecutionId,
                "node-1",
                IncidentSeverity.Error,
                IncidentStatus.Open,
                null,
                "System.InvalidOperationException",
                message,
                Now,
                null),
            new Dictionary<string, string>());

    // ---- store-facing shapes: the enricher has already made every capture decision by this point -------------------

    public static ExecutionEvidenceCommitBatch Batch(
        string checkpointId = "cp-1",
        string workflowExecutionId = WorkflowId,
        string? correlationId = null,
        bool terminal = false,
        IReadOnlyList<ExecutionEvidenceRecord>? records = null,
        IReadOnlyDictionary<string, ExecutionEvidenceVariableCapture>? rootVariables = null) =>
        new()
        {
            WorkflowExecutionId = workflowExecutionId,
            CheckpointId = checkpointId,
            OccurredAt = Now,
            CorrelationId = correlationId,
            Terminal = terminal,
            Records = records ?? [Record(workflowExecutionId, checkpointId)],
            RootVariables = rootVariables
        };

    public static ExecutionEvidenceRecord Record(
        string workflowExecutionId = WorkflowId,
        string checkpointId = "cp-1",
        string kind = RuntimeCheckpointNames.WorkflowStarted) =>
        new()
        {
            WorkflowExecutionId = workflowExecutionId,
            Kind = kind,
            OccurredAt = Now,
            CheckpointId = checkpointId
        };

    /// <summary>
    /// A capture carrying an inline value. The comparand shape is the store's only input to the diff, so these tests
    /// use a deliberately simple one rather than restating the enricher's format: the store's contract is "equal
    /// comparands mean equal state", nothing more.
    /// </summary>
    public static ExecutionEvidenceVariableCapture Captured(string value) =>
        new()
        {
            Disposition = ExecutionEvidenceValueDisposition.Captured,
            Value = JsonSerializer.SerializeToElement(value),
            Comparand = $"Captured:{value}"
        };

    /// <summary>A capture the enricher withheld: no inline value, but still a comparand that tracks the change.</summary>
    public static ExecutionEvidenceVariableCapture Withheld(
        ExecutionEvidenceValueDisposition disposition,
        string comparand) =>
        new()
        {
            Disposition = disposition,
            Comparand = $"{disposition}:{comparand}"
        };

    public static IReadOnlyDictionary<string, ExecutionEvidenceVariableCapture> Captures(
        params (string Name, ExecutionEvidenceVariableCapture Capture)[] captures) =>
        captures.ToDictionary(capture => capture.Name, capture => capture.Capture, StringComparer.Ordinal);

    /// <summary>Captured inline values keyed by variable name, in the shape the store's diff consumes.</summary>
    public static IReadOnlyDictionary<string, ExecutionEvidenceVariableCapture> CapturedVariables(
        params (string Name, string Value)[] variables) =>
        Captures(variables.Select(variable => (variable.Name, Captured(variable.Value))).ToArray());
}
