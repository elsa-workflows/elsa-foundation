using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class MissingGeneratedEventSchedulerWorkHandler : IFallbackWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(MissingGeneratedEventSchedulerWorkHandler);

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.GeneratedEvent;
    }

    public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException(
            $"GeneratedEvent scheduler work item '{workItem.WorkItemId}' cannot be handled because no generated-event provider is registered. " +
            "Compose a generator runtime feature that contributes a GeneratedEvent scheduler work handler.");
    }
}
