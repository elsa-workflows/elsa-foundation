using System.Text.Json;
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
    [Fact]
    public async Task ExecuteAsync_RenewsLeaseUntilWriteCompletes()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable("artifact-1"));
        var manager = new WorkflowExecutableRootWriteLeaseManager(
            store,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions
            {
                // Keep enough scheduling headroom for the full parallel solution suite while still
                // waiting beyond the original lease to prove that renewal extended it.
                RootWriteLeaseDuration = TimeSpan.FromSeconds(2)
            }),
            TimeProvider.System);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writeTask = manager.ExecuteAsync("artifact-1", "writer-1", async cancellationToken =>
        {
            writeStarted.SetResult();
            await finishWrite.Task.WaitAsync(cancellationToken);
        }).AsTask();

        await writeStarted.Task;
        await Task.Delay(TimeSpan.FromMilliseconds(2500));

        var guardWhileWriting = await store.TryBeginDeletionAsync(
            "artifact-1",
            "gc-1",
            DateTimeOffset.UtcNow.AddMinutes(1),
            DateTimeOffset.UtcNow);

        Assert.Null(guardWhileWriting);

        finishWrite.SetResult();
        await writeTask;

        var now = DateTimeOffset.UtcNow;
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
        var now = DateTimeOffset.UtcNow;
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
        var now = DateTimeOffset.UtcNow;
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

    private static WorkflowExecutableRootWriteLeaseManager NewManager(IWorkflowExecutableStore store) =>
        new(
            store,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            TimeProvider.System);

    private static WorkflowExecutable Executable(string artifactId, params WorkflowExecutable[] dependencies) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "activity-root",
                activityType: "Test.Root",
                activityTypeVersion: "1.0.0",
                descriptorType: "Test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: dependencies.Select(dependency => new WorkflowExecutableDependency(
                dependency.Identity.ArtifactId,
                dependency.Identity.ArtifactHash,
                ["node-root"])).ToArray());

    private static string Hash(string artifactId) => $"sha256:{artifactId}";

    private sealed class RecordingExecutableStore(IWorkflowExecutableStore inner) : IWorkflowExecutableStore
    {
        public List<string> AcquiredArtifactIds { get; } = [];

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

        public ValueTask<bool> RenewRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);

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

        public ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);
    }
}
