using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>One persisted execution and its detached, model-owned inspection projection.</summary>
/// <remarks>Transports authorize the exact State snapshot and explicitly project its header; never serialize its private Payload.</remarks>
public sealed class BpmnModelInspection
{
    public BpmnModelInspection(RuntimeModelExecutionState state, string profile, JsonElement process, BpmnInspectionSource? source,
        IReadOnlyList<BpmnInspectionWork> work, IReadOnlyList<BpmnInspectionAttempt> attempts,
        IReadOnlyList<BpmnServiceIncident> incidents, IReadOnlyList<BpmnInspectionSettlement> settlements)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        if (process.ValueKind != JsonValueKind.Object) throw new ArgumentException("Inspection requires its process object.", nameof(process));
        ArgumentNullException.ThrowIfNull(work); ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(incidents); ArgumentNullException.ThrowIfNull(settlements);
        State = state; Profile = profile; Process = process.Clone(); Source = source;
        Work = Array.AsReadOnly(work.ToArray()); Attempts = Array.AsReadOnly(attempts.ToArray());
        Incidents = Array.AsReadOnly(incidents.ToArray()); Settlements = Array.AsReadOnly(settlements.ToArray());
    }
    public RuntimeModelExecutionState State { get; }
    public string Profile { get; }
    public JsonElement Process { get; }
    public BpmnInspectionSource? Source { get; }
    public IReadOnlyList<BpmnInspectionWork> Work { get; }
    public IReadOnlyList<BpmnInspectionAttempt> Attempts { get; }
    public IReadOnlyList<BpmnServiceIncident> Incidents { get; }
    public IReadOnlyList<BpmnInspectionSettlement> Settlements { get; }
}

/// <summary>Frozen provenance/presentation only; authored inputs and connector values are excluded.</summary>
public sealed class BpmnInspectionSource
{
    public BpmnInspectionSource(WorkflowExecutableSourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        SourceReferenceId = reference.SourceReferenceId; SourceKind = reference.SourceKind;
        SourceId = reference.SourceId; SourceVersion = reference.SourceVersion;
        CreatedAt = reference.CreatedAt; PublishedAt = reference.PublishedAt; RetiredAt = reference.DeletedAt;
        Layout = Array.AsReadOnly(reference.Layout.Select(record => record with { AdditionalProperties = record.AdditionalProperties?.Clone() }).ToArray());
        ActivityPresentation = Array.AsReadOnly(reference.ActivityPresentation.ToArray());
    }
    public string SourceReferenceId { get; }
    public string SourceKind { get; }
    public string SourceId { get; }
    public string? SourceVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? PublishedAt { get; }
    public DateTimeOffset? RetiredAt { get; }
    public IReadOnlyList<WorkflowExecutableLayoutRecord> Layout { get; }
    public IReadOnlyList<WorkflowExecutableActivityPresentationRecord> ActivityPresentation { get; }
}

/// <summary>Current committed work, not inferred node history or a claim about a native worker's running state.</summary>
public sealed class BpmnInspectionWork(string kind, string elementId, string bindingId, string tokenId, string? iterationId,
    string invocationId, string attemptId, string? workerExecutionId, DateTimeOffset? dueAt)
{
    public string Kind { get; } = kind;
    public string ElementId { get; } = elementId;
    public string BindingId { get; } = bindingId;
    public string TokenId { get; } = tokenId;
    public string? IterationId { get; } = iterationId;
    public string InvocationId { get; } = invocationId;
    public string AttemptId { get; } = attemptId;
    public string? WorkerExecutionId { get; } = workerExecutionId;
    public DateTimeOffset? DueAt { get; } = dueAt;
}

/// <summary>One retained physical native attempt. Requested does not mean that the native worker has started.</summary>
public sealed class BpmnInspectionAttempt(string bindingId, string invocationId, string attemptId,
    string workerExecutionId, WorkflowExecutableIdentity workerExecutable, string status, int? attemptNumber,
    string? outcomeKey, string? faultCode)
{
    public string BindingId { get; } = bindingId;
    public string InvocationId { get; } = invocationId;
    public string AttemptId { get; } = attemptId;
    public string WorkerExecutionId { get; } = workerExecutionId;
    public WorkflowExecutableIdentity WorkerExecutable { get; } = workerExecutable;
    public string Status { get; } = status;
    public int? AttemptNumber { get; } = attemptNumber;
    public string? OutcomeKey { get; } = outcomeKey;
    public string? FaultCode { get; } = faultCode;
}

/// <summary>A committed native cancellation or terminal parent-consumption obligation and its actual proof.</summary>
public sealed class BpmnInspectionSettlement(string kind, string executionId, string? invocationId, string? attemptId,
    bool settled, bool notAdmitted)
{
    public string Kind { get; } = kind;
    public string ExecutionId { get; } = executionId;
    public string? InvocationId { get; } = invocationId;
    public string? AttemptId { get; } = attemptId;
    public bool Settled { get; } = settled;
    public bool NotAdmitted { get; } = notAdmitted;
}
