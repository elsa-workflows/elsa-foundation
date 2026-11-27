using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelWorkflowExecutableRetentionReader(KernelRuns runs, IPersistenceAccessContextAccessor access,
    LegacyWorkflowExecutableRetentionReader? legacy = null, KernelTestRuns? testRuns = null,
    IWorkflowTestScopeStore? testScopes = null, IWorkflowExecutionPartitionAccessor? partitions = null,
    IRuntimeKernelExecutablePinStore? pins = null) : IWorkflowExecutableRetentionReader
{
    private const int PinPageSize = 64;

    public async ValueTask<WorkflowExecutableRetention> ReadAsync(WorkflowExecutableRetentionQuery query, CancellationToken cancellationToken)
    {
        var retention = legacy is null ? new WorkflowExecutableRetention() : await legacy.ReadAsync(query, cancellationToken);
        var context = access.Current;
        if (!KernelRunStates.CanReadTenant(context, runs.Tenant)) return retention;

        var scope = context.Scope!;
        var candidate = query.Source is { } source && (source.TenantId ?? PersistenceScope.DefaultValue) == runs.Tenant.Tenant &&
            (source.PublicationScope is null || source.PublicationScope == scope) ? source : null;
        // The actor captured the live source. Retirement may change these two facts after the committed birth.
        var digest = candidate is null ? null : KernelSourceStamp.Digest(candidate with { DeletedAt = null, DeletedReason = null });
        IEnumerable<string?> definitions = query.Definitions is { Count: > 0 } known ? known : [null];
        var isolated = testScopes is null ? null : testRuns?.Reads;
        var partition = partitions?.Current.Value ?? WorkflowExecutionPartition.DefaultValue;
        var bindings = new Dictionary<string, WorkflowTestScopeRecord?>(StringComparer.Ordinal);
        var seen = query.ArtifactId is null ? null : new HashSet<(bool Isolated, ActorRef Actor)>();
        foreach (var definition in definitions)
        {
            await foreach (var (view, run) in runs.ListAsync(definition, cancellationToken))
                await AddAsync(view, run, isIsolated: false);

            if (isolated is not null)
                await foreach (var (view, run) in isolated.ListTestRunsAsync(definition, cancellationToken))
                    await AddAsync(view, run, isIsolated: true);
        }

        // An accepted actor's definition label is projected by its first turn. For point reads, open
        // material pins identify potential unlabelled births. Each candidate still needs a committed ActorView.
        if (query.ArtifactId is { } artifact && seen is not null && pins is not null &&
            await pins.ReadOpenTailAsync(scope, cancellationToken) is { } tail)
        {
            string? after = null;
            do
            {
                var page = await pins.ListOpenAsync(scope, after, tail, PinPageSize, cancellationToken);
                foreach (var pin in page.Items)
                {
                    if (pin.Tenant != runs.Tenant.Tenant || pin.Executable.ArtifactId != artifact) continue;
                    var isIsolated = pin.IsolatedTenant is not null;
                    var selected = !isIsolated ? runs : isolated?.Tenant.Tenant == pin.IsolatedTenant ? isolated : null;
                    var actor = new ActorRef(pin.ActorReference);
                    if (selected is null || seen.Contains((isIsolated, actor))) continue;
                    var found = await selected.GetAsync(actor, cancellationToken);
                    if (found.Run is { } committed)
                        await AddAsync(committed.View, committed.Run, isIsolated);
                }

                after = page.Next;
            } while (after is not null);
        }

        return retention;

        async ValueTask AddAsync(ActorView view, WorkflowRunHeader run, bool isIsolated)
        {
            if (query.ArtifactId is not null && run.Workflow != query.ArtifactId ||
                isIsolated != (run.TestStart is not null) || seen is not null && !seen.Add((isIsolated, view.Header.Ref))) return;
            if (run.TestStart is not { } test)
            {
                retention.Add(run.Workflow, candidate is { Scope: WorkflowExecutableReferenceScope.Published } published &&
                    MatchesPublishedSource(run, published, digest));
                return;
            }

            if (test.ScopeId != test.SourceReferenceId || (test.Tenant ?? PersistenceScope.DefaultValue) != runs.Tenant.Tenant ||
                test.Partition != partition) return;
            if (!bindings.TryGetValue(test.ScopeId, out var stored))
                bindings[test.ScopeId] = stored = await testScopes!.FindAsync(test.ScopeId, cancellationToken);

            var root = view.Header.Parent is null ? view.Header.Ref.Id : test.RootActor;
            if (stored is not { IsolatedActor: { } binding } || binding.ActorReference != root ||
                stored.Scope.ScopeId != test.ScopeId || stored.Scope.TenantId != test.Tenant ||
                stored.Scope.Partition.Value != test.Partition || stored.Scope.ExpiresAt != test.ExpiresAt) return;

            // Expiry can leave a retained run. The inspector remains responsible for refusing retired TestRun sources.
            retention.Add(run.Workflow, candidate is { Scope: WorkflowExecutableReferenceScope.TestRun } testSource &&
                test.ScopeId == testSource.SourceReferenceId && run.Workflow == testSource.ArtifactId &&
                run.Definition == testSource.DefinitionId && test.SourceDigest == digest);
        }
    }

    private static bool MatchesPublishedSource(WorkflowRunHeader run, WorkflowExecutableSourceReference source, string? digest) =>
        run.PublishedStart is { SlotRevision: > 0 } publication && run.Workflow == source.ArtifactId &&
        run.Definition == source.DefinitionId && publication.SourceReferenceId == source.SourceReferenceId &&
        publication.ActivationId == source.ActivationId && publication.SlotId == source.SlotId && publication.SourceDigest == digest;
}
