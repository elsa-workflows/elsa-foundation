using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Samples.Compensation.Activities;
using Elsa.Samples.Compensation.Ledger;
using Xunit;

namespace Elsa.Samples.Compensation.Tests;

/// <summary>
/// The sample participant against the compensation participant contract (spec 251 FR-220), driven through the two
/// activities the way the runtime calls them: a repeat with one operation key, a release before its reservation (a
/// tombstone) and the late request it refuses, an uncertain outcome, failing attempts, and the step-result check.
/// </summary>
public sealed class ParticipantContractTests : IDisposable
{
    private const string Run = "run-1";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "elsa-compensation-sample-" + Guid.NewGuid().ToString("N"));
    private readonly ReservationLedger _ledger;

    public ParticipantContractTests() => _ledger = new ReservationLedger(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_repeated_reservation_returns_the_original_and_applies_once()
    {
        var first = await ReserveAsync("inventory", "reserve-key");
        var repeat = await ReserveAsync("inventory", "reserve-key", invocationId: "second-invocation");
        var other = await ReserveAsync("inventory", "other-key");

        Assert.Equal("inventory-1", Completed(first).Reservation);
        Assert.Equal("inventory-1", Completed(repeat).Reservation);
        Assert.Equal("inventory-2", Completed(other).Reservation);
        var ledger = await _ledger.ReadAsync("inventory");
        Assert.Equal(["reserve-key", "other-key"], ledger.Reservations.Select(reservation => reservation.OperationKey));
        Assert.All(ledger.Reservations, reservation => Assert.Equal(ReservationStatus.Reserved, reservation.Status));
    }

    [Fact]
    public async Task A_release_undoes_its_target_and_a_repeated_release_changes_nothing()
    {
        var reservation = Completed(await ReserveAsync("delivery", "reserve-key")).Reservation;

        Assert.Equal(ActivityTransitionKind.Complete, (await ReleaseAsync("delivery", "release-key", "reserve-key", reservation)).Kind);
        Assert.Equal(ActivityTransitionKind.Complete, (await ReleaseAsync("delivery", "release-key", "reserve-key", reservation, invocationId: "again")).Kind);

        var ledger = await _ledger.ReadAsync("delivery");
        var released = Assert.Single(ledger.Reservations);
        Assert.Equal((ReservationStatus.Released, "release-key"), (released.Status, released.ReleasedBy));
        var release = Assert.Single(ledger.Releases);
        Assert.Equal((ReleaseStatus.Released, "reserve-key", reservation), (release.Result, release.TargetOperationKey, release.ReservationId));
        Assert.Empty(ledger.Tombstones);
    }

    [Fact]
    public async Task A_release_before_its_reservation_leaves_a_tombstone_that_refuses_the_late_request()
    {
        Assert.Equal(ActivityTransitionKind.Complete, (await ReleaseAsync("delivery", "release-key", "reserve-key")).Kind);

        var late = await ReserveAsync("delivery", "reserve-key");

        Assert.Equal(Reserve.RefusedCode, Faulted(late).Code);
        var ledger = await _ledger.ReadAsync("delivery");
        Assert.Empty(ledger.Reservations);
        var tombstone = Assert.Single(ledger.Tombstones);
        Assert.Equal(("reserve-key", "release-key"), (tombstone.OperationKey, tombstone.ReleasedBy));
        Assert.Equal(ReleaseStatus.Tombstoned, Assert.Single(ledger.Releases).Result);
        // Another key is not affected by the tombstone.
        Assert.Equal("delivery-1", Completed(await ReserveAsync("delivery", "other-key")).Reservation);
    }

    [Fact]
    public async Task A_lost_response_applies_the_reservation_and_reports_an_unknown_outcome_that_its_release_undoes()
    {
        var lost = await ReserveAsync("delivery", "reserve-key", loseResponse: true);

        var fault = Faulted(lost);
        Assert.Equal((Reserve.TimeoutCode, "outcome-unknown"), (fault.Code, fault.Category));
        Assert.Equal(ReservationStatus.Reserved, Assert.Single((await _ledger.ReadAsync("delivery")).Reservations).Status);

        // An uncertain step returns no result: the release works from the target operation key alone.
        Assert.Equal(ActivityTransitionKind.Complete, (await ReleaseAsync("delivery", "release-key", "reserve-key")).Kind);
        Assert.Equal(ReservationStatus.Released, Assert.Single((await _ledger.ReadAsync("delivery")).Reservations).Status);
    }

    [Fact]
    public async Task A_rejected_request_faults_with_no_effect()
    {
        var rejected = await ReserveAsync("order", "confirm-key", reject: true);

        Assert.Equal(Reserve.RejectedCode, Faulted(rejected).Code);
        Assert.False(File.Exists(_ledger.FilePath("order")));
    }

    [Fact]
    public async Task Fail_times_fails_that_many_calls_of_one_operation_key_before_any_effect()
    {
        Completed(await ReserveAsync("delivery", "reserve-key"));

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var failed = await ReleaseAsync("delivery", "release-key", "reserve-key", failTimes: 2, invocationId: $"attempt-{attempt}");
            var fault = Faulted(failed);
            Assert.Equal(Release.UnavailableCode, fault.Code);
            Assert.True(fault.IsRetryable);
            Assert.Equal(ReservationStatus.Reserved, Assert.Single((await _ledger.ReadAsync("delivery")).Reservations).Status);
        }

        Assert.Equal(ActivityTransitionKind.Complete, (await ReleaseAsync("delivery", "release-key", "reserve-key", failTimes: 2, invocationId: "attempt-3")).Kind);
        var ledger = await _ledger.ReadAsync("delivery");
        Assert.Equal(ReservationStatus.Released, Assert.Single(ledger.Reservations).Status);
        Assert.Equal(2, ledger.FailedReleaseAttempts["release-key"].Count);
        // A release that is done stays done, even if it would still have failures to spend.
        Assert.Equal(ActivityTransitionKind.Complete, (await ReleaseAsync("delivery", "release-key", "reserve-key", failTimes: 5)).Kind);
        Assert.Single((await _ledger.ReadAsync("delivery")).Releases);
    }

    [Fact]
    public async Task A_release_given_a_reservation_its_target_did_not_make_is_refused_with_no_effect()
    {
        Completed(await ReserveAsync("stock-a", "reserve-a"));
        var other = Completed(await ReserveAsync("stock-a", "reserve-other")).Reservation;

        var mismatch = await ReleaseAsync("stock-a", "release-a", "reserve-a", reservation: other);
        var unknown = await ReleaseAsync("stock-b", "release-b", "reserve-b", reservation: "stock-b-1");

        Assert.Equal(Release.MismatchCode, Faulted(mismatch).Code);
        Assert.Equal(Release.MismatchCode, Faulted(unknown).Code);
        var ledger = await _ledger.ReadAsync("stock-a");
        Assert.All(ledger.Reservations, reservation => Assert.Equal(ReservationStatus.Reserved, reservation.Status));
        Assert.Empty(ledger.Releases);
        Assert.False(File.Exists(_ledger.FilePath("stock-b")));
    }

    [Fact]
    public async Task A_release_outside_a_scope_releases_the_reservation_it_is_given_and_needs_one()
    {
        var reservation = Completed(await ReserveAsync("payment", "charge-key")).Reservation;

        Assert.Equal(Release.NoTargetCode, Faulted(await ReleaseAsync("payment", "refund-key", targetOperationKey: null)).Code);
        Assert.Equal(ActivityTransitionKind.Complete, (await ReleaseAsync("payment", "refund-key", targetOperationKey: null, reservation: reservation)).Kind);

        Assert.Equal(ReservationStatus.Released, Assert.Single((await _ledger.ReadAsync("payment")).Reservations).Status);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(".hidden")]
    [InlineData("")]
    [InlineData("a/b")]
    public async Task A_resource_that_is_not_one_file_name_is_refused(string resource)
    {
        Assert.Equal(Reserve.InvalidResourceCode, Faulted(await ReserveAsync(resource, "key")).Code);
        Assert.Equal(Release.InvalidResourceCode, Faulted(await ReleaseAsync(resource, "key", "target")).Code);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task Concurrent_calls_on_one_resource_lose_no_write_and_leave_no_temporary_file()
    {
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => ReserveAsync("inventory", $"key-{index}").AsTask()));

        Assert.Equal(20, (await _ledger.ReadAsync("inventory")).Reservations.Select(reservation => reservation.ReservationId).Distinct().Count());
        Assert.Equal(["inventory.json"], Directory.EnumerateFiles(_directory).Select(Path.GetFileName));
    }

    private ValueTask<ActivityTransition> ReserveAsync(string resource, string operationKey, bool reject = false, bool loseResponse = false,
        string invocationId = "invocation") =>
        ((IActivity)new Reserve(_ledger) { Resource = resource, Reject = reject, LoseResponse = loseResponse })
        .ExecuteAsync(Context("reserve", invocationId, operationKey, targetOperationKey: null));

    private ValueTask<ActivityTransition> ReleaseAsync(string resource, string operationKey, string? targetOperationKey, string? reservation = null,
        int? failTimes = null, string invocationId = "invocation") =>
        ((IActivity)new Release(_ledger) { Resource = resource, Reservation = reservation, FailTimes = failTimes })
        .ExecuteAsync(Context("release", invocationId, operationKey, targetOperationKey));

    private static ActivityExecutionContext Context(string nodeId, string invocationId, string operationKey, string? targetOperationKey) =>
        new(Run, invocationId, "attempt", nodeId, CancellationToken.None, null, null, operationKey, targetOperationKey);

    private static ReservationResult Completed(ActivityTransition transition) =>
        Assert.IsAssignableFrom<IActivityCompletionTransition<ReservationResult>>(transition).Result;

    private static ActivityFault Faulted(ActivityTransition transition) =>
        Assert.IsAssignableFrom<IActivityFaultTransition>(transition).Fault;
}
