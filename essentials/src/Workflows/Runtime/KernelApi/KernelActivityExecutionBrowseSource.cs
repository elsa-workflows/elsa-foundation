using Elsa.Workflows.Runtime.Api.Handlers;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelActivityExecutionBrowseSource(KernelRunReader runs, KernelActivityTimeline timeline) : IActivityExecutionBrowseSource
{
    public bool SupportsExecutionBrowsing => true;

    public async ValueTask<ActivityExecutionBrowseRun?> FindWorkflowAsync(string run, CancellationToken cancellationToken)
    {
        if (await runs.GetAsync(run, cancellationToken) is not { } found) return null;
        return new ActivityExecutionBrowseRun(found.State, true, async (query, journalHeadRevision, cancellation) =>
        {
            if (query.WorkflowExecutionId != run || await timeline.OpenAsync(found, journalHeadRevision, activity: null, cancellation) is not { } snapshot)
                return null;
            return new ActivityExecutionBrowseResult(await snapshot.Activities.BrowseAsync(query, cancellation), snapshot.Revision);
        });
    }
}
