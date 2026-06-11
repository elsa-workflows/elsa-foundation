using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class NoopWorkflowSchedulerWorkHandler : IFallbackWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(NoopWorkflowSchedulerWorkHandler);

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind is not WorkflowExecutionCommandKind.InvokeActivity
            and not WorkflowExecutionCommandKind.GeneratedEvent
            and not WorkflowExecutionCommandKind.ResumeBookmark;
    }

    public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
    }
}
