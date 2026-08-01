using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableRootWriteLeaseManagerTests
{
    // The default lease duration; the manager renews at a third of it.
    private static readonly TimeSpan LeaseDuration = new WorkflowExecutableGarbageCollectionOptions().RootWriteLeaseDuration;
    private static readonly TimeSpan RenewalCadence = LeaseDuration / 3;

    // Guards the handshakes with the background renewal loop so a regression fails fast instead of hanging CI.
    private static readonly TimeSpan AwaitTimeout = TimeSpan.FromSeconds(5);

    private readonly ManualTimerTimeProvider _timeProvider = new(new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ExecuteAsync_RenewsLeaseUntilWriteCompletes()
    {
        var store = new RecordingExecutableStore(new InMemoryWorkflowExecutableStore());
        await store.SaveAsync(Executable("artifact-1"));
        var manager = NewManager(store);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writeTask = manager.ExecuteAsync("artifact-1", "writer-1", async cancellationToken =>
        {
            writeStarted.SetResult();
            await finishWrite.Task.WaitAsync(cancellationToken);
        }).AsTask();

        await writeStarted.Task.WaitAsync(AwaitTimeout);
        await _timeProvider.TimerCreated.WaitAsync(AwaitTimeout);

        // Renew once at the cadence, pushing expiry out to cadence + duration.
        _timeProvider.AdvanceAndFire(RenewalCadence);
        await store.FirstRenewalObserved.WaitAsync(AwaitTimeout);

        // Land between the original expiry (duration) and the renewed one (cadence + duration) without firing the next
        // renewal, so the lease can only still be held because renewal extended it.
        _timeProvider.Advance(LeaseDuration - RenewalCadence / 2);

        var now = _timeProvider.GetUtcNow();
        var guardWhileWriting = await store.TryBeginDeletionAsync("artifact-1", "gc-1", now.AddMinutes(1), now);

        Assert.Null(guardWhileWriting);

        finishWrite.SetResult();
        await writeTask.WaitAsync(AwaitTimeout);

        var guardAfterWrite = await store.TryBeginDeletionAsync("artifact-1", "gc-1", now.AddMinutes(1), now);
        Assert.NotNull(guardAfterWrite);
    }

    [Fact]
    public async Task ExecuteAsync_LeasesTheDistinctDependencyClosureInOrdinalArtifactOrder()
    {
        var inner = new InMemoryWorkflowExecutableStore();
        var child = Executable("artifact-a");
        var root = Executable("artifact-z", child);
        await inner.SaveAsync(child);
        await inner.SaveAsync(root);
        var store = new RecordingExecutableStore(inner);
        var now = _timeProvider.GetUtcNow();
        var manager = NewManager(store);

        await manager.ExecuteAsync(root.Identity, "writer", async _ =>
        {
            Assert.Null(await store.TryBeginDeletionAsync("artifact-a", "gc-a", now.AddMinutes(1), now));
            Assert.Null(await store.TryBeginDeletionAsync("artifact-z", "gc-z", now.AddMinutes(1), now));
        });

        Assert.Equal(["artifact-a", "artifact-z"], store.AcquiredArtifactIds);
        Assert.NotNull(await store.TryBeginDeletionAsync("artifact-a", "gc-a", now.AddMinutes(1), now));
        Assert.NotNull(await store.TryBeginDeletionAsync("artifact-z", "gc-z", now.AddMinutes(1), now));
    }

    [Fact]
    public async Task ExecuteAsync_ReleasesAlreadyAcquiredClosureLeasesWhenALaterArtifactCannotBeLeased()
    {
        var inner = new InMemoryWorkflowExecutableStore();
        var child = Executable("artifact-a");
        var root = Executable("artifact-z", child);
        await inner.SaveAsync(child);
        await inner.SaveAsync(root);
        var now = _timeProvider.GetUtcNow();
        var blockingGuard = await inner.TryBeginDeletionAsync("artifact-z", "gc-z", now.AddMinutes(1), now);
        var store = new RecordingExecutableStore(inner);
        var manager = NewManager(store);
        var wrote = false;

        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            manager.ExecuteAsync(root.Identity, "writer", _ =>
            {
                wrote = true;
                return ValueTask.CompletedTask;
            }).AsTask());

        Assert.NotNull(blockingGuard);
        Assert.False(wrote);
        Assert.Equal(["artifact-a", "artifact-z"], store.AcquiredArtifactIds);
        Assert.NotNull(await inner.TryBeginDeletionAsync("artifact-a", "gc-a", now.AddMinutes(1), now));
    }

    private WorkflowExecutableRootWriteLeaseManager NewManager(IWorkflowExecutableStore store) =>
        new(
            store,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            _timeProvider);

    private WorkflowExecutable Executable(string artifactId, params WorkflowExecutable[] dependencies) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "activity-root",
                activityType: "Test.Root",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor(
                    "Test",
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { })),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _timeProvider.GetUtcNow(),
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: dependencies.Select(dependency => new WorkflowExecutableDependency(
                dependency.Identity.ArtifactId,
                dependency.Identity.ArtifactHash,
                ["node-root"])).ToArray(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static string Hash(string artifactId) => $"sha256:{artifactId}";

    private sealed class RecordingExecutableStore(IWorkflowExecutableStore inner) : IWorkflowExecutableStore
    {
        private readonly TaskCompletionSource _firstRenewalObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> AcquiredArtifactIds { get; } = [];

        /// <summary>Completes once a renewal has been applied, so the test can safely move the clock again.</summary>
        public Task FirstRenewalObserved => _firstRenewalObserved.Task;

        public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(executable, cancellationToken);

        public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(artifactId, cancellationToken);

        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
            string artifactId,
            string leaseId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            AcquiredArtifactIds.Add(artifactId);
            return inner.TryAcquireRootWriteLeaseAsync(artifactId, leaseId, expiresAt, now, cancellationToken);
        }

        public async ValueTask<bool> RenewRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var renewed = await inner.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);
            _firstRenewalObserved.TrySetResult();
            return renewed;
        }

        public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default) =>
            inner.ReleaseRootWriteLeaseAsync(lease, cancellationToken);

        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
            string artifactId,
            string operationId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.TryBeginDeletionAsync(artifactId, operationId, expiresAt, now, cancellationToken);

        public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken cancellationToken = default) =>
            inner.CancelDeletionAsync(guard, cancellationToken);

        public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(guard, now, cancellationToken);

        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(artifactId, cancellationToken);

        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
            RuntimeStorePageRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ListPageAsync(request, cancellationToken);
    }
}
