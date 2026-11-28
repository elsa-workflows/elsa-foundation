using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>
/// A kernel shell's workflow-instance list (Studio's Runs page): the kernel's runs in the host's bound tenant, and no other
/// runs. Each request reads the tenant's runs from the foundation (those of one definition when the request names one),
/// maps them to <see cref="WorkflowExecutionState"/>, and uses the shared runtime API list projection for filtering,
/// paging and per-run authorization.
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
        var states = new Dictionary<string, WorkflowExecutionState>(StringComparer.Ordinal);
        if (KernelRunStates.CanReadTenant(access.Current, runs.Tenant))
        {
            var identities = new Dictionary<string, WorkflowExecutableIdentity?>(StringComparer.Ordinal);
            var definition = string.IsNullOrWhiteSpace(request.DefinitionId) ? null : request.DefinitionId;
            await foreach (var (view, run) in runs.ListAsync(definition, cancellationToken))
            {
                if (!identities.TryGetValue(run.Workflow, out var identity))
                    identities[run.Workflow] = identity = (await executables.FindAsync(run.Workflow, cancellationToken))?.Identity;
                cancellationToken.ThrowIfCancellationRequested();
                var state = KernelRunStates.From(view, run, identity, runs.Tenant.Tenant);
                ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
                states[state.WorkflowExecutionId] = state;
            }
        }
        return await WorkflowInstanceListProjection.ListSnapshotAsync(states.Values, authorization, request, cancellationToken);
    }
}
