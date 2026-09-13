using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        var rendezvous = new CoordinatedMutationInterceptor();
        await using var left = await fixture.ReopenAsync("scope-a", rendezvous);
        await using var right = await fixture.ReopenAsync("scope-a", rendezvous);

        var results = await Task.WhenAll(
            left.Store.TryClaimAsync(Claim("node-a", "wf-race"), Now).AsTask(),
            right.Store.TryClaimAsync(Claim("node-b", "wf-race"), Now).AsTask());

        Assert.Equal(2, rendezvous.Arrivals);
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
        var rendezvous = new CoordinatedMutationInterceptor();
        await using var left = await fixture.ReopenAsync("scope-a", rendezvous);
        await using var right = await fixture.ReopenAsync("scope-a", rendezvous);
        var takeoverAt = Now.AddSeconds(2);

        var results = await Task.WhenAll(
            left.Store.TryClaimAsync(Claim("node-b", "wf-expired", takeoverAt), takeoverAt).AsTask(),
            right.Store.TryClaimAsync(Claim("node-c", "wf-expired", takeoverAt), takeoverAt).AsTask());

        Assert.Equal(2, rendezvous.Arrivals);
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
        var leases = await fixture.Store.ListOwnedAsync(new("node-a", Now.AddSeconds(2), 2));
        Assert.Collection(leases,
            lease => Assert.Equal("wf-a", lease.WorkflowExecutionId),
            lease => Assert.Equal("wf-z", lease.WorkflowExecutionId));
    }

    [Fact]
    public async Task Missing_scope_invalid_input_and_cancellation_fail_before_provider_io()
    {
        await using var fixture = await Fixture.CreateAsync(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.FindAsync("wf-1").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.TryClaimAsync(Claim("node-a", "wf-1"), Now).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ReleaseAsync(new("wf-1", "node-a", 1, Now, Now.AddSeconds(1))).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ListOwnedAsync(new("node-a", Now)).AsTask());

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.FindAsync(" ").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Store.TryClaimAsync(null!, Now).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Store.ReleaseAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Store.ListOwnedAsync(null!).AsTask());
        Assert.Throws<ArgumentException>(() => new ExecutionPlacementClaim(" ", "node-a", Now, Now.AddSeconds(1)));
        Assert.Throws<ArgumentException>(() => new ExecutionPlacementClaim("wf-1", " ", Now, Now.AddSeconds(1)));
        Assert.Throws<ArgumentException>(() => new ExecutionPlacementLease(" ", "node-a", 1, Now, Now.AddSeconds(1)));
        Assert.Throws<ArgumentException>(() => new ExecutionPlacementLease("wf-1", " ", 1, Now, Now.AddSeconds(1)));
        Assert.Throws<ArgumentException>(() => new ExecutionPlacementLeaseListRequest("", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionPlacementLeaseListRequest("node-a", Now, 0));

        await using var validFixture = await Fixture.CreateAsync("scope-a");
        var validClaim = Claim("node-a", "wf-cancel");
        var validLease = await validFixture.Store.TryClaimAsync(validClaim, Now);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validFixture.Store.FindAsync("wf-1", canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validFixture.Store.TryClaimAsync(validClaim, Now, canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validFixture.Store.ReleaseAsync(validLease.Lease, canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validFixture.Store.ListOwnedAsync(new("node-a", Now), canceled.Token).AsTask());
    }

    [Fact]
    public async Task Rollback_and_conflicting_write_leave_the_tracker_reusable()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await using var contender = await fixture.ReopenAsync("scope-a");

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await fixture.Store.TryClaimAsync(Claim("node-a", "wf-rollback"), Now);
            await transaction.RollbackAsync();
        }

        await using var reopened = await fixture.ReopenAsync("scope-a");
        Assert.Null(await reopened.Store.FindAsync("wf-rollback"));

        var results = await Task.WhenAll(
            fixture.Store.TryClaimAsync(Claim("node-a", "wf-conflict"), Now).AsTask(),
            contender.Store.TryClaimAsync(Claim("node-b", "wf-conflict"), Now).AsTask());
        Assert.Single(results, result => result.Outcome == ExecutionPlacementClaimOutcome.Granted);
        Assert.Single(results, result => result.Outcome == ExecutionPlacementClaimOutcome.Denied);

        var reusable = results[0].Outcome == ExecutionPlacementClaimOutcome.Denied ? fixture.Store : contender.Store;
        var subsequent = await reusable.TryClaimAsync(Claim("node-reusable", "wf-after-conflict"), Now);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, subsequent.Outcome);
    }

    [Fact]
    public async Task Maximum_length_unicode_identity_and_exact_scope_isolation_are_lossless()
    {
        var pathScope = "tenant-a:bc/🧪";
        await using var fixture = await Fixture.CreateAsync(pathScope);
        await using var collidingScope = await fixture.ReopenAsync("tenant-ab:c/🧪");
        var workflowId = new string('x', 126) + "😀";
        var ownerId = new string('o', 126) + "😀";

        var claimed = await fixture.Store.TryClaimAsync(Claim(ownerId, workflowId), Now);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, claimed.Outcome);
        AssertLeaseEqual(claimed.Lease, await fixture.Store.FindAsync(workflowId));
        Assert.Empty(await collidingScope.Store.ListOwnedAsync(new(ownerId, Now)));
        Assert.Null(await collidingScope.Store.FindAsync(workflowId));
    }

    [Fact]
    public async Task Non_UTC_timestamps_round_trip_losslessly_with_one_expiry_query_authority()
    {
        var requestedAt = new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.FromHours(5.5));
        await using var fixture = await Fixture.CreateAsync("scope-a");

        var claimed = await fixture.Store.TryClaimAsync(Claim("node-a", "wf-offset", requestedAt), requestedAt);
        AssertLeaseEqual(claimed.Lease, await fixture.Store.FindAsync("wf-offset"));
        AssertLeaseEqual(claimed.Lease, Assert.Single(await fixture.Store.ListOwnedAsync(new("node-a", requestedAt))));

        await using var reopened = await fixture.ReopenAsync("scope-a");
        AssertLeaseEqual(claimed.Lease, await reopened.Store.FindAsync("wf-offset"));
    }

    [Fact]
    public async Task Distinct_unpaired_surrogate_scopes_do_not_alias_and_reopen_losslessly()
    {
        await using var firstScope = await Fixture.CreateAsync("tenant-\uD800");
        await using var secondScope = await firstScope.ReopenAsync("tenant-\uD801");
        var workflowId = "wf-malformed-scope";

        var first = await firstScope.Store.TryClaimAsync(Claim("node-a", workflowId), Now);
        var second = await secondScope.Store.TryClaimAsync(Claim("node-b", workflowId), Now);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, first.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, second.Outcome);
        Assert.Equal(1, first.Lease.PlacementToken);
        Assert.Equal(1, second.Lease.PlacementToken);

        await using var reopened = await firstScope.ReopenAsync("tenant-\uD800");
        AssertLeaseEqual(first.Lease, await reopened.Store.FindAsync(workflowId));
    }

    [Fact]
    public async Task Corrupt_derived_projections_and_expiry_offset_fail_closed_without_unbounded_listing()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-projection"), Now);
        var row = await fixture.Context.PlacementLeases.SingleAsync();
        var originalScopeHash = row.ScopeKeyHash;
        var originalOwnerHash = row.OwnerIdHash;
        var originalOrderKey = row.WorkflowExecutionIdOrderKey;

        row.ScopeKeyHash = "corrupt";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.FindAsync("wf-projection").AsTask());

        row.ScopeKeyHash = originalScopeHash;
        row.OwnerIdHash = "corrupt";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.FindAsync("wf-projection").AsTask());

        row.OwnerIdHash = originalOwnerHash;
        row.WorkflowExecutionIdOrderKey = "corrupt";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ListOwnedAsync(new("node-a", Now)).AsTask());

        row.WorkflowExecutionIdOrderKey = originalOrderKey;
        row.ExpiresAtOffsetMinutes = 14 * 60 + 1;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.FindAsync("wf-projection").AsTask());
    }

    [Fact]
    public async Task Provider_failure_is_normalized_and_the_same_context_recovers_without_partial_write()
    {
        var interceptor = new FailOnceProviderInterceptor();
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);
        interceptor.Arm();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.TryClaimAsync(Claim("node-a", "wf-fails-once"), Now).AsTask());
        Assert.Contains("failed while claiming placement", failure.Message, StringComparison.Ordinal);
        Assert.Null(await fixture.Store.FindAsync("wf-fails-once"));

        var recovered = await fixture.Store.TryClaimAsync(Claim("node-a", "wf-recovers"), Now);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, recovered.Outcome);
        AssertLeaseEqual(recovered.Lease, await fixture.Store.FindAsync("wf-recovers"));
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
        Assert.Equal(expected.AcquiredAt.Offset, actual.AcquiredAt.Offset);
        Assert.Equal(expected.ExpiresAt, actual.ExpiresAt);
        Assert.Equal(expected.ExpiresAt.Offset, actual.ExpiresAt.Offset);
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

        public ExecutionPlacementSqliteDbContext Context => context;

        public static async Task<Fixture> CreateAsync(string? scope, DbCommandInterceptor? interceptor = null)
        {
            var path = Path.Join(Path.GetTempPath(), $"elsa-placement-{Guid.NewGuid():N}.db");
            var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ExecutionPlacementSqliteDbContext>().UseSqlite(connection);
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
            var context = new ExecutionPlacementSqliteDbContext(options.Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context, scope, ownsDatabase: true);
        }

        public async Task<Fixture> ReopenAsync(string? nextScope, DbCommandInterceptor? interceptor = null)
        {
            var next = new SqliteConnection(connection.ConnectionString);
            await next.OpenAsync();
            var options = new DbContextOptionsBuilder<ExecutionPlacementSqliteDbContext>().UseSqlite(next);
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
            var nextContext = new ExecutionPlacementSqliteDbContext(options.Options);
            return new Fixture(next, nextContext, nextScope, ownsDatabase: false);
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
            if (ownsDatabase)
                foreach (var file in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
                    File.Delete(file);
        }

        private sealed class Accessor(string? scope) : IPersistenceAccessContextAccessor
        {
            public PersistenceAccessContext Current => scope is null
                ? PersistenceAccessContext.Global
                : PersistenceAccessContext.Scoped(new PersistenceScope(scope));
        }
    }

    private sealed class CoordinatedMutationInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public int Arrivals => Volatile.Read(ref arrivals);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await CoordinateAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await CoordinateAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            await CoordinateAsync(command, cancellationToken);
            return result;
        }

        private async ValueTask CoordinateAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!IsMutationCommand(command.CommandText))
                return;

            var arrival = Interlocked.Increment(ref arrivals);
            if (arrival > 2)
                return;
            if (arrival == 2)
                bothArrived.TrySetResult(true);
            await bothArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailOnceProviderInterceptor : DbCommandInterceptor
    {
        private int armed;
        private int fired;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result) =>
            ShouldFail(command) ? throw new SyntheticProviderException() : result;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ShouldFail(command)
                ? throw new SyntheticProviderException()
                : ValueTask.FromResult(result);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result) =>
            ShouldFail(command) ? throw new SyntheticProviderException() : result;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            ShouldFail(command)
                ? throw new SyntheticProviderException()
                : ValueTask.FromResult(result);

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result) =>
            ShouldFail(command) ? throw new SyntheticProviderException() : result;

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default) =>
            ShouldFail(command)
                ? throw new SyntheticProviderException()
                : ValueTask.FromResult(result);

        private bool ShouldFail(DbCommand command) =>
            IsMutationCommand(command.CommandText) &&
            Volatile.Read(ref armed) == 1 &&
            Interlocked.Exchange(ref fired, 1) == 0;
    }

    private sealed class SyntheticProviderException() : DbException("synthetic provider failure")
    {
    }

    private static bool IsMutationCommand(string commandText)
    {
        var sql = commandText.AsSpan().TrimStart();
        return sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
               sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
               sql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
    }
}
