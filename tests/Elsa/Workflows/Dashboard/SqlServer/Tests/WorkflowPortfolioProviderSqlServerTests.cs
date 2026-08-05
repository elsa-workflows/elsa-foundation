using System.Text.Json;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Dashboard.SqlServer.Tests;

/// <summary>
/// SQL Server counterpart of
/// <c>Elsa.Workflows.Dashboard.Tests.WorkflowPortfolioProviderTests.GroundworkSqliteReturnsTheCompleteIdenticalPortfolioFixture</c>.
/// Lives in its own leaf project (see the project file comment) so the real-container run stays out of the
/// fast PR gate while the SQLite coverage in the main Dashboard test project keeps running there.
/// </summary>
[Collection(SqlServerDashboardContainerCollection.Name)]
public sealed class WorkflowPortfolioProviderSqlServerTests(SqlServerDashboardContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakePayloadSerializer PayloadSerializer = new();

    [SkippableFact]
    public async Task GroundworkSqlServerReturnsTheCompleteIdenticalPortfolioFixture()
    {
        var driver = await fixture.RequireCleanDriverAsync();

        await using var client = await driver.OpenClientAsync(
            new GroundworkAllFeaturesDeploymentSchema().CreateManifest(),
            DocumentStoreAccess.Scoped(new StorageScope("tenant-b")));
        var store = client.DocumentStore;

        foreach (var definition in Enumerable.Range(0, 105).Select(Definition))
            await SaveDefinitionAsync(store, definition);
        foreach (var draft in Enumerable.Range(0, 30).Select(Draft))
            await SaveDraftAsync(store, draft);
        var serializer = new GroundworkRuntimeDocumentSerializer();
        var referenceStore = new GroundworkWorkflowExecutableSourceReferenceStore(store, serializer);
        for (var index = 0; index < 50; index++)
            await referenceStore.SaveAsync(Reference(index));

        var source = new GroundworkWorkflowPortfolioDataSource(
            () => driver.CreateRawConnection(),
            GroundworkRunHealthDialect.SqlServer,
            PayloadSerializer);

        await AssertFixtureAsync(source);
    }

    private static async Task AssertFixtureAsync(IWorkflowPortfolioDataSource source)
    {
        var counts = await source.QueryBaseCountsAsync("tenant-b", Now);
        var drafts = new List<WorkflowDefinitionDraft>();
        await foreach (var draft in source.StreamCurrentDraftsAsync("tenant-b"))
            drafts.Add(draft);
        Assert.Equal(new WorkflowPortfolioBaseCounts(105, 50, 30), counts);
        Assert.Equal(30, drafts.Count);
        Assert.All(drafts, draft => Assert.NotNull(draft.State));
    }

    private static WorkflowDefinition Definition(int index) => new()
    {
        Id = $"definition-{index}", TenantId = "tenant-b", Name = $"Definition {index}",
        CreatedAt = Now, LastModifiedAt = Now
    };

    private static WorkflowDefinitionDraft Draft(int index) => new()
    {
        Id = $"draft-{index}", WorkflowDefinitionId = $"definition-{index}", TenantId = "tenant-b",
        State = WorkflowDefinitionState.Empty,
        StateSource = PayloadSerializer.Serialize(WorkflowDefinitionState.Empty),
        CreatedAt = Now, LastModifiedAt = Now
    };

    private static WorkflowExecutableSourceReference Reference(int index) => new(
        $"reference-{index}", $"artifact-{index}", "WorkflowDefinitionVersion", $"version-{index}", "1",
        $"definition-{index}", $"version-{index}", "1", Now, Now, WorkflowExecutableReferenceScope.Published);

    private static async Task SaveDefinitionAsync(IDocumentStore store, WorkflowDefinition definition) => await store.SaveAsync(new(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(new DefinitionDocument(WorkflowsDesignStorageManifest.WorkflowDefinitionCollection, definition), GroundworkDesignJson.Options)));

    private static async Task SaveDraftAsync(IDocumentStore store, WorkflowDefinitionDraft draft) => await store.SaveAsync(new(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draft.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(new DraftDocument(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection, draft, []),
                GroundworkDesignDocumentSerialization.Create(PayloadSerializer))));

    private sealed record DefinitionDocument(string Collection, WorkflowDefinition Entity);
    private sealed record DraftDocument(string Collection, WorkflowDefinitionDraft Entity, IReadOnlyCollection<DesignMetadataRecord> Layout);

    private sealed class FakePayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;
        public JsonSerializerOptions GetOptions() => Options;
    }
}
