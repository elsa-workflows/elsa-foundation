using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>Runs the command transport contract against the product in-memory store and the public v2 SQLite adapter.</summary>
public sealed class ExecutionCommandTransportContractTests
{
    private const string ExecutionId = "wf-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    public static TheoryData<string> Providers => new()
    {
        DistributedStoreHarness.InMemory,
        DistributedStoreHarness.GroundworkSqlite
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task SendSequencesStrictlyIncreaseAndLeaseDrainsInOrder(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        var first = await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        var second = await harness.Transport.SendAsync(ExecutionId, Envelope("env-2"), Now);

        var leased = await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10);

        Assert.True(second.Sequence > first.Sequence);
        Assert.Equal(["env-1", "env-2"], leased.Select(item => item.Envelope.EnvelopeId));
        Assert.All(leased, item => Assert.Equal(NodeA, item.LeasedByOwnerId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task LeaseHidesUntilExpiryThenIncrementsDeliveryAttempt(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10));

        Assert.Empty(await harness.Transport.LeaseAsync(ExecutionId, NodeB, Now.AddSeconds(1), LeaseDuration, 10));
        Assert.Empty(await harness.Transport.ListPendingExecutionIdsAsync(Now.AddSeconds(1), 10));

        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        Assert.Equal([ExecutionId], await harness.Transport.ListPendingExecutionIdsAsync(afterExpiry, 10));
        var released = await harness.Transport.LeaseAsync(ExecutionId, NodeB, afterExpiry, LeaseDuration, 10);
        Assert.Equal(2, Assert.Single(released).DeliveryAttemptCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task AckRequiresTheCurrentLiveHolderAndDeletesExactlyOneItem(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        var item = Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10));

        Assert.False(await harness.Transport.AckAsync(ExecutionId, item.TransportItemId, NodeB, item.LeaseToken!.Value, Now.AddSeconds(1)));
        Assert.True(await harness.Transport.AckAsync(ExecutionId, item.TransportItemId, NodeA, item.LeaseToken.Value, Now.AddSeconds(1)));
        Assert.Equal(0, await harness.Transport.CountPendingAsync(ExecutionId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ExpiredOrSupersededHolderCannotAck(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        var first = Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10));
        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        Assert.False(await harness.Transport.AckAsync(ExecutionId, first.TransportItemId, NodeA, first.LeaseToken!.Value, afterExpiry));

        var second = Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeB, afterExpiry, LeaseDuration, 10));
        Assert.False(await harness.Transport.AckAsync(ExecutionId, first.TransportItemId, NodeA, first.LeaseToken.Value, afterExpiry.AddSeconds(1)));
        Assert.True(await harness.Transport.AckAsync(ExecutionId, second.TransportItemId, NodeB, second.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
    }

    [Fact]
    public async Task DurableAdapterReopenReleasesExpiredItemAndRejectsStaleToken()
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(DistributedStoreHarness.GroundworkSqlite);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        var before = Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10));
        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);

        var reopened = await harness.ReopenTransportAsync();
        var after = Assert.Single(await reopened.LeaseAsync(ExecutionId, NodeA, afterExpiry, LeaseDuration, 10));

        Assert.NotEqual(before.LeaseToken, after.LeaseToken);
        Assert.False(await reopened.AckAsync(ExecutionId, before.TransportItemId, NodeA, before.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
        Assert.True(await reopened.AckAsync(ExecutionId, after.TransportItemId, NodeA, after.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task LeaseAndPendingQueriesRespectBoundsAcrossExecutions(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync("wf-a", Envelope("env-1", "wf-a"), Now);
        await harness.Transport.SendAsync("wf-a", Envelope("env-2", "wf-a"), Now);
        await harness.Transport.SendAsync("wf-b", Envelope("env-3", "wf-b"), Now);

        Assert.Equal(["wf-a"], await harness.Transport.ListPendingExecutionIdsAsync(Now, 1));
        Assert.Equal(["wf-a", "wf-b"], await harness.Transport.ListPendingExecutionIdsAsync(Now, 2));
        Assert.Single(await harness.Transport.LeaseAsync("wf-a", NodeA, Now, LeaseDuration, 1));
        Assert.Equal(2, await harness.Transport.CountPendingAsync("wf-a"));
    }

    [Fact]
    public async Task PendingHeadTracksLeaseAckAndFinalRemovalAcrossRestart()
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(DistributedStoreHarness.GroundworkSqlite);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-2"), Now);

        var leased = await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10);
        Assert.Equal(2, leased.Count);
        Assert.Empty(await harness.Transport.ListPendingExecutionIdsAsync(Now, 10));

        var reopened = await harness.ReopenTransportAsync();
        Assert.True(await reopened.AckAsync(ExecutionId, leased[0].TransportItemId, NodeA, leased[0].LeaseToken!.Value, Now.AddSeconds(1)));
        Assert.Empty(await reopened.ListPendingExecutionIdsAsync(Now.AddSeconds(1), 10));
        Assert.True(await reopened.AckAsync(ExecutionId, leased[1].TransportItemId, NodeA, leased[1].LeaseToken!.Value, Now.AddSeconds(1)));
        Assert.Empty(await reopened.ListPendingExecutionIdsAsync(Now.AddSeconds(1), 10));

        var next = await reopened.SendAsync(ExecutionId, Envelope("env-3"), Now.AddSeconds(2));
        Assert.Equal(3, next.Sequence);
        Assert.Equal([ExecutionId], await reopened.ListPendingExecutionIdsAsync(Now.AddSeconds(2), 10));
    }

    [Fact]
    public async Task ConcurrentSqliteLeasesReplenishAContendedPageUntilBothCallsFill()
    {
        await using var fixture = await QueryBarrierFixture.CreateAsync();
        const int visibleCommands = 20;
        const int requestedPerTransport = 10;
        for (var index = 0; index < visibleCommands; index++)
            await fixture.First.SendAsync(ExecutionId, Envelope($"env-{index:D2}"), Now);

        fixture.ArmQueryBarrier();
        var first = fixture.First;
        var second = fixture.Second;
        var claims = await Task.WhenAll(
            Task.Run(() => first.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, requestedPerTransport).AsTask()),
            Task.Run(() => second.LeaseAsync(ExecutionId, NodeB, Now, LeaseDuration, requestedPerTransport).AsTask()));

        Assert.All(claims, batch =>
        {
            Assert.Equal(requestedPerTransport, batch.Count);
            Assert.Equal(batch.Select(item => item.Sequence).Order(), batch.Select(item => item.Sequence));
        });
        Assert.Equal(
            Enumerable.Range(1, visibleCommands).Select(index => (long)index),
            claims.SelectMany(batch => batch).Select(item => item.Sequence).Order());
        Assert.Equal(
            visibleCommands,
            claims.SelectMany(batch => batch).Select(item => item.TransportItemId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task CancelledLeaseStopsBeforeScanningTheFirstPage()
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(DistributedStoreHarness.GroundworkSqlite);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-cancel"), Now);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task SqlitePreservesMaximumIdentityAndOrdinalUnicodeOrdering()
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(DistributedStoreHarness.GroundworkSqlite);
        var supplementary = "wf-\U00010000";
        var privateUse = "wf-\uE000";
        var maximumLength = new string('x', DistributedRuntimeIdentityConstraints.MaximumLength - 3) + "\U0001F600:";
        foreach (var executionId in new[] { privateUse, maximumLength, supplementary })
            await harness.Transport.SendAsync(executionId, Envelope($"env-{executionId.Length}", executionId), Now);

        var expected = new[] { privateUse, maximumLength, supplementary }
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, await harness.Transport.ListPendingExecutionIdsAsync(Now, 10));
    }

    private static Elsa.Workflows.Runtime.Core.Models.WorkflowExecutionCommandEnvelope Envelope(
        string envelopeId,
        string executionId = ExecutionId) =>
        DistributedStoreHarness.Envelope(executionId, envelopeId, Now);

    private sealed class QueryBarrierFixture : IAsyncDisposable
    {
        private readonly string databasePath;
        private readonly IStorageProviderConnection connection;
        private readonly QueryBarrierSessionSource source;

        private QueryBarrierFixture(
            string databasePath,
            IStorageProviderConnection connection,
            QueryBarrierSessionSource source,
            IExecutionCommandTransport first,
            IExecutionCommandTransport second)
        {
            this.databasePath = databasePath;
            this.connection = connection;
            this.source = source;
            First = first;
            Second = second;
        }

        public IExecutionCommandTransport First { get; }
        public IExecutionCommandTransport Second { get; }

        public static ValueTask<QueryBarrierFixture> CreateAsync()
        {
            var path = Path.Join(Path.GetTempPath(), $"elsa-command-lease-replenishment-{Guid.NewGuid():N}.db");
            var connection = new SqliteProviderFactory().Create($"Data Source={path}");
            var units = DistributedGroundworkStorageManifest.CreateUnits();
            foreach (var unit in units)
                connection.Schema.Apply(unit);
            var source = new QueryBarrierSessionSource(connection, units);
            var first = new GroundworkExecutionCommandTransport(source, new FixedAccessor());
            var second = new GroundworkExecutionCommandTransport(source, new FixedAccessor());
            return ValueTask.FromResult(new QueryBarrierFixture(path, connection, source, first, second));
        }

        public void ArmQueryBarrier() => source.ArmQueryBarrier();

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            foreach (var candidate in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
            return ValueTask.CompletedTask;
        }

        private sealed class QueryBarrierSessionSource(
            IStorageProviderConnection connection,
            IReadOnlyList<StorageUnit> units) : IGroundworkStorageSessionSource
        {
            private readonly Dictionary<string, StorageUnit> unitsById =
                units.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);

            private Barrier? queryBarrier;
            private int queryBarrierCalls;

            public void ArmQueryBarrier()
            {
                Volatile.Write(ref queryBarrierCalls, 0);
                Volatile.Write(ref queryBarrier, new Barrier(2));
            }

            public bool AwaitInitialQueries()
            {
                var barrier = Volatile.Read(ref queryBarrier);
                if (barrier is null || Interlocked.Increment(ref queryBarrierCalls) > 2)
                    return true;
                return barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            }

            public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
            {
                var session = connection.OpenSession(unitsById[unitId], access);
                return unitId == DistributedGroundworkStorageManifest.CommandTransportUnitId
                    ? new QueryBarrierStorageSession(session, this)
                    : session;
            }

            public IUnitOfWork BeginUnitOfWork(
                StorageAccess access,
                BatchWriteOptions options,
                IReadOnlyList<string> unitIds,
                string? targetName = null) =>
                connection.BeginUnitOfWork(access, options, unitIds.Select(unitId => unitsById[unitId]).ToArray());

            public StorageUnit Unit(string unitId, string? targetName = null) => unitsById[unitId];
        }

        private sealed class QueryBarrierStorageSession(
            IStorageSession inner,
            QueryBarrierSessionSource source) : IStorageSession, IConcurrencyStorageSession
        {
            public StorageUnit Unit => inner.Unit;
            public StorageAccess Access => inner.Access;

            public StoredEntry? Read(StorageKey key) => inner.Read(key);
            public ValueTask<StoredEntry?> ReadAsync(StorageKey key, CancellationToken cancellationToken = default) => inner.ReadAsync(key, cancellationToken);

            public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
            {
                var result = inner.Query(request, options);
                if (!source.AwaitInitialQueries())
                    throw new TimeoutException("The SQLite query contention barrier did not observe both lease queries.");
                return result;
            }

            public ValueTask<QueryMaterializedResult> QueryAsync(QueryRequest request, QueryRenderOptions? options = null, CancellationToken cancellationToken = default) => inner.QueryAsync(request, options, cancellationToken);
            public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
            public ValueTask<AggregationResult> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken = default) => inner.AggregateAsync(query, cancellationToken);
            public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
            public ValueTask<WriteOutcome> InsertAsync(StorageValues values, WriteOptions? options = null, CancellationToken cancellationToken = default) => inner.InsertAsync(values, options, cancellationToken);
            public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
            public ValueTask<WriteOutcome> UpdateAsync(StorageValues values, WriteOptions? options = null, CancellationToken cancellationToken = default) => inner.UpdateAsync(values, options, cancellationToken);
            public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
            public ValueTask<WriteOutcome> UpsertAsync(StorageValues values, WriteOptions? options = null, CancellationToken cancellationToken = default) => inner.UpsertAsync(values, options, cancellationToken);
            public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
            public ValueTask<WriteOutcome> DeleteAsync(StorageKey key, WriteOptions? options = null, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, options, cancellationToken);
            public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
            public ValueTask<WriteOutcome> AppendAsync(OperationId operationId, IReadOnlyList<StorageValues> values, CancellationToken cancellationToken = default) => inner.AppendAsync(operationId, values, cancellationToken);
            public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) => ((IConcurrencyStorageSession)inner).ConditionalUpsert(values, options);
            public ValueTask<WriteOutcome> ConditionalUpsertAsync(StorageValues values, WriteOptions? options = null, CancellationToken cancellationToken = default) => ((IConcurrencyStorageSession)inner).ConditionalUpsertAsync(values, options, cancellationToken);
        }

        private sealed class FixedAccessor : IPersistenceAccessContextAccessor
        {
            public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(PersistenceScope.DefaultValue));
        }
    }
}
