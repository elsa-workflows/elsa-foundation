using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Immutable model-owned address of one actual native service invocation; no Design lookup is required.</summary>
public sealed class BpmnNativeWorkAddress(int formatVersion, RuntimeModelNativeWorkOrigin origin, string? tenantId,
    string logicalDefinitionId, string elementId, string workerRootNodeId, string workerRootActivityType,
    string workerRootActivityTypeVersion, ActivityInvocationContractIdentity workerRootContract, BpmnServiceFaultPolicy? faultPolicy)
{
    public int FormatVersion { get; } = formatVersion;
    public RuntimeModelNativeWorkOrigin Origin { get; } = origin;
    public string? TenantId { get; } = tenantId;
    /// <summary>The process's unmodified ordinal ProcessId; its material DefinitionId is content-derived.</summary>
    public string LogicalDefinitionId { get; } = logicalDefinitionId;
    public string ElementId { get; } = elementId;
    public string WorkerRootNodeId { get; } = workerRootNodeId;
    public string WorkerRootActivityType { get; } = workerRootActivityType;
    public string WorkerRootActivityTypeVersion { get; } = workerRootActivityTypeVersion;
    public ActivityInvocationContractIdentity WorkerRootContract { get; } = workerRootContract;
    public BpmnServiceFaultPolicy? FaultPolicy { get; } = faultPolicy;
}

/// <summary>Semantic root consumption is distinct from transport settlement and native-helper completion.</summary>
public enum BpmnNativeWorkObservationKind
{
    NotConsumed = 0,
    AcceptedRootInput = 1,
    RootTerminal = 2
}

/// <summary>Exact owner evidence. Only AcceptedRootInput carries a positively accepted matching input.</summary>
public sealed class BpmnNativeWorkObservation(BpmnNativeWorkAddress address, BpmnNativeWorkObservationKind kind,
    long modelRevision, RuntimeModelLifecycle lifecycle, string? terminalStatus, string? modelReceiptId,
    BpmnNativeWorkInput? acceptedInput, string? terminalReceiptId = null)
{
    public BpmnNativeWorkAddress Address { get; } = address;
    public BpmnNativeWorkObservationKind Kind { get; } = kind;
    public long ModelRevision { get; } = modelRevision;
    public RuntimeModelLifecycle Lifecycle { get; } = lifecycle;
    public string? TerminalStatus { get; } = terminalStatus;
    public string? ModelReceiptId { get; } = modelReceiptId;
    public BpmnNativeWorkInput? AcceptedInput { get; } = acceptedInput;
    /// <summary>Independent positive exact-root terminal proof, including when an accepted input also remains retained.</summary>
    public string? TerminalReceiptId { get; } = terminalReceiptId;
}

/// <summary>Retained Accepted classification for the exact native-result input and its physically validated result source.</summary>
public sealed class BpmnNativeWorkInput(string inputId, string contentFingerprint, long acceptedRevision,
    DateTimeOffset acceptedAt, BpmnNativeWorkResultSnapshot result)
{
    public string InputId { get; } = inputId;
    public string ContentFingerprint { get; } = contentFingerprint;
    public long AcceptedRevision { get; } = acceptedRevision;
    public DateTimeOffset AcceptedAt { get; } = acceptedAt;
    public BpmnNativeWorkResultSnapshot Result { get; } = result;
}

/// <summary>Policy-safe native terminal projection linked to its actual durable outbox source.</summary>
public sealed class BpmnNativeWorkResultSnapshot(string sourceOutboxItemId, WorkflowExecutionStatus status,
    BpmnNativeWorkCompletion? completion, string? faultCode)
{
    public string SourceOutboxItemId { get; } = sourceOutboxItemId;
    public WorkflowExecutionStatus Status { get; } = status;
    public BpmnNativeWorkCompletion? Completion { get; } = completion;
    public string? FaultCode { get; } = faultCode;
}

/// <summary>Useful root completion exposed only when the retained value policy permits it.</summary>
public sealed class BpmnNativeWorkCompletion(string invocationId, string attemptId, string contractFingerprint,
    string outcomeKey, ValueTypeDescriptor resultType, JsonElement? result, bool isRedacted)
{
    public string InvocationId { get; } = invocationId;
    public string AttemptId { get; } = attemptId;
    public string ContractFingerprint { get; } = contractFingerprint;
    public string OutcomeKey { get; } = outcomeKey;
    public ValueTypeDescriptor ResultType { get; } = resultType;
    public JsonElement? Result { get; } = result?.Clone();
    public bool IsRedacted { get; } = isRedacted;
}

/// <summary>A live result's exact root input and physically validated terminal worker, before any semantic advancement.</summary>
/// <remarks>Worker is a native runtime header, not BPMN's private model document. Contributions revalidate their own
/// retained native result binding and compare existing permissions; they do not infer authority from this object alone.</remarks>
public sealed class BpmnNativeWorkResultValidation(RuntimeModelNativeWorkOrigin origin, string inputId,
    string contentFingerprint, BpmnNativeWorkResultSnapshot result, WorkflowExecutionState worker)
{
    public RuntimeModelNativeWorkOrigin Origin { get; } = origin;
    public string InputId { get; } = inputId;
    public string ContentFingerprint { get; } = contentFingerprint;
    public BpmnNativeWorkResultSnapshot Result { get; } = result;
    public WorkflowExecutionState Worker { get; } = worker;
}

/// <summary>The model-owned semantic classification of an exact native-result input receipt.</summary>
public enum BpmnNativeWorkInputDisposition
{
    Accepted = 0,
    Rejected = 1
}

/// <summary>Physically validated result and retained root receipt before delivery of the existing result source.</summary>
/// <remarks>TerminalReceiptId is independent positive exact-root proof. Rejected with a live root leaves any
/// unconsumed integration responsibility owed. Contributors must still authenticate their own durable state.</remarks>
public sealed class BpmnNativeWorkResultDelivery(RuntimeModelNativeWorkOrigin origin, WorkflowExecutionState worker,
    BpmnNativeWorkResultSnapshot result, RuntimeModelInputReceipt inputReceipt, BpmnNativeWorkInputDisposition disposition,
    long modelRevision, RuntimeModelLifecycle lifecycle, string? terminalStatus, string? terminalReceiptId)
{
    public RuntimeModelNativeWorkOrigin Origin { get; } = origin;
    public WorkflowExecutionState Worker { get; } = worker;
    public BpmnNativeWorkResultSnapshot Result { get; } = result;
    public RuntimeModelInputReceipt InputReceipt { get; } = inputReceipt;
    public BpmnNativeWorkInputDisposition Disposition { get; } = disposition;
    public long ModelRevision { get; } = modelRevision;
    public RuntimeModelLifecycle Lifecycle { get; } = lifecycle;
    public string? TerminalStatus { get; } = terminalStatus;
    public string? TerminalReceiptId { get; } = terminalReceiptId;
}
