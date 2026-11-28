using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Reads activity executions from the configured workflow runtime stores.</summary>
public sealed class ActivityExecutionBrowseSource(IWorkflowExecutionStateStore? workflows = null,
    IActivityExecutionInspectionStore? inspections = null) : IActivityExecutionBrowseSource
{
    public bool SupportsExecutionBrowsing => inspections?.SupportsExecutionBrowsing == true;

    public async ValueTask<ActivityExecutionBrowseRun?> FindWorkflowAsync(string run, CancellationToken cancellationToken) =>
        workflows is not null && await workflows.FindAsync(run, cancellationToken) is { } workflow ? For(workflow) : null;

    /// <summary>Binds the legacy browse operation to a workflow already found by the caller.</summary>
    public ActivityExecutionBrowseRun For(WorkflowExecutionState workflow) =>
        new(workflow, SupportsExecutionBrowsing, (query, journalHeadRevision, cancellationToken) =>
            journalHeadRevision is null && query.WorkflowExecutionId == workflow.WorkflowExecutionId
                ? BrowseAsync(query, cancellationToken)
                : ValueTask.FromResult<ActivityExecutionBrowseResult?>(null));

    private async ValueTask<ActivityExecutionBrowseResult?> BrowseAsync(ActivityExecutionBrowseQuery query,
        CancellationToken cancellationToken) => inspections is not null
            ? new(await inspections.BrowseAsync(query, cancellationToken))
            : throw new NotSupportedException("This host does not support activity execution browsing.");
}
