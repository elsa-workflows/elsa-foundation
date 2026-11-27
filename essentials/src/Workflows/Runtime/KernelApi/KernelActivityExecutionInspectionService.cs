using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Inspection.Core;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelActivityExecutionInspectionService(KernelRunReader runs, KernelActivityTimeline timeline,
    IActivityInspectionContextAsync authorization, ActivityExecutionInspectionService? legacy = null) : IActivityExecutionInspectionService
{
    public async Task<ActivityExecutionInspectionView?> GetAsync(GetActivityExecution request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivityExecutionId);
        if (await runs.GetAsync(request.WorkflowExecutionId, cancellationToken) is not { } run) return null;
        if (run.Header is null) return legacy is null ? null : await legacy.GetAsync(run.State, request, cancellationToken);
        if (!await authorization.CanInspectStructureAsync(run.State, cancellationToken) ||
            await timeline.OpenAsync(run, upperRevision: null, request.ActivityExecutionId, cancellationToken) is not { } snapshot ||
            await snapshot.Activities.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken) is not { } activity)
            return null;

        return ActivityExecutionInspectionView.From(activity,
            canInspectSensitiveValues: await authorization.CanInspectSensitiveValuesAsync(run.State, cancellationToken));
    }
}
