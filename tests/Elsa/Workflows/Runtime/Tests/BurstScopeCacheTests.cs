using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Contract coverage for the burst-scoped reconstructible cache (ADR 0031 item b, spec 111) and its first consumer,
// the burst-cached executable reader. These are the unit-level guarantees; the end-to-end guardrail (byte-identical
// committed durable state cache-on vs cache-off) and the read-count collapse live in the Groundwork test project.
public sealed class BurstScopeCacheTests
{
    // ---- WorkflowBurstScope contract (FR-001) ----------------------------------------------------------------

    [Fact]
    public async Task GetOrAddAsync_MemoizesFactory_WithinScope()
    {
        await using var scope = new WorkflowBurstScope("wfexec-1");
        var factoryCalls = 0;

        var first = await scope.GetOrAddAsync("k", _ => { factoryCalls++; return new ValueTask<string>("value"); });
        var second = await scope.GetOrAddAsync("k", _ => { factoryCalls++; return new ValueTask<string>("value"); });

        Assert.Equal("value", first);
        Assert.Equal("value", second);
        Assert.Equal(1, factoryCalls);   // single-flight within the scope
        Assert.Equal(2, scope.ReadCount);
        Assert.Equal(1, scope.MissCount); // exactly one durable read the cache did NOT save
    }

    [Fact]
    public async Task GetOrAddAsync_CachesNullResult_AsAValidEntry()
    {
        await using var scope = new WorkflowBurstScope("wfexec-1");
        var factoryCalls = 0;

        var first = await scope.GetOrAddAsync<string?>("k", _ => { factoryCalls++; return new ValueTask<string?>((string?)null); });
        var second = await scope.GetOrAddAsync<string?>("k", _ => { factoryCalls++; return new ValueTask<string?>((string?)null); });

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, factoryCalls); // a cached "not found" is a hit, not a re-read
    }

    // ---- Reconstructibility: a lost cache re-reads durably (FR-005 / US3) -------------------------------------

    [Fact]
    public async Task Clear_EvictsEntries_SoNextReadReconstructs()
    {
        await using var scope = new WorkflowBurstScope("wfexec-1");
        var factoryCalls = 0;
        ValueTask<string> Factory(CancellationToken _) { factoryCalls++; return new ValueTask<string>($"value-{factoryCalls}"); }

        var before = await scope.GetOrAddAsync("k", Factory);
        scope.Clear();
        var after = await scope.GetOrAddAsync("k", Factory);

        Assert.Equal("value-1", before);
        Assert.Equal("value-2", after); // reconstructed from the factory (durable path) after eviction
        Assert.Equal(2, factoryCalls);
    }

    // ---- Deterministic disposal at burst end (FR-005 / US4) ---------------------------------------------------

    [Fact]
    public async Task DisposeAsync_DisposesDisposableEntries_AndBlocksReuse()
    {
        var tracker = new TrackingDisposable();
        var scope = new WorkflowBurstScope("wfexec-1");
        await scope.GetOrAddAsync("k", _ => new ValueTask<TrackingDisposable>(tracker));

        await scope.DisposeAsync();

        Assert.True(tracker.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await scope.GetOrAddAsync("k", _ => new ValueTask<string>("x")));
    }

    // ---- Eviction: independent scopes never share entries (two sequential drains ⇒ separate scopes) -----------

    [Fact]
    public async Task SeparateScopes_DoNotShareEntries()
    {
        await using var first = new WorkflowBurstScope("wfexec-1");
        await using var second = new WorkflowBurstScope("wfexec-1");
        var firstCalls = 0;
        var secondCalls = 0;

        await first.GetOrAddAsync("k", _ => { firstCalls++; return new ValueTask<string>("a"); });
        await second.GetOrAddAsync("k", _ => { secondCalls++; return new ValueTask<string>("a"); });

        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls); // the second "drain" re-reads durably; nothing leaked from the first
    }

#if DEBUG
    // ---- Debug-only misuse guard: must-survive values belong in durable storage, not the cache (FR-007) ------

    [Fact]
    public async Task GetOrAddAsync_RejectsMustSurviveValue_InDebug()
    {
        await using var scope = new WorkflowBurstScope("wfexec-1");
        var durable = new DurableValueExternalReference("profile", "locator", new Dictionary<string, string>());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scope.GetOrAddAsync<object>("k", _ => new ValueTask<object>(durable)));
    }
#endif

    // ---- Accessor push/pop (FR-002) ---------------------------------------------------------------------------

    [Fact]
    public void Accessor_PushPop_RestoresPriorScope()
    {
        var accessor = new AsyncLocalWorkflowBurstScopeAccessor();
        Assert.Null(accessor.Current);

        var outer = new WorkflowBurstScope("wfexec-1");
        using (accessor.Push(outer))
        {
            Assert.Same(outer, accessor.Current);
            var inner = new WorkflowBurstScope("wfexec-1");
            using (accessor.Push(inner))
                Assert.Same(inner, accessor.Current);
            Assert.Same(outer, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    // ---- BurstCachedWorkflowExecutableReader (FR-003) --------------------------------------------------------

    [Fact]
    public async Task Reader_NoBurst_PassesThroughToStore_EveryRead()
    {
        var store = new CountingExecutableStore(NewExecutable("artifact-1"));
        var reader = new BurstCachedWorkflowExecutableReader(store, new AsyncLocalWorkflowBurstScopeAccessor());

        await reader.FindAsync("artifact-1");
        await reader.FindAsync("artifact-1");

        Assert.Equal(2, store.FindCount); // no scope ⇒ no memoization ⇒ durable every time
    }

    [Fact]
    public async Task Reader_WithBurst_CachesByArtifactId_OneDurableRead()
    {
        var executable = NewExecutable("artifact-1");
        var store = new CountingExecutableStore(executable);
        var accessor = new AsyncLocalWorkflowBurstScopeAccessor();
        var reader = new BurstCachedWorkflowExecutableReader(store, accessor);

        await using var scope = new WorkflowBurstScope("wfexec-1");
        using (accessor.Push(scope))
        {
            var first = await reader.FindAsync("artifact-1");
            var second = await reader.FindAsync("artifact-1");
            Assert.Same(executable, first);
            Assert.Same(executable, second); // same shared instance across hops
        }

        Assert.Equal(1, store.FindCount); // 46-reads-per-hop collapse: one durable read per artifact per burst
    }

    [Fact]
    public async Task Reader_WithBurst_DistinctArtifacts_ReadEachOnce()
    {
        var store = new CountingExecutableStore(NewExecutable("artifact-1"), NewExecutable("artifact-2"));
        var accessor = new AsyncLocalWorkflowBurstScopeAccessor();
        var reader = new BurstCachedWorkflowExecutableReader(store, accessor);

        await using var scope = new WorkflowBurstScope("wfexec-1");
        using (accessor.Push(scope))
        {
            await reader.FindAsync("artifact-1");
            await reader.FindAsync("artifact-2");
            await reader.FindAsync("artifact-1");
            await reader.FindAsync("artifact-2");
        }

        Assert.Equal(2, store.FindCount); // keyed by artifact id ⇒ one read per distinct artifact
    }

    private static WorkflowExecutable NewExecutable(string artifactId)
    {
        using var descriptorPayload = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, descriptorPayload.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    // A minimal executable store that counts FindAsync calls (the durable reads the burst cache exists to save) and
    // serves a fixed set of artifacts by id. All non-read members are unused by these tests.
    private sealed class CountingExecutableStore(params WorkflowExecutable[] executables) : IWorkflowExecutableStore
    {
        private readonly Dictionary<string, WorkflowExecutable> _byId =
            executables.ToDictionary(executable => executable.Identity.ArtifactId, StringComparer.Ordinal);

        public int FindCount { get; private set; }

        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            FindCount++;
            return new ValueTask<WorkflowExecutable?>(_byId.GetValueOrDefault(artifactId));
        }

        public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId, string leaseId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string artifactId, string operationId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
