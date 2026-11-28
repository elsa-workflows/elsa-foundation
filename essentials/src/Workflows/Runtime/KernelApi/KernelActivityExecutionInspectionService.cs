using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Inspection.Core;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelActivityExecutionInspectionService(KernelRunReader runs, KernelActivityTimeline timeline,
    IActivityInspectionContextAsync authorization) : IActivityExecutionInspectionService
{
    public async Task<ActivityExecutionInspectionView?> GetAsync(GetActivityExecution request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivityExecutionId);
        if (await runs.GetAsync(request.WorkflowExecutionId, cancellationToken) is not { } run) return null;
        if (!await authorization.CanInspectStructureAsync(run.State, cancellationToken) ||
            await timeline.OpenAsync(run, upperRevision: null, request.ActivityExecutionId, cancellationToken) is not { } snapshot ||
            await snapshot.Activities.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken) is not { } activity)
            return null;

        return ActivityExecutionInspectionView.From(activity,
            canInspectSensitiveValues: await authorization.CanInspectSensitiveValuesAsync(run.State, cancellationToken));
    }
}
