using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelRunReader(KernelRuns runs, IWorkflowExecutableStore executables, IPersistenceAccessContextAccessor access,
    IWorkflowExecutionStateStore? legacyRuns = null)
{
    internal sealed record Run(WorkflowExecutionState State, WorkflowRunHeader? Header, WorkflowExecutable? Executable);

    internal async ValueTask<Run?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (access.Current is not { AccessPolicy: PersistenceAccessPolicy.Ordinary, AcrossScopes: false, Scope.Value: { } tenant })
            return null;
        if (tenant != runs.Tenant.Tenant) return await FindTestRunAsync();
        var (missing, found) = await runs.GetAsync(new ActorRef(id), cancellationToken);
        if (missing) return await FindTestRunAsync();
        if (found is not { } kernel) return null;

        var (view, run) = kernel;
        var executable = await executables.FindAsync(run.Workflow, cancellationToken);
        return new Run(KernelRunStates.From(view, run, executable?.Identity, tenant), run, executable);

        async ValueTask<Run?> FindTestRunAsync()
        {
            var legacy = legacyRuns is null ? null : await legacyRuns.FindAsync(id, cancellationToken);
            return legacy is { RunKind: WorkflowRunKind.TestRun } && legacy.WorkflowExecutionId == id && legacy.TenantId == tenant
                ? new Run(legacy, null, null)
                : null;
        }
    }
}
