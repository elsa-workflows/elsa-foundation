namespace Elsa.Workflows.Runtime.Core.Models;

public static class WorkflowDispatchDurabilityComponents
{
    public const string Checkpoint = "checkpoint";
    public const string DispatchStore = "dispatch-store";
    public const string Outbox = "outbox";
    public const string Scheduler = "scheduler";
    public const string Resumption = "resumption";

    public static readonly IReadOnlyCollection<string> Required =
    [Checkpoint, DispatchStore, Outbox, Scheduler, Resumption];
}

public enum WorkflowDispatchDurabilityLevel
{
    ProcessLocal = 0,
    Durable = 1
}

public enum WorkflowDispatchReadinessGuarantee
{
    ProcessLocal = 0,
    DurableReady = 1,
    Unsafe = 2
}

public sealed record WorkflowDispatchReadinessComponent(
    string Component,
    WorkflowDispatchDurabilityLevel? Level,
    string ReasonCode);

public sealed record WorkflowDispatchReadinessReport(
    WorkflowDispatchReadinessGuarantee Guarantee,
    bool Ready,
    IReadOnlyCollection<WorkflowDispatchReadinessComponent> Components,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record WorkflowDispatchDurabilityEvidence(
    string Component,
    WorkflowDispatchDurabilityLevel Level) : Contracts.IWorkflowDispatchDurabilityEvidence;
