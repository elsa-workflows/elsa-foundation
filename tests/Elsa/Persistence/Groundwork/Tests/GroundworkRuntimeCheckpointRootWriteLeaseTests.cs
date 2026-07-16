using System.Text.Json;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimeCheckpointRootWriteLeaseTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckpointDoesNotWriteExecutionRootWhileDeletionGuardOwnsArtifact()
    {
        IDocumentStore documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var workflowStateStore = new GroundworkWorkflowExecutionStateStore(
            documentStore,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor);
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(Executable());
        var deletionGuard = await executableStore.TryBeginDeletionAsync("artifact-1", "gc-test", _now.AddMinutes(1), _now);
        var rootWriteLeaseManager = new WorkflowExecutableRootWriteLeaseManager(
            executableStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            new FixedTimeProvider(_now));
        var writer = new GroundworkRuntimeCheckpointWriter(
            documentStore,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            workflowStateStore,
            new GroundworkSchedulerStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkActivityExecutionStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkBookmarkStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkDurableValueStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkIncidentStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkExecutionLivenessStateStore(documentStore, GroundworkTestSerialization.Serializer),
            rootWriteLeaseManager);

        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            writer.CommitAsync(Commit(), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

        Assert.NotNull(deletionGuard);
        Assert.Null(await workflowStateStore.FindAsync("execution-1"));
        Assert.Null(await documentStore.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-1"));
    }

    [Fact]
    public async Task ClosureLeaseFencesEveryGroundworkArtifactAndReleasesAfterTheRootWrite()
    {
        IDocumentStore documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        IWorkflowExecutableStore executableStore = new GroundworkWorkflowExecutableStore(
            documentStore,
            GroundworkTestSerialization.Serializer);
        var child = Executable("artifact-child");
        var parent = Executable("artifact-parent", child);
        await executableStore.SaveAsync(child);
        await executableStore.SaveAsync(parent);
        var manager = new WorkflowExecutableRootWriteLeaseManager(
            executableStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()),
            new FixedTimeProvider(_now));

        await manager.ExecuteAsync(parent.Identity, "checkpoint:closure", async _ =>
        {
            Assert.Null(await executableStore.TryBeginDeletionAsync(
                child.Identity.ArtifactId,
                "gc-child",
                _now.AddMinutes(1),
                _now));
            Assert.Null(await executableStore.TryBeginDeletionAsync(
                parent.Identity.ArtifactId,
                "gc-parent",
                _now.AddMinutes(1),
                _now));
        });

        Assert.NotNull(await executableStore.TryBeginDeletionAsync(
            child.Identity.ArtifactId,
            "gc-child",
            _now.AddMinutes(1),
            _now));
        Assert.NotNull(await executableStore.TryBeginDeletionAsync(
            parent.Identity.ArtifactId,
            "gc-parent",
            _now.AddMinutes(1),
            _now));
    }

    private RuntimeCheckpointCommit Commit()
    {
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: "execution-1",
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", Hash("artifact-1")),
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());
        var checkpoint = new RuntimeCheckpoint(
            CheckpointId: "checkpoint-1",
            Name: "WorkflowStarted",
            WorkflowExecutionId: state.WorkflowExecutionId,
            OccurredAt: _now,
            ActivityExecutionIds: [],
            Metadata: new Dictionary<string, string>());

        return new RuntimeCheckpointCommit(
            CommitId: "commit-1",
            Checkpoint: checkpoint,
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: new RuntimeStateChange<WorkflowExecutionState>(
                    state.WorkflowExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    new Dictionary<string, string>()),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());
    }

    private WorkflowExecutable Executable() => Executable("artifact-1");

    private WorkflowExecutable Executable(string artifactId, params WorkflowExecutable[] dependencies) =>
        new(
            new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "activity-root",
                activityType: "Test.Root",
                activityTypeVersion: "1.0.0",
                descriptorType: "Test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            _now,
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies: dependencies.Select(dependency => new WorkflowExecutableDependency(
                dependency.Identity.ArtifactId,
                dependency.Identity.ArtifactHash,
                ["node-root"])).ToArray());

    private static string Hash(string artifactId) => $"sha256:{artifactId}";

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
