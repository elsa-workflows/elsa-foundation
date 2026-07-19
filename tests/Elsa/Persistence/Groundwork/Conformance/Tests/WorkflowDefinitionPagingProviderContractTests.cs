using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class WorkflowDefinitionPagingProviderContractTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new()
    {
        "sqlite",
        "sqlserver",
        "postgresql",
        "mongodb"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Legacy_definition_search_pages_by_id_and_resumes_after_reopen_on_every_provider(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]);

        string continuation;
        await using (var client = await driver.OpenPhysicalClientAsync())
        {
            foreach (var id in new[] { "legacy-d", "legacy-b", "legacy-a", "legacy-c" })
                await SaveLegacyAsync(client.DocumentStore, Definition(id));
            await SaveLegacyAsync(client.DocumentStore, Definition("aaa-unrelated"));
            await SaveLegacyAsync(client.DocumentStore, Definition("deleted-legacy", deleted: true));

            var bounded = new RecordingBoundedDocumentStore(
                client.BoundedDocumentStore
                ?? throw new InvalidOperationException("The provider did not expose its admitted bounded-query runtime."));
            var store = new GroundworkWorkflowDefinitionStore(
                client.DocumentStore,
                bounded);
            var first = await store.QueryPageAsync(new WorkflowDefinitionPageQuery(2, SearchTerm: "legacy"));

            Assert.Equal(["legacy-a", "legacy-b"], first.Items.Select(item => item.Id));
            continuation = Assert.IsType<string>(first.NextContinuationToken);
            var observation = Assert.Single(bounded.Observations);
            Assert.Equal(WorkflowsDesignStorageManifest.SearchWorkflowDefinitionsQuery, observation.Query.QueryIdentity);
            Assert.Equal(2, observation.Query.Take);
            Assert.Equal(2, observation.MaterializedDocuments);
        }

        await using (var reopened = await driver.OpenPhysicalClientAsync())
        {
            var bounded = new RecordingBoundedDocumentStore(
                reopened.BoundedDocumentStore
                ?? throw new InvalidOperationException("The provider did not expose its admitted bounded-query runtime."));
            var store = new GroundworkWorkflowDefinitionStore(
                reopened.DocumentStore,
                bounded);
            var second = await store.QueryPageAsync(
                new WorkflowDefinitionPageQuery(2, SearchTerm: "legacy", ContinuationToken: continuation));
            var deleted = await store.QueryPageAsync(
                new WorkflowDefinitionPageQuery(
                    2,
                    SearchTerm: "deleted",
                    State: WorkflowDefinitionPageState.Deleted));

            Assert.Equal(["legacy-c", "legacy-d"], second.Items.Select(item => item.Id));
            Assert.Null(second.NextContinuationToken);
            Assert.Equal(["deleted-legacy"], deleted.Items.Select(item => item.Id));
            Assert.All(
                bounded.Observations,
                observation => Assert.Equal(
                    WorkflowsDesignStorageManifest.SearchWorkflowDefinitionsQuery,
                    observation.Query.QueryIdentity));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.QueryPageAsync(
                    new WorkflowDefinitionPageQuery(2, SearchTerm: "different", ContinuationToken: continuation)));
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Scale_bearing_definition_pages_resume_deterministically_and_filter_lifecycle_on_every_provider(
        string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients);
        await driver.ResetPhysicalAsync([new WorkflowsDesignGroundworkStorageManifestSource()]);

        string continuation;
        await using (var client = await driver.OpenPhysicalClientAsync())
        {
            foreach (var id in new[] { "active-d", "active-b", "active-a", "active-c" })
                await SaveLegacyAsync(client.DocumentStore, Definition(id));
            await SaveLegacyAsync(client.DocumentStore, Definition("deleted-b", deleted: true));
            await SaveLegacyAsync(client.DocumentStore, Definition("deleted-a", deleted: true));

            var bounded = new RecordingBoundedDocumentStore(
                client.BoundedDocumentStore
                ?? throw new InvalidOperationException("The provider did not expose its admitted bounded-query runtime."));
            var store = new GroundworkWorkflowDefinitionStore(client.DocumentStore, bounded);
            var first = await store.QueryPageAsync(new WorkflowDefinitionPageQuery(2));

            Assert.Equal(["active-a", "active-b"], first.Items.Select(item => item.Id));
            continuation = Assert.IsType<string>(first.NextContinuationToken);
            var observation = Assert.Single(bounded.Observations);
            Assert.Equal(WorkflowsDesignStorageManifest.PageWorkflowDefinitionsQuery, observation.Query.QueryIdentity);
            Assert.Equal(2, observation.Query.Take);
            Assert.Equal(2, observation.MaterializedDocuments);
        }

        await using (var reopened = await driver.OpenPhysicalClientAsync())
        {
            var bounded = new RecordingBoundedDocumentStore(
                reopened.BoundedDocumentStore
                ?? throw new InvalidOperationException("The provider did not expose its admitted bounded-query runtime."));
            var store = new GroundworkWorkflowDefinitionStore(reopened.DocumentStore, bounded);
            var second = await store.QueryPageAsync(
                new WorkflowDefinitionPageQuery(2, ContinuationToken: continuation));
            var deleted = await store.QueryPageAsync(
                new WorkflowDefinitionPageQuery(10, State: WorkflowDefinitionPageState.Deleted));

            Assert.Equal(["active-c", "active-d"], second.Items.Select(item => item.Id));
            Assert.Null(second.NextContinuationToken);
            Assert.Equal(["deleted-a", "deleted-b"], deleted.Items.Select(item => item.Id));
            Assert.Equal(
                6,
                second.Items.Concat(deleted.Items)
                    .Select(item => item.Id)
                    .Concat(["active-a", "active-b"])
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(
                bounded.Observations,
                observation => Assert.Equal(
                    WorkflowsDesignStorageManifest.PageWorkflowDefinitionsQuery,
                    observation.Query.QueryIdentity));
        }
    }

    private static WorkflowDefinition Definition(string id, bool deleted = false) => new()
    {
        Id = id,
        Name = "unrelated",
        LastModifiedAt = Timestamp,
        DeletedAt = deleted ? Timestamp : null
    };

    private static Task SaveLegacyAsync(IDocumentStore store, WorkflowDefinition definition)
    {
        var document = new GroundworkDocument<WorkflowDefinition>(
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            definition);
        return store.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    private sealed class RecordingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
    {
        public List<QueryObservation> Observations { get; } = [];

        public async Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.QueryAsync(query, cancellationToken);
            Observations.Add(new QueryObservation(query, result.Documents.Count));
            return result;
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
    }

    private sealed record QueryObservation(DocumentQuery Query, int MaterializedDocuments);
}
