using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests;

public sealed class EfExecutionPlacementStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Round_trip_scope_isolation_claim_renew_takeover_release_and_restart()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var first = await fixture.Store.TryClaimAsync(Claim("node-a", "wf-ä"), Now);
        var renewal = await fixture.Store.TryClaimAsync(Claim("node-a", "wf-ä", Now.AddSeconds(2)), Now.AddSeconds(2));
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, first.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Renewed, renewal.Outcome);
        Assert.Equal(2, renewal.Lease.PlacementToken);
        AssertLeaseEqual(renewal.Lease, await fixture.Store.FindAsync("wf-ä"));

        await using var otherScope = await fixture.ReopenAsync("scope-b");
        Assert.Null(await otherScope.Store.FindAsync("wf-ä"));
        var takeover = await fixture.Store.TryClaimAsync(Claim("node-b", "wf-ä", Now.AddMinutes(1)), Now.AddMinutes(1));
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, takeover.Outcome);
        Assert.Equal(3, takeover.Lease.PlacementToken);
        await fixture.Store.ReleaseAsync(renewal.Lease);
        AssertLeaseEqual(takeover.Lease, await fixture.Store.FindAsync("wf-ä"));
        await fixture.Store.ReleaseAsync(takeover.Lease);
        Assert.Null(await fixture.Store.FindAsync("wf-ä"));

        await using var reopened = await fixture.ReopenAsync("scope-a");
        Assert.Null(await reopened.Store.FindAsync("wf-ä"));
    }

    [Fact]
    public async Task Foreign_live_claim_is_denied_and_stale_release_cannot_clear_successor()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var first = await fixture.Store.TryClaimAsync(Claim("node-a", "wf-1"), Now);
        var denied = await fixture.Store.TryClaimAsync(Claim("node-b", "wf-1", Now.AddSeconds(1)), Now.AddSeconds(1));
        Assert.Equal(ExecutionPlacementClaimOutcome.Denied, denied.Outcome);
        AssertLeaseEqual(first.Lease, denied.Lease);

        var successor = await fixture.Store.TryClaimAsync(Claim("node-b", "wf-1", Now.AddMinutes(1)), Now.AddMinutes(1));
        await fixture.Store.ReleaseAsync(first.Lease);
        AssertLeaseEqual(successor.Lease, await fixture.Store.FindAsync("wf-1"));
    }

    [Fact]
    public async Task Concurrent_first_claims_have_exactly_one_authoritative_winner()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await using var contender = await fixture.ReopenAsync("scope-a");

        var results = await Task.WhenAll(
            fixture.Store.TryClaimAsync(Claim("node-a", "wf-race"), Now).AsTask(),
            contender.Store.TryClaimAsync(Claim("node-b", "wf-race"), Now).AsTask());

        Assert.Single(results, result => result.Outcome == ExecutionPlacementClaimOutcome.Granted);
        Assert.Single(results, result => result.Outcome == ExecutionPlacementClaimOutcome.Denied);
        var winner = results.Single(result => result.Outcome == ExecutionPlacementClaimOutcome.Granted).Lease;
        AssertLeaseEqual(winner, await fixture.Store.FindAsync("wf-race"));
    }

    [Fact]
    public async Task Concurrent_expired_takeover_claims_have_exactly_one_winner()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-expired", Now, 1), Now);
        await using var contender = await fixture.ReopenAsync("scope-a");
        var takeoverAt = Now.AddSeconds(2);

        var results = await Task.WhenAll(
            fixture.Store.TryClaimAsync(Claim("node-b", "wf-expired", takeoverAt), takeoverAt).AsTask(),
            contender.Store.TryClaimAsync(Claim("node-c", "wf-expired", takeoverAt), takeoverAt).AsTask());

        Assert.Single(results, result => result.Outcome == ExecutionPlacementClaimOutcome.Granted);
        Assert.Single(results, result => result.Outcome == ExecutionPlacementClaimOutcome.Denied);
        var winner = results.Single(result => result.Outcome == ExecutionPlacementClaimOutcome.Granted).Lease;
        Assert.Equal(2, winner.PlacementToken);
        AssertLeaseEqual(winner, await fixture.Store.FindAsync("wf-expired"));
    }

    [Fact]
    public async Task Listing_is_live_owner_filtered_provider_bounded_and_ordinally_ordered()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-z", Now, 20), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-a", Now, 20), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-expired", Now, 1), Now);
        await fixture.Store.TryClaimAsync(Claim("node-b", "wf-other", Now, 20), Now);
        var leases = await fixture.Store.ListOwnedAsync(new("node-a", Now.AddSeconds(2), 1));
        var lease = Assert.Single(leases);
        Assert.Equal("wf-a", lease.WorkflowExecutionId);
    }

    [Fact]
    public async Task Missing_scope_invalid_input_and_cancellation_fail_before_provider_io()
    {
        await using var fixture = await Fixture.CreateAsync(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.FindAsync("wf-1").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.FindAsync(" ").AsTask());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.FindAsync("wf-1", canceled.Token).AsTask());
    }

    private static ExecutionPlacementClaim Claim(string owner, string id, DateTimeOffset? requestedAt = null, int seconds = 30)
    {
        var requested = requestedAt ?? Now;
        return new(id, owner, requested, requested.AddSeconds(seconds));
    }

    private static void AssertLeaseEqual(ExecutionPlacementLease expected, ExecutionPlacementLease? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.WorkflowExecutionId, actual.WorkflowExecutionId);
        Assert.Equal(expected.OwnerId, actual.OwnerId);
        Assert.Equal(expected.PlacementToken, actual.PlacementToken);
        Assert.Equal(expected.AcquiredAt, actual.AcquiredAt);
        Assert.Equal(expected.ExpiresAt, actual.ExpiresAt);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string databasePath;
        private readonly bool ownsDatabase;
        private readonly string scope;
        private readonly ExecutionPlacementSqliteDbContext context;

        private Fixture(SqliteConnection connection, ExecutionPlacementSqliteDbContext context, string? scope, bool ownsDatabase)
        {
            this.connection = connection;
            databasePath = new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource;
            this.ownsDatabase = ownsDatabase;
            this.context = context;
            this.scope = scope!;
            Store = new EfExecutionPlacementStore(context, new Accessor(scope));
        }

        public EfExecutionPlacementStore Store { get; }

        public static async Task<Fixture> CreateAsync(string? scope)
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-placement-{Guid.NewGuid():N}.db");
            var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var context = new ExecutionPlacementSqliteDbContext(new DbContextOptionsBuilder<ExecutionPlacementSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context, scope, ownsDatabase: true);
        }

        public async Task<Fixture> ReopenAsync(string? nextScope)
        {
            var next = new SqliteConnection(connection.ConnectionString);
            await next.OpenAsync();
            var nextContext = new ExecutionPlacementSqliteDbContext(new DbContextOptionsBuilder<ExecutionPlacementSqliteDbContext>().UseSqlite(next).Options);
            return new Fixture(next, nextContext, nextScope, ownsDatabase: false);
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
            if (ownsDatabase)
                foreach (var file in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
                    if (File.Exists(file)) File.Delete(file);
        }

        private sealed class Accessor(string? scope) : IPersistenceAccessContextAccessor
        {
            public PersistenceAccessContext Current => scope is null
                ? PersistenceAccessContext.Global
                : PersistenceAccessContext.Scoped(new PersistenceScope(scope));
        }
    }
}
