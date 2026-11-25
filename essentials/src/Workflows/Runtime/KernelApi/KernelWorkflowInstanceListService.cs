using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>
/// A kernel shell's workflow-instance list (Studio's Runs page): the kernel's runs in the host's bound tenant, and no other
/// runs. Each request reads the tenant's runs from the foundation (those of one definition when the request names one),
/// maps them to <see cref="WorkflowExecutionState"/>, and lists them through the runtime API's own list service over an
/// in-memory store, so filters, paging cursors and per-run authorization behave as they do on the old core.
/// </summary>
/// <remarks>
/// Preview limit: the read is whole, once per request. A kernel list query that pages in the store comes later (K14).
/// </remarks>
internal sealed class KernelWorkflowInstanceListService(
    KernelRuns runs,
    IWorkflowExecutableStore executables,
    IPersistenceAccessContextAccessor access,
    IActivityInspectionContextAsync authorization) : IWorkflowInstanceListService
{
    public async Task<WorkflowInstanceListView> ListAsync(ListWorkflowInstances request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var store = new InMemoryWorkflowExecutionStateStore();
        if (ReadsHostTenant())
        {
            var identities = new Dictionary<string, WorkflowExecutableIdentity?>(StringComparer.Ordinal);
            var definition = string.IsNullOrWhiteSpace(request.DefinitionId) ? null : request.DefinitionId;
            await foreach (var (view, run) in runs.ListAsync(definition, cancellationToken))
            {
                if (!identities.TryGetValue(run.Workflow, out var identity))
                    identities[run.Workflow] = identity = (await executables.FindAsync(run.Workflow, cancellationToken))?.Identity;
                await store.SaveAsync(KernelRunStates.From(view, run, identity, runs.Tenant.Tenant), cancellationToken);
            }
        }
        // The old core's activity and incident stores hold nothing for a kernel run: empty ones in memory answer its counts
        // (0) without a database round trip per row.
        return await new WorkflowInstanceListService(store, new InMemoryActivityExecutionStateStore(), new InMemoryIncidentStateStore(), authorization)
            .ListAsync(request, cancellationToken);
    }

    // The kernel's runs are its bound tenant's: a caller reading that tenant, and only that one, sees them.
    private bool ReadsHostTenant() =>
        access.Current is { AccessPolicy: PersistenceAccessPolicy.Ordinary, AcrossScopes: false, Scope.Value: { } scope } &&
        string.Equals(scope, runs.Tenant.Tenant, StringComparison.Ordinal);
}
