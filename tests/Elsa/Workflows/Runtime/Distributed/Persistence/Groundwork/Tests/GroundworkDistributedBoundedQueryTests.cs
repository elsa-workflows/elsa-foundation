using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

public sealed class GroundworkDistributedBoundedQueryTests
{
    [Fact]
    public async Task StoreReadsUseDeclaredBoundedQueryIdentitiesAndPaths()
    {
        var documents = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var placements = new GroundworkExecutionPlacementStore(documents, queries);
        var transport = new GroundworkExecutionCommandTransport(
            documents,
            GroundworkDistributedTestAccess.Scoped(),
            queries);

        await placements.ListPageAsync(new ExecutionPlacementLeasePageRequest(10, 25));
        await transport.ListPendingExecutionIdsAsync(DateTimeOffset.UtcNow);
        await transport.CountPendingAsync("execution-1");

        Assert.Collection(
            queries.CountObserved,
            query => AssertQuery(
                query,
                DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind,
                DistributedGroundworkStorageManifest.ListAllQuery,
                DistributedGroundworkStorageManifest.CollectionField,
                DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind));

        Assert.Collection(
            queries.Observed,
            query => AssertQuery(
                query,
                DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind,
                DistributedGroundworkStorageManifest.ListAllQuery,
                DistributedGroundworkStorageManifest.CollectionField,
                DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind,
                10,
                25),
            query => AssertQuery(
                query,
                DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind,
                DistributedGroundworkStorageManifest.ListAllQuery,
                DistributedGroundworkStorageManifest.CollectionField,
                DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind),
            query => AssertQuery(
                query,
                DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind,
                DistributedGroundworkStorageManifest.ListByWorkflowExecutionQuery,
                DistributedGroundworkStorageManifest.WorkflowExecutionIdField,
                "execution-1"));
    }

    [Fact]
    public async Task SendRejectsEnvelopeFromAnotherPartitionBeforeProviderIo()
    {
        var documents = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var transport = new GroundworkExecutionCommandTransport(
            documents,
            GroundworkDistributedTestAccess.Scoped("tenant-a"),
            queries);
        var now = DateTimeOffset.UtcNow;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.SendAsync(
                "execution-1",
                DistributedStoreHarness.Envelope("execution-1", "envelope-1", now, "tenant-b"),
                now));

        Assert.Equal("The requested resource does not belong to the current persistence scope.", exception.Message);
        Assert.Empty(queries.Observed);
        Assert.Equal(0, documents.LoadCount);
        Assert.Equal(0, documents.SaveCount);
        Assert.Equal(0, documents.DeleteCount);
        Assert.Equal(0, documents.BeginCount);
        Assert.Empty(documents.Snapshot(DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind));
    }

    private static void AssertQuery(
        DocumentQuery query,
        string documentKind,
        string identity,
        string path,
        string value,
        int? skip = null,
        int? take = null)
    {
        Assert.Equal(documentKind, query.DocumentKind);
        Assert.Equal(identity, query.QueryIdentity);
        Assert.Equal(skip, query.Skip);
        Assert.Equal(take, query.Take);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(path, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal(value, Assert.Single(comparison.Values));
    }

    private sealed class RecordingBoundedDocumentStore : IBoundedDocumentStore
    {
        public List<DocumentQuery> Observed { get; } = [];
        public List<DocumentQuery> CountObserved { get; } = [];

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Observed.Add(query);
            return Task.FromResult(DocumentQueryResult.Empty);
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            CountObserved.Add(query);
            return Task.FromResult(0L);
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
