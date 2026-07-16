using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Concurrency contract for spec 092's artifact staging grace and provider-backed deletion guard.
/// </summary>
public sealed class WorkflowExecutableReferenceGarbageCollectorConcurrencyTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_DoesNotDeleteAnUnreferencedArtifactStillInsideItsCreationStagingGrace()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        await executableStore.SaveAsync(Executable("artifact-staging", _now));

        var result = await NewCollector(executableStore, sourceReferenceStore).SweepAsync();

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await executableStore.FindAsync("artifact-staging"));
    }

    [Fact]
    public async Task Sweep_RechecksRootsAfterCandidateSelectionSoAConcurrentNewRootWinsOverDeletion()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new HookedSourceReferenceStore();
        await executableStore.SaveAsync(Executable("artifact-racing", _now.AddDays(-30)));
        sourceReferenceStore.AfterQuery = queryNumber =>
            queryNumber == 1
                ? sourceReferenceStore.SaveAsync(Reference("ref-concurrent", "artifact-racing"))
                : ValueTask.CompletedTask;

        var result = await NewCollector(executableStore, sourceReferenceStore).SweepAsync();

        Assert.NotNull(await sourceReferenceStore.FindAsync("ref-concurrent"));
        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await executableStore.FindAsync("artifact-racing"));
    }

    [Fact]
    public async Task Sweep_FinalRecheckProtectsAChildWhenAConcurrentParentRootAppears()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new HookedSourceReferenceStore();
        var child = Executable("artifact-child", _now.AddDays(-30));
        var parent = Executable("artifact-parent", _now.AddDays(-30), child);
        await executableStore.SaveAsync(child);
        await executableStore.SaveAsync(parent);
        sourceReferenceStore.AfterQuery = queryNumber =>
            queryNumber == 2
                ? sourceReferenceStore.SaveAsync(Reference("ref-concurrent-parent", parent.Identity.ArtifactId))
                : ValueTask.CompletedTask;

        var result = await NewCollector(executableStore, sourceReferenceStore).SweepAsync();

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await executableStore.FindAsync(child.Identity.ArtifactId));
        Assert.NotNull(await executableStore.FindAsync(parent.Identity.ArtifactId));
    }

    [Fact]
    public async Task Sweep_RootWriteLeaseAcquiredBeforeDeletionGuardWinsAndRetainsArtifact()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new HookedSourceReferenceStore();
        await executableStore.SaveAsync(Executable("artifact-root-wins", _now.AddDays(-30)));
        WorkflowExecutableRootWriteLease? winningLease = null;
        sourceReferenceStore.AfterQuery = async queryNumber =>
        {
            if (queryNumber == 1)
                winningLease = await executableStore.TryAcquireRootWriteLeaseAsync(
                    "artifact-root-wins",
                    "root-writer",
                    _now.AddMinutes(1),
                    _now);
        };

        var result = await NewCollector(executableStore, sourceReferenceStore).SweepAsync();

        Assert.NotNull(winningLease);
        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await executableStore.FindAsync("artifact-root-wins"));
    }

    [Fact]
    public async Task Sweep_DeletionGuardBlocksRootWriterInFinalQueryDeleteGapAndDeletesArtifact()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new HookedSourceReferenceStore();
        await executableStore.SaveAsync(Executable("artifact-gc-wins", _now.AddDays(-30)));
        WorkflowExecutableRootWriteLease? losingLease = null;
        sourceReferenceStore.AfterQuery = async queryNumber =>
        {
            if (queryNumber == 2)
                losingLease = await executableStore.TryAcquireRootWriteLeaseAsync(
                    "artifact-gc-wins",
                    "late-root-writer",
                    _now.AddMinutes(1),
                    _now);
        };

        var result = await NewCollector(executableStore, sourceReferenceStore).SweepAsync();

        Assert.Null(losingLease);
        Assert.Equal(1, result.DeletedArtifactCount);
        Assert.Null(await executableStore.FindAsync("artifact-gc-wins"));
    }

    [Fact]
    public async Task Sweep_FinalRootQueryFailureCancelsGuardAndRetainsArtifact()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new HookedSourceReferenceStore();
        await executableStore.SaveAsync(Executable("artifact-query-failure", _now.AddDays(-30)));
        sourceReferenceStore.AfterQuery = queryNumber =>
            queryNumber == 2
                ? ValueTask.FromException(new InvalidOperationException("Root query unavailable."))
                : ValueTask.CompletedTask;

        var result = await NewCollector(executableStore, sourceReferenceStore).SweepAsync();
        var leaseAfterFailure = await executableStore.TryAcquireRootWriteLeaseAsync(
            "artifact-query-failure",
            "writer-after-failure",
            _now.AddMinutes(1),
            _now);

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await executableStore.FindAsync("artifact-query-failure"));
        Assert.NotNull(leaseAfterFailure);
    }

    [Fact]
    public async Task Sweep_DoesNotDeleteWithAGuardThatExpiredDuringTheFinalRootQuery()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new HookedSourceReferenceStore();
        var timeProvider = new MutableTimeProvider(_now);
        await executableStore.SaveAsync(Executable("artifact-slow-query", _now.AddDays(-30)));
        sourceReferenceStore.AfterQuery = queryNumber =>
        {
            if (queryNumber == 2)
                timeProvider.Advance(TimeSpan.FromMinutes(2));
            return ValueTask.CompletedTask;
        };

        var result = await NewCollector(executableStore, sourceReferenceStore, timeProvider).SweepAsync(_now);
        var recoveredLease = await executableStore.TryAcquireRootWriteLeaseAsync(
            "artifact-slow-query",
            "writer-after-expiry",
            timeProvider.GetUtcNow().AddMinutes(1),
            timeProvider.GetUtcNow());

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await executableStore.FindAsync("artifact-slow-query"));
        Assert.NotNull(recoveredLease);
    }

    [Fact]
    public async Task Store_DoesNotIssueGuardsOrLeasesForMissingArtifacts()
    {
        var store = new InMemoryWorkflowExecutableStore();

        var lease = await store.TryAcquireRootWriteLeaseAsync("missing", "lease", _now.AddMinutes(1), _now);
        var guard = await store.TryBeginDeletionAsync("missing", "delete", _now.AddMinutes(1), _now);

        Assert.Null(lease);
        Assert.Null(guard);
    }

    [Fact]
    public async Task Store_RecoversExpiredRootWriteLeaseAndDeletionGuard()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable("artifact-recovery", _now.AddDays(-30)));
        var expiredLease = await store.TryAcquireRootWriteLeaseAsync(
            "artifact-recovery",
            "expired-lease",
            _now.AddSeconds(1),
            _now);

        var guardAfterLeaseExpiry = await store.TryBeginDeletionAsync(
            "artifact-recovery",
            "expiring-guard",
            _now.AddSeconds(2),
            _now.AddSeconds(1));
        var leaseAfterGuardExpiry = await store.TryAcquireRootWriteLeaseAsync(
            "artifact-recovery",
            "recovered-lease",
            _now.AddSeconds(4),
            _now.AddSeconds(2));

        Assert.NotNull(expiredLease);
        Assert.NotNull(guardAfterLeaseExpiry);
        Assert.NotNull(leaseAfterGuardExpiry);
    }

    [Fact]
    public async Task Store_GuardedDeleteRejectsStaleTokenAndRequiresCurrentGuard()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable("artifact-guarded", _now.AddDays(-30)));
        var guard = Assert.IsType<WorkflowExecutableDeletionGuard>(
            await store.TryBeginDeletionAsync("artifact-guarded", "delete", _now.AddMinutes(1), _now));
        var staleGuard = guard with { ConcurrencyToken = "stale" };

        Assert.False(await store.DeleteAsync(staleGuard, _now));
        Assert.NotNull(await store.FindAsync("artifact-guarded"));
        Assert.True(await store.DeleteAsync(guard, _now));
        Assert.Null(await store.FindAsync("artifact-guarded"));
    }

    [Fact]
    public async Task Store_RenewAndReleaseRequireTheStableLeaseToken()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable("artifact-lease", _now.AddDays(-30)));
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(
            await store.TryAcquireRootWriteLeaseAsync("artifact-lease", "writer", _now.AddMinutes(1), _now));
        var sameLease = await store.TryAcquireRootWriteLeaseAsync("artifact-lease", "writer", _now.AddMinutes(2), _now);
        var staleLease = lease with { ConcurrencyToken = "stale" };

        Assert.Equal(lease, sameLease);
        Assert.False(await store.RenewRootWriteLeaseAsync(staleLease, _now.AddMinutes(2), _now));
        await store.ReleaseRootWriteLeaseAsync(staleLease);
        Assert.Null(await store.TryBeginDeletionAsync("artifact-lease", "blocked", _now.AddMinutes(1), _now));

        Assert.True(await store.RenewRootWriteLeaseAsync(lease, _now.AddMinutes(2), _now));
        await store.ReleaseRootWriteLeaseAsync(lease);
        Assert.NotNull(await store.TryBeginDeletionAsync("artifact-lease", "unblocked", _now.AddMinutes(1), _now));
    }

    private WorkflowExecutableReferenceGarbageCollector NewCollector(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        TimeProvider? timeProvider = null) =>
        new(
            executableStore,
            sourceReferenceStore,
            new InMemoryWorkflowExecutionStateStore(),
            timeProvider ?? new FixedTimeProvider(_now),
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

    private WorkflowExecutable Executable(string artifactId, DateTimeOffset createdAt, params WorkflowExecutable[] dependencies) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "authored-root",
                activityType: "test/activity",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor(
                    "test",
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { type = "test" })),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: createdAt,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: dependencies.Select(dependency => new WorkflowExecutableDependency(
                dependency.Identity.ArtifactId,
                dependency.Identity.ArtifactHash,
                ["node-root"])).ToArray());

    private static string Hash(string artifactId) => $"sha256:{artifactId}";

    private WorkflowExecutableSourceReference Reference(string sourceReferenceId, string artifactId) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: artifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: "version-1",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1.0.0",
            CreatedAt: _now,
            PublishedAt: _now,
            Scope: WorkflowExecutableReferenceScope.Published);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now += duration;
    }

    /// <summary>
    /// Opens the race deterministically after GC has computed its unreferenced candidate set but before
    /// it attempts physical deletion. A correct collector/store contract performs a final guarded root
    /// check and observes the newly created live reference.
    /// </summary>
    private sealed class HookedSourceReferenceStore : IWorkflowExecutableSourceReferenceStore
    {
        private readonly InMemoryWorkflowExecutableSourceReferenceStore _inner = new();
        private int _queryCount;

        public Func<int, ValueTask>? AfterQuery { get; set; }

        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(reference, cancellationToken);

        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(sourceReferenceId, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListByArtifactAsync(
            string artifactId,
            CancellationToken cancellationToken = default) =>
            _inner.ListByArtifactAsync(artifactId, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAsync(
            WorkflowExecutableReferenceScope? scope = null,
            bool liveOnly = false,
            DateTimeOffset? now = null,
            CancellationToken cancellationToken = default) =>
            _inner.ListAsync(scope, liveOnly, now, cancellationToken);

        public ValueTask<bool> RetireAsync(
            string sourceReferenceId,
            DateTimeOffset deletedAt,
            string? reason = null,
            CancellationToken cancellationToken = default) =>
            _inner.RetireAsync(sourceReferenceId, deletedAt, reason, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            _inner.DeleteExpiredOrRetiredAsync(now, cancellationToken);

        public async ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
            IEnumerable<string> artifactIds,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var candidates = await _inner.ListUnreferencedArtifactIdsAsync(artifactIds, now, cancellationToken);
            var queryNumber = Interlocked.Increment(ref _queryCount);
            if (AfterQuery is not null)
                await AfterQuery(queryNumber);
            return candidates;
        }
    }
}
