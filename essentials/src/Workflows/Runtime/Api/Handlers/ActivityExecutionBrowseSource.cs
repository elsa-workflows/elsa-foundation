using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Supplies the exact run and its committed activity collection to the authorized browse endpoint.</summary>
public interface IActivityExecutionBrowseSource
{
    bool SupportsExecutionBrowsing { get; }

    ValueTask<ActivityExecutionBrowseRun?> FindWorkflowAsync(string run, CancellationToken cancellationToken);
}

/// <summary>An exact run and its browse operation. The endpoint authorizes the run before calling <see cref="BrowseAsync"/>.</summary>
public sealed record ActivityExecutionBrowseRun(
    WorkflowExecutionState State,
    bool SupportsExecutionBrowsing,
    Func<ActivityExecutionBrowseQuery, long?, CancellationToken, ValueTask<ActivityExecutionBrowseResult?>> BrowseAsync);

/// <summary>A browse page and the committed journal revision that fixes its result set across continuations, when available.</summary>
public sealed record ActivityExecutionBrowseResult(ActivityExecutionBrowsePage Page, long? JournalHeadRevision = null);

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
