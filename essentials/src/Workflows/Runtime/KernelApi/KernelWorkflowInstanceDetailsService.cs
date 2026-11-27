using System.Collections.Frozen;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelWorkflowInstanceDetailsService(
    KernelRuns runs,
    IWorkflowExecutableStore executables,
    IPersistenceAccessContextAccessor access,
    IActivityInspectionContextAsync authorization,
    WorkflowInstanceDetailsService testRuns) : IWorkflowInstanceDetailsService
{
    // A kernel turn commits one durable boundary and can execute several activities within it.
    private static readonly RuntimePersistenceModeProjection Mode = RuntimePersistenceModeProjection.For(PersistenceMode.Balanced);
    private static readonly IReadOnlyDictionary<string, WorkflowOutputView> Outputs = FrozenDictionary<string, WorkflowOutputView>.Empty;

    public async Task<WorkflowInstanceDetailsView?> GetAsync(GetWorkflowInstance request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);
        if (access.Current is not { AccessPolicy: PersistenceAccessPolicy.Ordinary, AcrossScopes: false, Scope.Value: { } tenant })
            return null;

        if (!string.Equals(tenant, runs.Tenant.Tenant, StringComparison.Ordinal))
            return await testRuns.GetTestRunAsync(request, tenant, cancellationToken);

        var (missing, found) = await runs.GetAsync(new ActorRef(request.WorkflowExecutionId), cancellationToken);
        if (missing)
            return await testRuns.GetTestRunAsync(request, tenant, cancellationToken);
        if (found is not { } committed)
            return null;

        var (view, run) = committed;
        var identity = (await executables.FindAsync(run.Workflow, cancellationToken))?.Identity;
        var state = KernelRunStates.From(view, run, identity, runs.Tenant.Tenant);
        if (!await authorization.CanInspectStructureAsync(state, cancellationToken))
            return null;

        var canInspectSensitive = await authorization.CanInspectSensitiveValuesAsync(state, cancellationToken);
        return new WorkflowInstanceDetailsView(
            WorkflowInstanceSummaryView.From(state, canInspectSensitiveValues: canInspectSensitive),
            [],
            [],
            Outputs,
            Mode.PersistenceMode,
            Mode.InspectionGranularity,
            // The runtime's existing operator endpoints still address legacy executions.
            Permissions: new WorkflowInstancePermissionsView(CanManage: false, CanInspectValues: canInspectSensitive));
    }
}
