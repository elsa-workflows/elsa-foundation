using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfWriteRetryTests : IAsyncDisposable
{
    private readonly RetryContext context = new();
    private readonly EfWriteRetry retry = new(4, EfWriteConflict.Concurrency | EfWriteConflict.Transient);
    private readonly List<Exception?> exhaustedWith = [];
    private int attempts;

    public ValueTask DisposeAsync() => context.DisposeAsync();

    [Fact]
    public void Budget_must_allow_one_attempt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EfWriteRetry(0, EfWriteConflict.Concurrency));
    }

    [Fact]
    public async Task A_settled_attempt_returns_its_value_without_retrying()
    {
        Assert.Equal("settled", await RunAsync(() => "settled"));
        Assert.Equal(1, attempts);
        Assert.Empty(exhaustedWith);
    }

    [Fact]
    public async Task A_retryable_race_is_retried_until_an_attempt_settles()
    {
        Assert.Equal("settled", await RunAsync(() => attempts < 3 ? throw Concurrency() : "settled"));
        Assert.Equal(3, attempts);
        Assert.Empty(exhaustedWith);
    }

    [Fact]
    public async Task Exhaustion_runs_the_whole_budget_and_hands_the_last_race_to_the_call_site()
    {
        var races = new List<Exception>();

        var result = await RunAsync(() =>
        {
            races.Add(Concurrency());
            throw races[^1];
        });

        Assert.Equal("exhausted", result);
        Assert.Equal(4, attempts);
        Assert.Same(races[^1], Assert.Single(exhaustedWith));
    }

    [Fact]
    public async Task An_exception_the_retry_does_not_classify_propagates_from_the_first_attempt()
    {
        var failure = new DbUpdateException("constraint", new SqliteException(19));

        Assert.Same(failure, await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(() => throw failure).AsTask()));
        Assert.Equal(1, attempts);
        Assert.Empty(exhaustedWith);
    }

    [Fact]
    public async Task Only_the_declared_conflicts_are_retried()
    {
        var uniqueKey = new DbUpdateException("duplicate", new SqliteException(19, 2067));

        await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(() => throw uniqueKey).AsTask());
        Assert.Equal(1, attempts);

        var uniqueRetry = new EfWriteRetry(4, EfWriteConflict.UniqueKey);
        Assert.True(uniqueRetry.ShouldRetry(null, uniqueKey));
        Assert.False(uniqueRetry.ShouldRetry(null, new InvalidOperationException("wrapper", uniqueKey)));
        Assert.False(uniqueRetry.ShouldRetry(null, Concurrency()));
    }

    [Fact]
    public async Task A_custom_predicate_decides_what_is_retried()
    {
        var custom = new EfWriteRetry(3, exception => exception is TimeoutException);

        var result = await custom.RunAsync(
            context,
            () => ++attempts < 3 ? throw new TimeoutException() : ValueTask.FromResult("settled"),
            _ => ValueTask.FromResult("exhausted"),
            CancellationToken.None);

        Assert.Equal("settled", result);
        Assert.Equal(3, attempts);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => custom.RunAsync(
            context,
            () => throw Concurrency(),
            _ => ValueTask.FromResult("exhausted"),
            CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Cancellation_is_never_retried_even_when_the_predicate_accepts_it()
    {
        var accepting = new EfWriteRetry(4, _ => true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => accepting.RunAsync(
            context,
            () =>
            {
                attempts++;
                throw new OperationCanceledException();
            },
            _ => ValueTask.FromResult("exhausted"),
            CancellationToken.None).AsTask());

        Assert.Equal(1, attempts);
        Assert.False(accepting.ShouldRetry(null, new OperationCanceledException()));
    }

    [Fact]
    public async Task The_backoff_follows_each_failed_attempt_except_the_last()
    {
        var delays = new List<int>();
        var delayed = new EfWriteRetry(4, EfWriteConflict.Concurrency, number =>
        {
            delays.Add(number);
            return TimeSpan.FromMilliseconds(1);
        });

        await delayed.RunAsync(context, () => throw Concurrency(), _ => ValueTask.CompletedTask, CancellationToken.None);

        Assert.Equal([1, 2, 3], delays);
    }

    [Fact]
    public async Task Cancellation_during_the_backoff_stops_the_loop()
    {
        using var cancellation = new CancellationTokenSource();
        var delayed = new EfWriteRetry(4, EfWriteConflict.Concurrency, _ => TimeSpan.FromMinutes(5));

        var run = delayed.RunAsync(
            context,
            () =>
            {
                attempts++;
                cancellation.Cancel();
                throw Concurrency();
            },
            _ => ValueTask.CompletedTask,
            cancellation.Token).AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task A_transient_conflict_inside_a_caller_transaction_is_rethrown_unchanged_instead_of_retried()
    {
        var transient = Transient();
        await using var transaction = await context.Database.BeginTransactionAsync();

        Assert.Same(transient, await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(() => throw transient).AsTask()));
        Assert.Equal(1, attempts);
        Assert.Empty(exhaustedWith);
        Assert.False(retry.ShouldRetry(context, transient));
    }

    [Fact]
    public async Task A_transient_conflict_without_a_caller_transaction_is_retried()
    {
        var result = await RunAsync(() => attempts < 2 ? throw Transient() : "settled");

        Assert.Equal("settled", result);
        Assert.Equal(2, attempts);
        Assert.True(retry.ShouldRetry(context, Transient()));
    }

    [Fact]
    public async Task A_concurrency_race_inside_a_caller_transaction_is_still_retried()
    {
        await using var transaction = await context.Database.BeginTransactionAsync();

        Assert.Equal("settled", await RunAsync(() => attempts < 2 ? throw Concurrency() : "settled"));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task An_attempt_that_owns_and_disposes_its_transaction_may_retry_a_transient_conflict()
    {
        var result = await RunAttemptAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            return attempts < 2 ? throw Transient() : "settled";
        });

        Assert.Equal("settled", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task An_explicit_retry_without_an_exception_starts_the_next_attempt()
    {
        var result = await retry.RunUntilSettledAsync<string>(
            context,
            () => ValueTask.FromResult(++attempts < 3 ? EfWriteAttempt<string>.Retry() : "settled"),
            Exhausted,
            CancellationToken.None);

        Assert.Equal("settled", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task An_explicit_attempt_is_exhausted_with_the_last_race_it_reported()
    {
        var race = Concurrency();

        var result = await retry.RunUntilSettledAsync<string>(
            context,
            () => ValueTask.FromResult(++attempts == 4 ? EfWriteAttempt<string>.Retry(race) : EfWriteAttempt<string>.Retry()),
            Exhausted,
            CancellationToken.None);

        Assert.Equal("exhausted", result);
        Assert.Equal(4, attempts);
        Assert.Same(race, Assert.Single(exhaustedWith));
    }

    [Fact]
    public async Task An_explicit_attempt_propagates_what_it_throws_even_when_it_is_retryable()
    {
        var race = Concurrency();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => retry.RunUntilSettledAsync<string>(
            context,
            () =>
            {
                attempts++;
                throw race;
            },
            Exhausted,
            CancellationToken.None).AsTask());

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task An_explicit_transient_retry_inside_a_caller_transaction_is_rethrown()
    {
        var transient = Transient();
        await using var transaction = await context.Database.BeginTransactionAsync();

        Assert.Same(transient, await Assert.ThrowsAsync<DbUpdateException>(() => retry.RunUntilSettledAsync<string>(
            context,
            () =>
            {
                attempts++;
                return ValueTask.FromResult(EfWriteAttempt<string>.Retry(transient));
            },
            Exhausted,
            CancellationToken.None).AsTask()));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task A_write_without_a_result_runs_through_the_same_loop()
    {
        await retry.RunAsync(
            context,
            () => ++attempts < 2 ? throw Concurrency() : ValueTask.CompletedTask,
            conflict =>
            {
                exhaustedWith.Add(conflict);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Empty(exhaustedWith);
    }

    private ValueTask<string> RunAsync(Func<string> attempt) =>
        RunAttemptAsync(() => ValueTask.FromResult(attempt()));

    private ValueTask<string> RunAttemptAsync(Func<ValueTask<string>> attempt) =>
        retry.RunAsync(
            context,
            () =>
            {
                attempts++;
                return attempt();
            },
            Exhausted,
            CancellationToken.None);

    private ValueTask<string> Exhausted(Exception? conflict)
    {
        exhaustedWith.Add(conflict);
        return ValueTask.FromResult("exhausted");
    }

    private static DbUpdateConcurrencyException Concurrency() => new("row version changed");

    private static DbUpdateException Transient() => new("database is locked", new SqliteException(5));

    private sealed class RetryContext() : DbContext(new DbContextOptionsBuilder<RetryContext>().UseSqlite("Data Source=:memory:").Options);

    private sealed class SqliteException(int errorCode, int extendedErrorCode = 0) : Exception
    {
        public int SqliteErrorCode { get; } = errorCode;
        public int SqliteExtendedErrorCode { get; } = extendedErrorCode;
    }
}
