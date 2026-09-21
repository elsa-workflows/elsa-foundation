using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed class EfIdentityMutationReceiptCleanupCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Cleanup_runs_immediately_then_on_the_attempt_or_time_boundary()
    {
        var coordinator = new EfIdentityMutationReceiptCleanupCoordinator();
        var calls = 0;

        Task Cleanup(CancellationToken _)
        {
            calls++;
            return Task.CompletedTask;
        }

        await coordinator.RunIfDueAsync("scope", Now, Interval, 3, Cleanup, CancellationToken.None);
        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(1), Interval, 3, Cleanup, CancellationToken.None);
        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(1), Interval, 3, Cleanup, CancellationToken.None);
        Assert.Equal(1, calls);

        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(1), Interval, 3, Cleanup, CancellationToken.None);
        Assert.Equal(2, calls);

        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(2), Interval, 3, Cleanup, CancellationToken.None);
        Assert.Equal(2, calls);

        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(6), Interval, 3, Cleanup, CancellationToken.None);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Cleanup_windows_are_independent_and_each_scope_is_serialized()
    {
        var coordinator = new EfIdentityMutationReceiptCleanupCoordinator();
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var waitingCancellation = new CancellationTokenSource();
        var scopeAActive = 0;
        var scopeAMaxActive = 0;
        var cancelledWaiterCalls = 0;

        async Task FirstCleanup(CancellationToken _)
        {
            var active = Interlocked.Increment(ref scopeAActive);
            scopeAMaxActive = Math.Max(scopeAMaxActive, active);
            firstEntered.SetResult(true);
            await releaseFirst.Task;
            Interlocked.Decrement(ref scopeAActive);
        }

        Task FinalCleanup(CancellationToken _)
        {
            var active = Interlocked.Increment(ref scopeAActive);
            scopeAMaxActive = Math.Max(scopeAMaxActive, active);
            Interlocked.Decrement(ref scopeAActive);
            return Task.CompletedTask;
        }

        var first = coordinator.RunIfDueAsync("scope-a", Now, Interval, 3, FirstCleanup, CancellationToken.None);
        await firstEntered.Task;
        var cancelledWaiter = coordinator.RunIfDueAsync(
            "scope-a",
            Now.Add(Interval),
            Interval,
            3,
            _ =>
            {
                cancelledWaiterCalls++;
                return Task.CompletedTask;
            },
            waitingCancellation.Token);
        Assert.False(cancelledWaiter.IsCompleted);

        var scopeBCalls = 0;
        var otherScope = coordinator.RunIfDueAsync(
            "scope-b",
            Now,
            Interval,
            3,
            _ =>
            {
                scopeBCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Assert.True(otherScope.IsCompletedSuccessfully);

        waitingCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);
        Assert.Equal(0, cancelledWaiterCalls);

        var final = coordinator.RunIfDueAsync("scope-a", Now.Add(Interval), Interval, 3, FinalCleanup, CancellationToken.None);
        Assert.False(final.IsCompleted);
        releaseFirst.SetResult(true);
        await Task.WhenAll(first, final, otherScope);
        Assert.Equal(1, scopeAMaxActive);
        Assert.Equal(1, scopeBCalls);
    }

    [Fact]
    public async Task Failed_cleanup_starts_a_fresh_bounded_retry_window()
    {
        var coordinator = new EfIdentityMutationReceiptCleanupCoordinator();
        var calls = 0;
        var failNext = false;

        Task Cleanup(CancellationToken _)
        {
            calls++;
            if (failNext)
            {
                failNext = false;
                throw new InvalidOperationException("provider unavailable");
            }
            return Task.CompletedTask;
        }

        await coordinator.RunIfDueAsync("scope", Now, Interval, 3, Cleanup, CancellationToken.None);
        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(1), Interval, 3, Cleanup, CancellationToken.None);
        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(1), Interval, 3, Cleanup, CancellationToken.None);
        Assert.Equal(1, calls);

        failNext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.RunIfDueAsync("scope", Now.AddMinutes(1), Interval, 3, Cleanup, CancellationToken.None));
        Assert.Equal(2, calls);

        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(2), Interval, 3, Cleanup, CancellationToken.None);
        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(2), Interval, 3, Cleanup, CancellationToken.None);
        Assert.Equal(2, calls);

        await coordinator.RunIfDueAsync("scope", Now.AddMinutes(2), Interval, 3, Cleanup, CancellationToken.None);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Cancellation_from_cleanup_releases_the_gate_and_applies_backoff()
    {
        var coordinator = new EfIdentityMutationReceiptCleanupCoordinator();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.RunIfDueAsync(
                "scope",
                Now,
                Interval,
                3,
                token =>
                {
                    calls++;
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                cancellation.Token));

        await coordinator.RunIfDueAsync(
            "scope",
            Now.Add(Interval),
            Interval,
            3,
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Assert.Equal(2, calls);
    }
}
