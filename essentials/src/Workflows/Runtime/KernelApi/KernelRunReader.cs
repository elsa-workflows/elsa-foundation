using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelRunReader(KernelRuns runs, IWorkflowExecutableStore executables, IPersistenceAccessContextAccessor access,
    KernelTestRuns? testRuns = null, KernelPreviewRunReads? preview = null,
    IWorkflowTestScopeStore? testScopes = null, IWorkflowExecutionPartitionAccessor? partitions = null, TimeProvider? clock = null)
{
    internal sealed record Run(WorkflowExecutionState State, WorkflowRunHeader? Header, WorkflowExecutable? Executable,
        ActorView? View = null, KernelRuns? IsolatedReads = null);

    internal ValueTask<ActorTerminalOutput?> ReadTerminalOutputAsync(Run run, bool includeValues, CancellationToken cancellationToken) =>
        run.Header is not null && run.State is { Status: WorkflowExecutionStatus.Completed, CompletedAt: { } ended }
            ? (run.IsolatedReads ?? runs).ReadTerminalOutputAsync(new ActorRef(run.State.WorkflowExecutionId),
                ended.ToUnixTimeMilliseconds(), includeValues, cancellationToken)
            : ValueTask.FromResult<ActorTerminalOutput?>(null);

    internal async ValueTask<Run?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (access.Current is not { AccessPolicy: PersistenceAccessPolicy.Ordinary, AcrossScopes: false, Scope.Value: { } tenant })
            return null;
        if (tenant != runs.Tenant.Tenant) return await FindTestRunAsync();
        var actor = new ActorRef(id);
        var (missing, found) = await runs.GetAsync(actor, cancellationToken);
        if (missing)
        {
            if (testRuns is { Reads: { } isolated })
            {
                var test = await isolated.GetAsync(actor, cancellationToken);
                if (!test.Missing)
                {
                    if (test.Run is not { } selected || !await HasTestScopeAsync(selected.View.Header, selected.Run.TestStart, tenant, cancellationToken))
                        return null;
                    var testExecutable = await executables.FindAsync(selected.Run.Workflow, cancellationToken);
                    return new Run(KernelRunStates.From(selected.View, selected.Run, testExecutable?.Identity, tenant),
                        selected.Run, testExecutable, selected.View, isolated);
                }
            }
            return await FindTestRunAsync();
        }
        if (found is not { Run.TestStart: null } kernel) return null;

        var (view, run) = kernel;
        var executable = await executables.FindAsync(run.Workflow, cancellationToken);
        return new Run(KernelRunStates.From(view, run, executable?.Identity, tenant), run, executable, view);

        async ValueTask<Run?> FindTestRunAsync()
        {
            var legacy = preview?.Runs is { } legacyRuns ? await legacyRuns.FindAsync(id, cancellationToken) : null;
            return legacy is { RunKind: WorkflowRunKind.TestRun } && legacy.WorkflowExecutionId == id && legacy.TenantId == tenant
                ? new Run(legacy, null, null)
                : null;
        }
    }

    private async ValueTask<bool> HasTestScopeAsync(ActorHeader header, WorkflowTestStart? test, string tenant, CancellationToken token)
    {
        if (test is null || testScopes is null || test.ScopeId != test.SourceReferenceId ||
            (test.Tenant ?? PersistenceScope.DefaultValue) != tenant ||
            test.Partition != (partitions?.Current.Value ?? WorkflowExecutionPartition.DefaultValue) ||
            test.ExpiresAt <= (clock ?? TimeProvider.System).GetUtcNow())
            return false;
        var stored = await testScopes.FindAsync(test.ScopeId, token);
        // The attached-birth transaction proved this stamp against the actual provider parent and the durable root binding.
        var root = header.Parent is null ? header.Ref.Id : test.RootActor;
        return stored is { IsolatedActor: { } binding } && binding.ActorReference == root &&
            WorkflowTestScope.ContextEquals(stored.Scope, new(test.ScopeId, test.ExpiresAt, test.Tenant, new(test.Partition)));
    }
}
