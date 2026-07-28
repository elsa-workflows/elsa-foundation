using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// End-to-end coverage for the burst-scoped executable read cache (ADR 0031 item b, spec 111) over the durable
// Groundwork substrate. The load-bearing ADR guardrail is that memory is a cache and NEVER a correctness dependency:
// a run with the cache disabled commits identical durable state to a run with it enabled, differing only in how many
// times the immutable pinned artifact was read. These tests drive the real dispatch/drain path (activate the actor,
// enqueue Start, drain to quiescence) and count executable-store FindAsync calls through a counting decorator whose
// tally is held in a shared singleton (the store is scoped, so the drain realizes several store instances).
public sealed class GroundworkBurstCacheGuardrailTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    // Guardrail (SC-002) + read-count collapse (SC-001): the cache-off and cache-on runs converge to identical
    // committed durable state (terminal activity snapshot and the multiset of persisted checkpoint-commit ids), while
    // the cache-on run performs strictly fewer — and few (≤ 3) — durable executable reads.
    [Fact]
    public async Task CacheOff_And_CacheOn_CommitIdenticalDurableState_ButCacheOnReadsFewer()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();

        var offStore = new InMemoryDocumentStore(manifest);
        int offReads;
        await using (var off = BuildProvider(offStore, cacheEnabled: false))
        {
            await SeedAndStartAsync(off);
            offReads = off.GetRequiredService<ExecutableReadCounter>().Count;
        }

        var onStore = new InMemoryDocumentStore(manifest);
        int onReads;
        await using (var on = BuildProvider(onStore, cacheEnabled: true))
        {
            await SeedAndStartAsync(on);
            onReads = on.GetRequiredService<ExecutableReadCounter>().Count;
        }

        var offSnapshot = await SnapshotActivityStateAsync(offStore);
        var onSnapshot = await SnapshotActivityStateAsync(onStore);

        Assert.NotEmpty(offSnapshot);
        Assert.Equal(offSnapshot, onSnapshot);
        Assert.Equal(await CommitIdsAsync(offStore), await CommitIdsAsync(onStore));

        Assert.True(offReads > onReads, $"Expected cache-on reads ({onReads}) < cache-off reads ({offReads}).");
        Assert.True(onReads <= 3, $"Expected cache-on executable reads ≤ 3 per run, was {onReads}.");
    }

    // Reconstructibility (SC-003 / US3): with the cache present but continuously evicted mid-drain (a clearing reader
    // decorator empties the burst scope after every read, modelling a burst that is repeatedly lost), the run still
    // completes to identical committed durable state — every hop simply re-reads durably. Proof the cache can never
    // become a correctness dependency: losing it changes only read count, never committed state.
    [Fact]
    public async Task ContinuouslyEvictedCache_ReconstructsFromDurableReads_AndCommitsIdenticalState()
    {
        var manifest = ElsaRuntimeStorageManifest.CreatePhysicalized();

        var controlStore = new InMemoryDocumentStore(manifest);
        await using (var control = BuildProvider(controlStore, cacheEnabled: false))
            await SeedAndStartAsync(control);

        var evictedStore = new InMemoryDocumentStore(manifest);
        int evictedReads;
        await using (var evicted = BuildProvider(evictedStore, cacheEnabled: true, clearAfterEachRead: true))
        {
            await SeedAndStartAsync(evicted);
            evictedReads = evicted.GetRequiredService<ExecutableReadCounter>().Count;
        }

        Assert.Equal(await SnapshotActivityStateAsync(controlStore), await SnapshotActivityStateAsync(evictedStore));
        Assert.Equal(await CommitIdsAsync(controlStore), await CommitIdsAsync(evictedStore));

        // A perpetually-lost cache re-reads on every hop, so its read count matches a run with no cache at all.
        Assert.True(evictedReads > 3, $"A continuously-evicted cache should re-read durably on every hop, was {evictedReads}.");
    }

    private static ServiceProvider BuildProvider(IDocumentStore store, bool cacheEnabled, bool clearAfterEachRead = false)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddSingleton(store);
        services.AddGroundworkRuntimeStores();

        // Deterministic activity-execution ids so both the cache-off and cache-on generations produce identical
        // durable state (commit ids embed the generated ids), making the guardrail a true byte-shape comparison.
        services.RemoveAll<IRuntimeExecutionIdGenerator>();
        services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(
            Enumerable.Range(0, 64).Select(index => $"actexec-{index}")));

        services.RemoveAll<RuntimeBurstCacheOptions>();
        services.AddSingleton(new RuntimeBurstCacheOptions { Enabled = cacheEnabled });

        // Shared tally across the scoped store instances the drain realizes.
        services.AddSingleton<ExecutableReadCounter>();

        // Wrap the durable executable store in a FindAsync counter. It sits UNDER the burst-cache reader, so on a cache
        // hit it is not called; the counter therefore measures durable reads the cache did not save.
        DecorateLast<IWorkflowExecutableStore>(services, (inner, sp) =>
            new CountingWorkflowExecutableStore(inner, sp.GetRequiredService<ExecutableReadCounter>()));

        if (clearAfterEachRead)
        {
            // Decorate the burst-cache reader so the scope is cleared after every read: the cache is present and used,
            // but immediately lost, forcing a durable re-read on the next hop.
            DecorateLast<IWorkflowExecutableReader>(services, (inner, sp) =>
                new ClearingWorkflowExecutableReader(inner, sp.GetRequiredService<IWorkflowBurstScopeAccessor>()));
        }

        return services.BuildServiceProvider();
    }

    // Replaces the last registration of TService with a decorator over it, preserving lifetime (mirrors the manual
    // decoration the coalescing convergence tests use for the outbox processor).
    private static void DecorateLast<TService>(IServiceCollection services, Func<TService, IServiceProvider, TService> decorate)
        where TService : class
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(TService));
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(TService),
            sp =>
            {
                var inner = (TService)(descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
                return decorate(inner, sp);
            },
            descriptor.Lifetime));
    }

    private static async Task SeedAndStartAsync(ServiceProvider provider)
    {
        var store = provider.GetRequiredService<IWorkflowExecutableStore>();
        var executable = NewCompletingExecutable();
        await store.SaveAsync(executable);
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest());
        await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
    }

    private static async Task<IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)>> SnapshotActivityStateAsync(IDocumentStore store)
    {
        await using var provider = BuildReaderProvider(store);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("wfexec-1");
        return states
            .Select(state => (state.Execution.ExecutableNodeId, state.Status))
            .OrderBy(entry => entry.ExecutableNodeId, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> CommitIdsAsync(IDocumentStore store)
    {
#pragma warning disable GW0004
        var result = await store.QueryAsync(new PortableDocumentQuery(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind));
#pragma warning restore GW0004
        return result.Documents.Select(document => document.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    // A fresh provider over an existing store for read-back only (no burst wiring needed).
    private static ServiceProvider BuildReaderProvider(IDocumentStore store)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddSingleton(store);
        services.AddGroundworkRuntimeStores();
        return services.BuildServiceProvider();
    }

    private static WorkflowExecutionActorActivationRequest NewActivationRequest() =>
        new(
            workflowExecutionId: "wfexec-1",
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: Now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private static WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: Now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    // A Finish intrinsic root runs to a Completed terminal state and needs no CLR activity contract, so both the
    // cache-off and cache-on runs converge deterministically.
    private static WorkflowExecutable NewCompletingExecutable()
    {
        var outcomeType = new ValueTypeDescriptor("String");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "elsa.intrinsic.finish",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = nameof(WorkflowIntrinsicKind.Finish), schemaVersion = "1.0.0" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Outcome] = new(
                    WorkflowIntrinsicInputKeys.Outcome,
                    outcomeType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(outcomeType, JsonSerializer.SerializeToElement("Done"), ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Finish);

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class ExecutableReadCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class CountingWorkflowExecutableStore(IWorkflowExecutableStore inner, ExecutableReadCounter counter) : IWorkflowExecutableStore
    {
        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            counter.Increment();
            return inner.FindAsync(artifactId, cancellationToken);
        }

        public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => inner.SaveAsync(executable, cancellationToken);
        public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) => inner.DeleteAsync(artifactId, cancellationToken);
        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId, string leaseId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now, cancellationToken);
        public ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);
        public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default) => inner.ReleaseRootWriteLeaseAsync(lease, cancellationToken);
        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string artifactId, string operationId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.TryBeginDeletionAsync(artifactId, operationId, expiresAt, now, cancellationToken);
        public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken cancellationToken = default) => inner.CancelDeletionAsync(guard, cancellationToken);
        public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.DeleteAsync(guard, now, cancellationToken);
        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) => inner.ListPageAsync(request, cancellationToken);
    }

    private sealed class ClearingWorkflowExecutableReader(IWorkflowExecutableReader inner, IWorkflowBurstScopeAccessor accessor) : IWorkflowExecutableReader
    {
        public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            var result = await inner.FindAsync(artifactId, cancellationToken);
            accessor.Current?.Clear();
            return result;
        }
    }
}
