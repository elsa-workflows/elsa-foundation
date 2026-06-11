using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class ImmediateWorkflowSchedulerDrainPolicy : IWorkflowSchedulerDrainPolicy
{
    public ImmediateWorkflowSchedulerDrainPolicy(int? maxWorkItems = null)
    {
        if (maxWorkItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWorkItems), "The scheduler drain maximum work item count must be greater than zero when provided.");

        MaxWorkItems = maxWorkItems;
    }

    public int? MaxWorkItems { get; }

    public RuntimeSchedulerDrainRequest? CreateDrainRequest(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(workItem);

        return new RuntimeSchedulerDrainRequest(workItem.WorkflowExecutionId, MaxWorkItems);
    }
}
