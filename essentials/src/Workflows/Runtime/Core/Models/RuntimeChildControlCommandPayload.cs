using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Exact nonterminal control delivery address; source authorization remains a separate runtime gate.</summary>
public sealed class RuntimeChildControlCommandPayload : IActivityCommandPayload
{
    public const string DeliveryReason = "DeliverChildControl";

    public RuntimeChildControlCommandPayload(WorkflowExecutableIdentity pinnedExecutable, string executableNodeId,
        string activityExecutionId, string? parentActivityExecutionId, string? branchId, RuntimeChildControlTransport control)
    {
        RuntimeChildControlValidation.Pin(pinnedExecutable);
        RuntimeChildControlValidation.Text(executableNodeId, nameof(executableNodeId));
        RuntimeChildControlValidation.Text(activityExecutionId, nameof(activityExecutionId));
        if (parentActivityExecutionId is not null) RuntimeChildControlValidation.Text(parentActivityExecutionId, nameof(parentActivityExecutionId));
        if (branchId is not null) RuntimeChildControlValidation.Text(branchId, nameof(branchId));
        ArgumentNullException.ThrowIfNull(control);
        if (control.Batch.IsTerminal || control.ConsumerActivityExecutionId != activityExecutionId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(control.ParentExecutable, pinnedExecutable))
            throw new ArgumentException("Nonterminal control must address its exact pinned consumer.", nameof(control));
        PinnedExecutable = pinnedExecutable;
        ExecutableNodeId = executableNodeId;
        ActivityExecutionId = activityExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        BranchId = branchId;
        Control = control;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string ExecutableNodeId { get; }
    public string ActivityExecutionId { get; }
    public string? ParentActivityExecutionId { get; }
    public string? BranchId { get; }
    public RuntimeChildControlTransport Control { get; }
    public string Reason => DeliveryReason;
}
