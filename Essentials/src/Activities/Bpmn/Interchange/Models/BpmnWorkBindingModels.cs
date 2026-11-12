using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Interchange.Models;

/// <summary>Host-owned work description translated from a retained BPMN descriptor.</summary>
public sealed record BpmnWorkBindingRequest(
    string ElementId,
    string BindingRef,
    BpmnWorkBindingKind Kind,
    string? Value = null,
    string? WorkflowDefinitionId = null,
    bool WaitForCompletion = true,
    ActivityNode? NestedProcess = null);

public enum BpmnWorkBindingKind
{
    Unknown,
    TimerWait,
    TimerDateWait,
    TimerCycleWait,
    MessageWait,
    SignalWait,
    MessagePublish,
    SignalPublish,
    CallProcess,
    NestedProcess,
    UnboundTask
}

public sealed record BpmnWorkBindingResult(ActivityNode? Activity, BpmnImportCapabilityAvailability Availability, string Reason);
