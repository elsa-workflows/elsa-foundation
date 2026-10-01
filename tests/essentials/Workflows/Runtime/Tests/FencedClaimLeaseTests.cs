using Elsa.Workflows.Runtime.Services.Claims;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The shared claim rule (#2195): a side effect runs only under a claim renewed immediately before it, and a claim lost
/// while it runs cancels it and is reported as lost, never as a cancellation of the caller or a failure of the row.
/// </summary>
public sealed class FencedClaimLeaseTests
{
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(20);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly Claim _claim = new("row-1", Version: 1);
    private readonly Renewals _renewals = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_claim_that_cannot_be_renewed_is_never_acted_on(bool renewWhileRunning)
    {
        _renewals.LoseFrom(1);
        var sideEffectRan = false;

        var run = await Lease(renewWhileRunning).RunAsync(_claim, _ =>
        {
            sideEffectRan = true;
            return ValueTask.FromResult(true);
        });

        Assert.False(sideEffectRan);
        Assert.Equal(FencedClaimRunStatus.LostBeforeSideEffect, run.Status);
        Assert.True(run.ClaimLost);
        Assert.Same(_claim, run.Claim);
        Assert.Null(run.Exception);
    }

    [Fact]
    public async Task A_renewal_that_throws_leaves_the_row_alone_and_carries_the_cause()
    {
        var outage = new InvalidOperationException("store unavailable");
        _renewals.ThrowOn(1, outage);
        var sideEffectRan = false;

        var run = await Lease(renewWhileRunning: false).RunAsync(_claim, _ =>
        {
            sideEffectRan = true;
            return ValueTask.FromResult(true);
        });

        Assert.False(sideEffectRan);
        Assert.Equal(FencedClaimRunStatus.LostBeforeSideEffect, run.Status);
        Assert.Same(outage, run.Exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_side_effect_runs_under_the_claim_renewed_for_it(bool renewWhileRunning)
    {
        var run = await Lease(renewWhileRunning).RunAsync(_claim, _ => ValueTask.FromResult(42));

        Assert.Equal(FencedClaimRunStatus.Completed, run.Status);
        Assert.Equal(42, run.Result);
        Assert.Equal(2, run.Claim.Version);
        Assert.Equal(1, _renewals.Count);
    }

    [Fact]
    public async Task A_claim_lost_while_the_side_effect_runs_cancels_it_and_is_reported_as_lost()
    {
        _renewals.LoseFrom(2);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = Lease(renewWhileRunning: true).RunAsync(_claim, async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        });
        await started.Task;
        _clock.Advance(Cadence);
        var run = await running;

        Assert.Equal(FencedClaimRunStatus.LostDuringSideEffect, run.Status);
        Assert.True(run.ClaimLost);
        Assert.Equal(2, run.Claim.Version);
        Assert.Null(run.Exception);
    }

    [Fact]
    public async Task Cancelling_the_caller_is_cancellation_not_a_lost_claim()
    {
        using var shutdown = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = Lease(renewWhileRunning: true).RunAsync(_claim, async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }, shutdown.Token);
        await started.Task;
        await shutdown.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
        Assert.Equal(1, _renewals.Count);
    }

    [Fact]
    public async Task A_side_effect_that_throws_under_a_held_claim_is_faulted_with_the_latest_claim()
    {
        var failure = new InvalidOperationException("dispatch failed");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = Lease(renewWhileRunning: true).RunAsync<bool>(_claim, async _ =>
        {
            started.SetResult();
            await release.Task;
            throw failure;
        });
        await started.Task;
        _clock.Advance(Cadence);
        await _renewals.WaitForAsync(2);
        release.SetResult();
        var run = await running;

        Assert.Equal(FencedClaimRunStatus.Faulted, run.Status);
        Assert.Same(failure, run.Exception);
        Assert.Equal(3, run.Claim.Version);
    }

    private FencedClaimLease<Claim> Lease(bool renewWhileRunning) =>
        new(_renewals.RenewAsync, VisibilityTimeout, _clock, renewWhileRunning);

    private sealed record Claim(string RowId, int Version);

    /// <summary>Renews by bumping the claim's version, until told to report the claim lost or to throw.</summary>
    private sealed class Renewals
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource> _waiters = [];
        private int _loseFrom = int.MaxValue;
        private (int Call, Exception Exception)? _throw;

        public int Count { get; private set; }

        public void LoseFrom(int call) => _loseFrom = call;

        public void ThrowOn(int call, Exception exception) => _throw = (call, exception);

        public ValueTask<Claim?> RenewAsync(Claim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken)
        {
            int call;
            lock (_gate)
            {
                call = ++Count;
                foreach (var waiter in _waiters)
                    waiter.TrySetResult();
            }

            if (_throw is { } scripted && scripted.Call == call)
                throw scripted.Exception;
            return ValueTask.FromResult(call >= _loseFrom ? null : claim with { Version = claim.Version + 1 });
        }

        public Task WaitForAsync(int calls)
        {
            lock (_gate)
            {
                if (Count >= calls)
                    return Task.CompletedTask;
                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(waiter);
                return waiter.Task.ContinueWith(_ => WaitForAsync(calls)).Unwrap();
            }
        }
    }
}
