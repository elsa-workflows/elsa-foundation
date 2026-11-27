using System.Collections.Frozen;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelWorkflowInstanceDetailsService(
    KernelRunReader runs,
    IActivityInspectionContextAsync authorization,
    WorkflowInstanceDetailsService legacy) : IWorkflowInstanceDetailsService
{
    // A kernel turn commits one durable boundary and can execute several activities within it.
    private static readonly RuntimePersistenceModeProjection Mode = RuntimePersistenceModeProjection.For(PersistenceMode.Balanced);
    private static readonly IReadOnlyDictionary<string, WorkflowOutputView> Outputs = FrozenDictionary<string, WorkflowOutputView>.Empty;

    public async Task<WorkflowInstanceDetailsView?> GetAsync(GetWorkflowInstance request, CancellationToken cancellationToken)
    {
        if (await runs.GetAsync(request.WorkflowExecutionId, cancellationToken) is not { } run) return null;
        if (run.Header is null) return await legacy.GetAsync(run.State, request, cancellationToken);
        var state = run.State;
        if (!await authorization.CanInspectStructureAsync(state, cancellationToken)) return null;

        var canInspectSensitive = await authorization.CanInspectSensitiveValuesAsync(state, cancellationToken);
        return new WorkflowInstanceDetailsView(
            WorkflowInstanceSummaryView.From(state, canInspectSensitiveValues: canInspectSensitive),
            [],
            [],
            Outputs,
            Mode.PersistenceMode,
            Mode.InspectionGranularity,
            // The runtime's existing operator endpoints still address legacy executions.
            Permissions: new WorkflowInstancePermissionsView(CanManage: false, CanInspectValues: canInspectSensitive))
            { ActivitiesDeferred = KernelActivityTimeline.Supports(run.Header) };
    }
}
