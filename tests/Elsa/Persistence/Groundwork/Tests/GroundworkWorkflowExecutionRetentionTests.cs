using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Provider-neutral retained-executable-root contract for workflow execution persistence (spec 092, FR-062..066).
/// These tests intentionally lead the production contract: they remain red until
/// <see cref="IWorkflowExecutionStateStore"/> exposes distinct pinned-artifact lookup and retained-state removal.
/// </summary>
public sealed class GroundworkWorkflowExecutionRetentionTests
{
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListsDistinctPinnedArtifactRootsWithoutDuplicatingSharedArtifacts(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var serializer = new DeserializeCountingSerializer(GroundworkTestSerialization.Serializer);
        IWorkflowExecutionStateStore store = Store(fixture, serializer);

        await store.SaveAsync(State("wf-running", "artifact-shared", WorkflowExecutionStatus.Running));
        await store.SaveAsync(State("wf-completed", "artifact-shared", WorkflowExecutionStatus.Completed));
        await store.SaveAsync(State("wf-faulted", "artifact-other", WorkflowExecutionStatus.Faulted));

        serializer.Reset();
        var roots = await store.ListPinnedExecutableArtifactIdsAsync();

        Assert.Equal(
            ["artifact-other", "artifact-shared"],
            roots.Order(StringComparer.Ordinal));
        Assert.Equal(0, serializer.FullDocumentDeserializeCount);
    }

    [Fact]
    public async Task PinnedArtifactRootsSurviveSqliteRestart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-execution-retention-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = Store(fixture);
                await store.SaveAsync(State("wf-suspended", "artifact-pinned", WorkflowExecutionStatus.Suspended));
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var restarted = Store(fixture);

                Assert.Equal(["artifact-pinned"], await restarted.ListPinnedExecutableArtifactIdsAsync());
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ArtifactRootDisappearsOnlyAfterTheLastRetainedExecutionIsRemoved(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        IWorkflowExecutionStateStore store = Store(fixture);

        await store.SaveAsync(State("wf-shared-1", "artifact-shared", WorkflowExecutionStatus.Completed));
        await store.SaveAsync(State("wf-shared-2", "artifact-shared", WorkflowExecutionStatus.Cancelled));
        await store.SaveAsync(State("wf-other", "artifact-other", WorkflowExecutionStatus.Faulted));

        Assert.True(await store.DeleteAsync("wf-shared-1"));
        Assert.Equal(
            ["artifact-other", "artifact-shared"],
            (await store.ListPinnedExecutableArtifactIdsAsync()).Order(StringComparer.Ordinal));

        Assert.True(await store.DeleteAsync("wf-shared-2"));
        Assert.Equal(["artifact-other"], await store.ListPinnedExecutableArtifactIdsAsync());
        Assert.Null(await store.FindAsync("wf-shared-2"));
        Assert.False(await store.DeleteAsync("wf-shared-2"));
    }

    private static IWorkflowExecutionStateStore Store(
        GroundworkDocumentStoreFixture fixture,
        IGroundworkRuntimeDocumentSerializer? serializer = null) =>
        new GroundworkWorkflowExecutionStateStore(
            fixture.DocumentStore,
            serializer ?? GroundworkTestSerialization.Serializer,
            null,
            fixture.BoundedDocumentStore);

    private static WorkflowExecutionState State(
        string workflowExecutionId,
        string artifactId,
        WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: workflowExecutionId,
            PinnedExecutable: new WorkflowExecutableIdentity(
                ArtifactId: artifactId,
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-1",
                ArtifactVersion: "1",
                ArtifactHash: $"hash-{artifactId}"),
            Status: status,
            SubStatus: null,
            CreatedAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Cancelled or WorkflowExecutionStatus.Faulted
                ? DateTimeOffset.UnixEpoch
                : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    /// <summary>
    /// Proves the retained-root query uses the provider/index partial-read path instead of loading full execution
    /// records. Current-version fragment reads remain allowed; historical records still require normal upcasting.
    /// </summary>
    private sealed class DeserializeCountingSerializer(IGroundworkRuntimeDocumentSerializer inner)
        : IGroundworkRuntimeDocumentSerializer
    {
        public int FullDocumentDeserializeCount { get; private set; }

        public (string SchemaVersion, string ContentJson) Serialize<T>(string documentKind, T document) =>
            inner.Serialize(documentKind, document);

        public string SerializeForComparison<T>(T value) => inner.SerializeForComparison(value);

        public bool IsCurrentVersion(DocumentEnvelope envelope) => inner.IsCurrentVersion(envelope);

        public T DeserializeElement<T>(JsonElement element) => inner.DeserializeElement<T>(element);

        public T Deserialize<T>(DocumentEnvelope envelope)
        {
            FullDocumentDeserializeCount++;
            return inner.Deserialize<T>(envelope);
        }

        public void Reset() => FullDocumentDeserializeCount = 0;
    }
}
