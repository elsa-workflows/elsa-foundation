using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests;

public sealed class EfExecutionPlacementStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);
    private const int Deadlock = 1205;

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
    public async Task Release_preserves_the_fence_across_reclaim_and_old_release_cannot_delete_the_successor()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var first = (await fixture.Store.TryClaimAsync(Claim("node-a", "wf-reclaim"), Now)).Lease;

        await fixture.Store.ReleaseAsync(first);
        Assert.Null(await fixture.Store.FindAsync(first.WorkflowExecutionId));
        var released = await fixture.Context.PlacementLeases.AsNoTracking().SingleAsync();
        Assert.True(released.IsReleased);
        Assert.Equal(first.PlacementToken, released.PlacementToken);
        var releasedRevision = released.Revision;

        await fixture.Store.ReleaseAsync(first);
        var afterRepeatedRelease = await fixture.Context.PlacementLeases.AsNoTracking().SingleAsync();
        Assert.Equal(releasedRevision, afterRepeatedRelease.Revision);

        var successor = (await fixture.Store.TryClaimAsync(
            Claim("node-a", first.WorkflowExecutionId, Now.AddSeconds(1)),
            Now.AddSeconds(1))).Lease;
        Assert.Equal(first.PlacementToken + 1, successor.PlacementToken);

        await fixture.Store.ReleaseAsync(first);
        AssertLeaseEqual(successor, await fixture.Store.FindAsync(first.WorkflowExecutionId));
        var active = await fixture.Context.PlacementLeases.AsNoTracking().SingleAsync();
        Assert.False(active.IsReleased);
        Assert.True(active.Revision > releasedRevision);

        await using var reopened = await fixture.ReopenAsync("scope-a");
        AssertLeaseEqual(successor, await reopened.Store.FindAsync(first.WorkflowExecutionId));
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
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-latest", Now, 30), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-later", Now, 20), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-a", Now, 10), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-a\0", Now, 10), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-A", Now, 10), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-earliest", Now, 5), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-expired", Now, 1), Now);
        await fixture.Store.TryClaimAsync(Claim("node-a", "wf-boundary", Now, 2), Now);
        await fixture.Store.TryClaimAsync(Claim("node-b", "wf-other", Now, 20), Now);
        var leases = await fixture.Store.ListOwnedAsync(new("node-a", Now.AddSeconds(2), 4));

        Assert.Collection(leases,
            lease => Assert.Equal("wf-earliest", lease.WorkflowExecutionId),
            lease => Assert.Equal("wf-A", lease.WorkflowExecutionId),
            lease => Assert.Equal("wf-a", lease.WorkflowExecutionId),
            lease => Assert.Equal("wf-a\0", lease.WorkflowExecutionId));
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
    public async Task Privileged_scoped_access_is_refused_instead_of_reporting_no_lease()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var privileged = new EfExecutionPlacementStore(fixture.Context, new PrivilegedScopedAccessor("scope-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => privileged.FindAsync("wf-1").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => privileged.TryClaimAsync(Claim("node-a", "wf-1"), Now).AsTask());
        Assert.Null(await fixture.Store.FindAsync("wf-1"));
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
        await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() => fixture.Store.FindAsync("wf-projection").AsTask());

        row = await fixture.Context.PlacementLeases.SingleAsync();
        row.ScopeKeyHash = originalScopeHash;
        row.OwnerIdHash = "corrupt";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() => fixture.Store.FindAsync("wf-projection").AsTask());

        row = await fixture.Context.PlacementLeases.SingleAsync();
        row.OwnerIdHash = originalOwnerHash;
        row.WorkflowExecutionIdOrderKey = [0x01, 0x02];
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() => fixture.Store.ListOwnedAsync(new("node-a", Now)).AsTask());

        row = await fixture.Context.PlacementLeases.SingleAsync();
        row.WorkflowExecutionIdOrderKey = originalOrderKey;
        row.ExpiresAtOffsetMinutes = 14 * 60 + 1;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() => fixture.Store.FindAsync("wf-projection").AsTask());
        await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() => fixture.Store.ListOwnedAsync(new("node-a", Now)).AsTask());
    }

    [Theory]
    [InlineData("token")]
    [InlineData("revision")]
    [InlineData("expiry")]
    public async Task Corrupt_scalar_lease_state_fails_closed_before_claim_or_release_mutation(string corruption)
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var lease = (await fixture.Store.TryClaimAsync(Claim("node-a", "wf-scalar-corrupt"), Now)).Lease;
        var row = await fixture.Context.PlacementLeases.SingleAsync();
        switch (corruption)
        {
            case "token":
                row.PlacementToken = 0;
                break;
            case "revision":
                row.Revision = 0;
                break;
            case "expiry":
                row.ExpiresAtUtcTicks = row.AcquiredAt.UtcTicks;
                row.ExpiresAtOffsetMinutes = checked((int)row.AcquiredAt.Offset.TotalMinutes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() =>
            fixture.Store.TryClaimAsync(Claim("node-a", "wf-scalar-corrupt"), Now.AddSeconds(1)).AsTask());
        await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() =>
            fixture.Store.ReleaseAsync(lease).AsTask());
    }

    [Theory]
    [InlineData("finding", "wf-provider")]
    [InlineData("claiming", "wf-provider")]
    [InlineData("releasing", "wf-provider")]
    [InlineData("listing", null)]
    public async Task Provider_failures_are_normalized_for_every_operation_and_the_context_recovers(
        string operation,
        string? expectedIdentity)
    {
        var interceptor = new FailOnceProviderInterceptor(operation is "claiming" or "releasing");
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);
        var existing = operation == "releasing"
            ? (await fixture.Store.TryClaimAsync(Claim("node-a", "wf-provider"), Now)).Lease
            : null;
        interceptor.Arm();

        Func<Task> action = operation switch
        {
            "finding" => async () => _ = await fixture.Store.FindAsync("wf-provider"),
            "claiming" => async () => _ = await fixture.Store.TryClaimAsync(Claim("node-a", "wf-provider"), Now),
            "releasing" => () => fixture.Store.ReleaseAsync(existing!).AsTask(),
            "listing" => async () => _ = await fixture.Store.ListOwnedAsync(new("node-a", Now)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        var failure = await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(action);
        Assert.Equal(operation, failure.Operation);
        if (expectedIdentity is null)
        {
            Assert.StartsWith("scope:", failure.Identity, StringComparison.Ordinal);
            Assert.DoesNotContain("scope-a", failure.Identity, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(expectedIdentity, failure.Identity);
        }
        var providerCause = failure.InnerException is DbUpdateException updateFailure
            ? updateFailure.InnerException
            : failure.InnerException;
        Assert.IsType<SyntheticProviderException>(providerCause);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        if (existing is not null)
            AssertLeaseEqual(existing, await fixture.Store.FindAsync(existing.WorkflowExecutionId));

        var recovered = await fixture.Store.TryClaimAsync(Claim("node-a", $"wf-recovers-{operation}"), Now);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, recovered.Outcome);
        AssertLeaseEqual(recovered.Lease, await fixture.Store.FindAsync(recovered.Lease.WorkflowExecutionId));
    }

    [Fact]
    public async Task Claim_contention_exhaustion_is_typed_bounded_and_leaves_the_context_reusable()
    {
        var interceptor = new FailingSaveInterceptor(Contention);
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);

        var failure = await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() =>
            fixture.Store.TryClaimAsync(Claim("node-a", "wf-contention"), Now).AsTask());

        Assert.Equal("claiming", failure.Operation);
        Assert.Equal("wf-contention", failure.Identity);
        Assert.IsType<DbUpdateConcurrencyException>(failure.InnerException);
        Assert.Equal(8, interceptor.Attempts);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        Assert.Null(await fixture.Store.FindAsync("wf-contention"));
    }

    [Fact]
    public async Task Release_contention_exhaustion_is_typed_bounded_and_does_not_hide_a_live_lease()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var lease = (await fixture.Store.TryClaimAsync(Claim("node-a", "wf-release-contention"), Now)).Lease;
        var interceptor = new FailingSaveInterceptor(Contention);
        await using var contender = await fixture.ReopenAsync("scope-a", interceptor);

        var failure = await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() =>
            contender.Store.ReleaseAsync(lease).AsTask());

        Assert.Equal("releasing", failure.Operation);
        Assert.Equal(lease.WorkflowExecutionId, failure.Identity);
        Assert.IsType<DbUpdateConcurrencyException>(failure.InnerException);
        Assert.Equal(8, interceptor.Attempts);
        Assert.Empty(contender.Context.ChangeTracker.Entries());
        AssertLeaseEqual(lease, await fixture.Store.FindAsync(lease.WorkflowExecutionId));
    }

    [Theory]
    [InlineData("claiming")]
    [InlineData("releasing")]
    public async Task A_transient_conflict_wrapped_by_the_provider_execution_strategy_is_retried(string operation)
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var lease = (await fixture.Store.TryClaimAsync(Claim("node-a", "wf-wrapped-transient"), Now)).Lease;
        var interceptor = new FailingSaveInterceptor(() => WrappedByExecutionStrategy(new SqlException(Deadlock)), failures: 1);
        await using var contender = await fixture.ReopenAsync("scope-a", interceptor);

        if (operation == "claiming")
        {
            var renewed = await contender.Store.TryClaimAsync(Claim("node-a", "wf-wrapped-transient", Now.AddSeconds(1)), Now.AddSeconds(1));
            Assert.Equal(ExecutionPlacementClaimOutcome.Renewed, renewed.Outcome);
            AssertLeaseEqual(renewed.Lease, await fixture.Store.FindAsync(lease.WorkflowExecutionId));
        }
        else
        {
            await contender.Store.ReleaseAsync(lease);
            Assert.Null(await fixture.Store.FindAsync(lease.WorkflowExecutionId));
        }

        Assert.Equal(2, interceptor.Attempts);
    }

    [Fact]
    public async Task Wrapped_transient_contention_exhausts_the_pinned_budget()
    {
        var interceptor = new FailingSaveInterceptor(() => WrappedByExecutionStrategy(new SqlException(Deadlock)));
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);

        var failure = await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() =>
            fixture.Store.TryClaimAsync(Claim("node-a", "wf-wrapped-contention"), Now).AsTask());

        Assert.Equal("claiming", failure.Operation);
        Assert.Contains("8 bounded compare-and-swap attempts", failure.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal(8, interceptor.Attempts);
        Assert.Null(await fixture.Store.FindAsync("wf-wrapped-contention"));
    }

    [Fact]
    public async Task A_wrapped_provider_failure_that_is_not_a_transient_conflict_fails_without_a_retry()
    {
        var interceptor = new FailingSaveInterceptor(() => WrappedByExecutionStrategy(new SyntheticProviderException()));
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);

        var failure = await Assert.ThrowsAsync<ExecutionPlacementEntityFrameworkPersistenceException>(() =>
            fixture.Store.TryClaimAsync(Claim("node-a", "wf-wrapped-failure"), Now).AsTask());

        Assert.Equal("claiming", failure.Operation);
        Assert.DoesNotContain("bounded compare-and-swap attempts", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, interceptor.Attempts);
    }

    private static DbUpdateConcurrencyException Contention() => new("Synthetic placement contention.");

    /// <summary>
    /// The shape SQL Server's default execution strategy gives a save that failed with an error it treats as transient.
    /// </summary>
    private static InvalidOperationException WrappedByExecutionStrategy(DbException providerError) =>
        new("An exception has been raised that is likely due to a transient failure.",
            new DbUpdateException("An error occurred while saving the entity changes.", providerError));

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

        public static async Task<Fixture> CreateAsync(string? scope, IInterceptor? interceptor = null)
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

        public async Task<Fixture> ReopenAsync(string? nextScope, IInterceptor? interceptor = null)
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

    private sealed class FailOnceProviderInterceptor(bool mutationsOnly) : DbCommandInterceptor
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
            (!mutationsOnly || IsMutationCommand(command.CommandText)) &&
            Volatile.Read(ref armed) == 1 &&
            Interlocked.Exchange(ref fired, 1) == 0;
    }

    /// <summary>Fails the first <paramref name="failures"/> saves with <paramref name="failure"/>, then lets saves through.</summary>
    private sealed class FailingSaveInterceptor(Func<Exception> failure, int failures = int.MaxValue) : SaveChangesInterceptor
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref attempts) <= failures ? throw failure() : ValueTask.FromResult(result);
    }

    private sealed class SyntheticProviderException() : DbException("synthetic provider failure")
    {
    }

    /// <summary>Carries a SQL Server error number the way the shared classifier reads it, by type name and <c>Number</c>.</summary>
    private sealed class SqlException(int number) : DbException($"synthetic SQL Server error {number}")
    {
        public int Number { get; } = number;
    }

    private static bool IsMutationCommand(string commandText)
    {
        var sql = commandText.AsSpan().TrimStart();
        return sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
               sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
               sql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
    }
}
