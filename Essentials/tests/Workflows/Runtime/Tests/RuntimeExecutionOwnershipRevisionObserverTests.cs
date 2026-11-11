using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// What the ownership service tells its <see cref="IOwnershipRevisionObserver"/>: the revision at which the ownership
/// row holds a lease it wrote, and when a lease is given up. The EF checkpoint store keeps its fence cursors from this.
/// </summary>
public sealed class RuntimeExecutionOwnershipRevisionObserverTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Acquire_reports_the_lease_at_the_revision_it_wrote()
    {
        var ownership = NewOwnership(out var store, out var observer, out _);

        var first = await ownership.AcquireAsync(WorkflowExecutionId);
        var second = await ownership.AcquireAsync(WorkflowExecutionId);

        Assert.Equal(
            [new Report(store, "held", first.LeaseId, first.FencingToken, first.ExpiresAt, 1),
             new Report(store, "held", second.LeaseId, second.FencingToken, second.ExpiresAt, 2)],
            observer.Reports);
        Assert.Equal(2, (await store.FindVersionedAsync(WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(WorkflowExecutionId)))!.Revision);
    }

    [Fact]
    public async Task Heartbeat_reports_the_renewed_lease_at_the_next_revision()
    {
        var ownership = NewOwnership(out var store, out var observer, out var clock);
        var lease = await ownership.AcquireAsync(WorkflowExecutionId);
        observer.Reports.Clear();
        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.True((await ownership.HeartbeatAsync(lease)).Succeeded);

        // The renewed expiry, not the one the caller's lease object still carries.
        Assert.Equal([new Report(store, "held", lease.LeaseId, lease.FencingToken, lease.ExpiresAt.AddSeconds(20), 2)], observer.Reports);
    }

    [Theory]
    [InlineData("taken-over")]
    [InlineData("expired")]
    public async Task A_refused_heartbeat_reports_nothing(string because)
    {
        var ownership = NewOwnership(out _, out var observer, out var clock);
        var lease = await ownership.AcquireAsync(WorkflowExecutionId);
        if (because == "taken-over")
            await ownership.AcquireAsync(WorkflowExecutionId);
        else
            clock.Advance(TimeSpan.FromMinutes(2));
        observer.Reports.Clear();

        Assert.False((await ownership.HeartbeatAsync(lease)).Succeeded);

        Assert.Empty(observer.Reports);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("taken-over")]
    public async Task Release_reports_the_lease_as_ended_whatever_the_release_finds(string leaseIs)
    {
        var ownership = NewOwnership(out var store, out var observer, out _);
        var lease = await ownership.AcquireAsync(WorkflowExecutionId);
        if (leaseIs == "taken-over")
            await ownership.AcquireAsync(WorkflowExecutionId);
        observer.Reports.Clear();

        var released = await ownership.ReleaseAsync(lease);

        Assert.Equal(leaseIs == "current", released.Succeeded);
        Assert.Equal([new Report(store, "ended", lease.LeaseId, lease.FencingToken, lease.ExpiresAt, null)], observer.Reports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Borrow_reports_the_borrowed_lease_and_return_reports_it_ended(bool fromAnOwner)
    {
        var ownership = NewOwnership(out var store, out var observer, out _);
        if (fromAnOwner)
            await ownership.AcquireAsync(WorkflowExecutionId);
        observer.Reports.Clear();

        var borrow = await ownership.BorrowAsync(WorkflowExecutionId);
        var revision = fromAnOwner ? 2 : 1;
        Assert.Equal([new Report(store, "held", borrow.Lease.LeaseId, borrow.Lease.FencingToken, borrow.Lease.ExpiresAt, revision)], observer.Reports);
        observer.Reports.Clear();

        Assert.True((await ownership.ReturnAsync(borrow)).Succeeded);

        // A lease left standing in for its owner is under a new fencing token, and nothing commits with it.
        Assert.All(observer.Reports, report => Assert.Equal(
            new Report(store, "ended", borrow.Lease.LeaseId, borrow.Lease.FencingToken, borrow.Lease.ExpiresAt, null), report));
        Assert.NotEmpty(observer.Reports);
    }

    [Fact]
    public async Task Without_an_observer_the_service_works_as_before()
    {
        var ownership = new RuntimeExecutionOwnershipService(new InMemoryExecutionLivenessStateStore(), new FakeTimeProvider(Now), Options());

        var lease = await ownership.AcquireAsync(WorkflowExecutionId);

        Assert.True((await ownership.HeartbeatAsync(lease)).Succeeded);
        Assert.True((await ownership.ReleaseAsync(lease)).Succeeded);
    }

    private static RuntimeExecutionOwnershipService NewOwnership(
        out InMemoryExecutionLivenessStateStore store, out RecordingObserver observer, out FakeTimeProvider clock)
    {
        store = new InMemoryExecutionLivenessStateStore();
        observer = new RecordingObserver();
        clock = new FakeTimeProvider(Now);
        return new RuntimeExecutionOwnershipService(store, clock, Options()) { RevisionObserver = observer };
    }

    private static RuntimeExecutionOwnershipOptions Options() => new() { OwnerId = "owner-under-test", LeaseDuration = TimeSpan.FromMinutes(1) };

    private sealed record Report(IExecutionLivenessStateStore Rows, string What, string LeaseId, long FencingToken, DateTimeOffset ExpiresAt, long? Revision);

    private sealed class RecordingObserver : IOwnershipRevisionObserver
    {
        public List<Report> Reports { get; } = [];

        public void LeaseHeld(IExecutionLivenessStateStore rows, RuntimeExecutionLease lease, long revision) =>
            Reports.Add(new(rows, "held", lease.LeaseId, lease.FencingToken, lease.ExpiresAt, revision));

        public void LeaseEnded(IExecutionLivenessStateStore rows, RuntimeExecutionLease lease) =>
            Reports.Add(new(rows, "ended", lease.LeaseId, lease.FencingToken, lease.ExpiresAt, null));
    }
}
