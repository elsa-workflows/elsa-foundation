using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class MissingBookmarkResumeSchedulerWorkHandler : IFallbackWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(MissingBookmarkResumeSchedulerWorkHandler);

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.ResumeBookmark;
    }

    public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException(
            $"ResumeBookmark scheduler work item '{workItem.WorkItemId}' cannot be handled because no bookmark resume provider is registered. " +
            "Compose a bookmark resume runtime feature that contributes a ResumeBookmark scheduler work handler.");
    }
}
