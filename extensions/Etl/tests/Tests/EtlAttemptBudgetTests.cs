using Elsa.Etl.Runtime.Execution;

namespace Elsa.Etl.Tests;

public sealed class EtlAttemptBudgetTests
{
    private static readonly EtlAttemptCharge Capacity = new(10, 100, 200, 300);

    [Fact]
    public async Task Replaced_attempt_keeps_its_allowances_until_its_actual_task_unwinds()
    {
        var pool = new EtlAttemptBudget(2, Capacity);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unwind = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ActualAttempt()
        {
            await using var permit = await pool.AcquireAsync(Capacity);
            entered.SetResult();
            await unwind.Task;
        }
        var oldAttempt = ActualAttempt();
        Task<IAsyncDisposable> replacement;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The coordinator may cancel/replace this task, but cancellation is not task completion.
            replacement = pool.AcquireAsync(Capacity).AsTask();
            Assert.False(replacement.IsCompleted);
        }
        finally
        {
            unwind.TrySetResult();
            await oldAttempt;
        }
        await using var replacementPermit = await replacement.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(10, 0, 0, 0)]
    [InlineData(0, 100, 0, 0)]
    [InlineData(0, 0, 200, 0)]
    [InlineData(0, 0, 0, 300)]
    public async Task Each_logical_dimension_independently_prevents_over_admission(long records, long input, long lookup, long output)
    {
        var pool = new EtlAttemptBudget(2, Capacity);
        var charge = new EtlAttemptCharge(records, input, lookup, output);
        var first = await pool.AcquireAsync(charge);
        var next = pool.AcquireAsync(charge).AsTask();
        Assert.False(next.IsCompleted);
        await first.DisposeAsync();
        await first.DisposeAsync(); // A repeated cleanup cannot release another attempt's capacity.
        await using var second = await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_cancelled_waiter_does_not_consume_or_release_capacity()
    {
        var pool = new EtlAttemptBudget(1, Capacity);
        var first = await pool.AcquireAsync(default);
        using var cancel = new CancellationTokenSource();
        var waiter = pool.AcquireAsync(default, cancel.Token).AsTask();
        Assert.False(waiter.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        var following = pool.AcquireAsync(default).AsTask();
        Assert.False(following.IsCompleted);
        await first.DisposeAsync();
        await using var second = await following.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task An_impossible_charge_is_refused_instead_of_waiting_forever() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new EtlAttemptBudget(1, Capacity)
            .AcquireAsync(Capacity with { InputBytes = 101 }).AsTask());
}
