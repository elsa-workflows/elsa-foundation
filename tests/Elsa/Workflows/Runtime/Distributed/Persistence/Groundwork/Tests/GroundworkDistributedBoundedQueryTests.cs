using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Core.Capabilities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Text;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

public sealed class GroundworkDistributedBoundedQueryTests
{
    [Fact]
    public async Task ManifestSource_DeclaresAtomicStreamAppendAndTransactionTopologyPrerequisites()
    {
        var declaration = await new DistributedGroundworkStorageManifestSource()
            .CreateDeclarationAsync(CancellationToken.None);

        var route = Assert.Single(declaration.RequiredRoutes);
        Assert.Equal(DistributedRuntimeStorageManifest.ExecutionCommandStreamHeadDocumentKind, route.StorageUnit.Value);
        Assert.Equal("command-stream-append", route.RouteIdentity);
        Assert.Equal([WellKnownCapabilities.AtomicCommit], route.RequiredCapabilities);
        Assert.Equal(
            RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity,
            Assert.Single(declaration.TopologyRequirements).Identity);
    }

    [Fact]
    public void ManifestUsesPortableOrdinalKeysWithinTheDeclaredIdentityBoundary()
    {
        var manifest = DistributedGroundworkStorageManifest.Create();
        var expectedOrdinalKeyLength = DistributedRuntimeIdentityConstraints.MaximumLength * 4;
        var placement = manifest.StorageUnits.Single(
            unit => unit.Identity.Value == DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind);
        var transport = manifest.StorageUnits.Single(
            unit => unit.Identity.Value == DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind);
        var streamHead = manifest.StorageUnits.Single(
            unit => unit.Identity.Value == DistributedRuntimeStorageManifest.ExecutionCommandStreamHeadDocumentKind);

        AssertProjectionLength(
            placement,
            DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
            expectedOrdinalKeyLength);
        AssertProjectionLength(
            placement,
            DistributedGroundworkStorageManifest.OwnerIdComparisonKeyField,
            expectedOrdinalKeyLength);
        AssertProjectionLength(
            placement,
            DistributedGroundworkStorageManifest.OwnerIdLookupKeyField,
            64);
        AssertProjectionLength(
            transport,
            DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
            expectedOrdinalKeyLength);
        AssertProjectionLength(
            transport,
            DistributedGroundworkStorageManifest.CollectionField,
            128);
        AssertProjectionLength(
            transport,
            DistributedGroundworkStorageManifest.LeaseOwnerIdField,
            64);
        var streamHeadPhysicalStorage = Assert.IsType<StorageUnitPhysicalStorage>(streamHead.PhysicalStorage);
        var streamHeadTable = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(streamHeadPhysicalStorage.Policy).Definition;
        Assert.Contains(
            streamHeadTable.ProjectedColumns,
            column => column.Path == DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField);
        Assert.Contains(
            streamHeadTable.ProjectedColumns,
            column => column.Path == DistributedGroundworkStorageManifest.LastSequenceField);

        var physicalStorage = Assert.IsType<StorageUnitPhysicalStorage>(transport.PhysicalStorage);
        var countRoute = Assert.Single(
            physicalStorage.BoundedQueries,
            query => query.Identity == DistributedGroundworkStorageManifest.CountPendingCommandsQuery);
        Assert.Equal(
            new[] { BoundedQueryResultOperation.Count },
            countRoute.ResultOperations.Order());
    }

    [Fact]
    public async Task OwnedPlacementRetrievalBindsOwnerExpiryOrderAndFiniteLimitToItsDeclaredRoute()
    {
        var documents = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var placements = new GroundworkExecutionPlacementStore(documents, queries);
        var now = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

        await placements.ListOwnedAsync(new ExecutionPlacementLeaseListRequest("node-a", now, take: 25));

        var query = Assert.Single(queries.Observed);
        Assert.Equal(DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind, query.DocumentKind);
        Assert.Equal(DistributedGroundworkStorageManifest.ListOwnedPlacementsQuery, query.QueryIdentity);
        Assert.Null(query.Skip);
        Assert.Equal(25, query.Take);
        Assert.Collection(
            query.Clauses,
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.OwnerIdLookupKeyField,
                QueryComparisonOperator.Equal,
                PortableStringComparison.CreateHash(PortableStringComparison.CreateOrdinal("node-a"))),
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.OwnerIdComparisonKeyField,
                QueryComparisonOperator.Equal,
                PortableStringComparison.CreateOrdinal("node-a")),
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.ExpiresAtField,
                QueryComparisonOperator.GreaterThan,
                now.ToString("O")));
        Assert.Collection(
            query.Order,
            order =>
            {
                Assert.Equal(DistributedGroundworkStorageManifest.OwnerIdLookupKeyField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            },
            order =>
            {
                Assert.Equal(DistributedGroundworkStorageManifest.ExpiresAtField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            },
            order =>
            {
                Assert.Equal(DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            });
    }

    [Fact]
    public async Task PendingExecutionDiscoveryBindsVisibilityDistinctnessOrderAndFiniteLimitToItsDeclaredRoute()
    {
        var documents = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var transport = new GroundworkExecutionCommandTransport(
            documents,
            GroundworkDistributedTestAccess.Scoped(),
            queries);
        var now = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

        await transport.ListPendingExecutionIdsAsync(now, maxItems: 25);

        var query = Assert.Single(queries.Observed);
        Assert.Equal(DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind, query.DocumentKind);
        Assert.Equal(DistributedGroundworkStorageManifest.ListPendingExecutionIdsQuery, query.QueryIdentity);
        Assert.Null(query.Skip);
        Assert.Equal(25, query.Take);
        Assert.Equal(DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField, query.LatestPerKeyPath);
        Assert.Collection(
            query.Clauses,
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.CollectionField,
                QueryComparisonOperator.Equal,
                DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind),
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.VisibleAtField,
                QueryComparisonOperator.LessThanOrEqual,
                now.ToString("O")));
        Assert.Collection(
            query.Order,
            order =>
            {
                Assert.Equal(DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            },
            order =>
            {
                Assert.Equal(DistributedGroundworkStorageManifest.SequenceField, order.Path);
                Assert.Equal(PhysicalSortDirection.Descending, order.Direction);
            });
    }

    [Fact]
    public async Task CommandLeaseBindsExecutionVisibilitySequenceOrderAndFiniteLimitToItsDeclaredRoute()
    {
        var documents = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var transport = new GroundworkExecutionCommandTransport(
            documents,
            GroundworkDistributedTestAccess.Scoped(),
            queries);
        var now = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

        await transport.LeaseAsync("execution-1", "node-a", now, TimeSpan.FromSeconds(30), maxItems: 25);

        var query = Assert.Single(queries.Observed);
        Assert.Equal(DistributedGroundworkStorageManifest.LeaseVisibleCommandsQuery, query.QueryIdentity);
        Assert.Null(query.Skip);
        Assert.Equal(25, query.Take);
        Assert.Collection(
            query.Clauses,
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
                QueryComparisonOperator.Equal,
                PortableStringComparison.CreateOrdinal("execution-1")),
            clause => AssertComparison(
                clause,
                DistributedGroundworkStorageManifest.VisibleAtField,
                QueryComparisonOperator.LessThanOrEqual,
                now.ToString("O")));
        Assert.Collection(
            query.Order,
            order =>
            {
                Assert.Equal(DistributedGroundworkStorageManifest.SequenceField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            });
    }

    [Fact]
    public async Task PendingCountUsesTheProviderCountOperationOnItsDeclaredRoute()
    {
        var documents = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var transport = new GroundworkExecutionCommandTransport(
            documents,
            GroundworkDistributedTestAccess.Scoped(),
            queries);

        await transport.CountPendingAsync("execution-1");

        Assert.Empty(queries.Observed);
        var query = Assert.Single(queries.CountObserved);
        AssertQuery(
            query,
            DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind,
            DistributedGroundworkStorageManifest.CountPendingCommandsQuery,
            DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
            PortableStringComparison.CreateOrdinal("execution-1"));
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

    [Fact]
    public async Task SendAllocatesSequenceThroughStreamHeadWithoutBacklogQuery()
    {
        var documents = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var transport = new GroundworkExecutionCommandTransport(
            documents,
            GroundworkDistributedTestAccess.Scoped(),
            queries);
        var now = DateTimeOffset.UtcNow;

        var first = await transport.SendAsync("execution-1", DistributedStoreHarness.Envelope("execution-1", "envelope-1", now), now);
        var second = await transport.SendAsync("execution-1", DistributedStoreHarness.Envelope("execution-1", "envelope-2", now), now);

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        Assert.Empty(queries.Observed);
        Assert.Empty(queries.CountObserved);
        Assert.Equal(2, documents.LoadCount);
        Assert.Equal(2, documents.BeginCount);
        Assert.Single(documents.Snapshot(DistributedRuntimeStorageManifest.ExecutionCommandStreamHeadDocumentKind));
        Assert.Equal(2, documents.Snapshot(DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind).Count);
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

    private static void AssertComparison(
        DocumentQueryClause clause,
        string path,
        QueryComparisonOperator operation,
        string value)
    {
        var comparison = Assert.Single(clause.Comparisons);
        Assert.Equal(path, comparison.Path);
        Assert.Equal(operation, comparison.Operator);
        Assert.Equal(value, Assert.Single(comparison.Values));
    }

    private static void AssertProjectionLength(
        global::Groundwork.Core.Manifests.StorageUnit unit,
        string logicalName,
        int expectedLength)
    {
        var policy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
            unit.PhysicalStorage!.Policy);
        var projection = Assert.Single(
            policy.Definition.ProjectedColumns,
            column => column.LogicalName == logicalName);
        Assert.Equal(expectedLength, projection.Length);
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
