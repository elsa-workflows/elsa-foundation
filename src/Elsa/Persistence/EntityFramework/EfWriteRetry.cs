using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The one bounded retry loop first-party EF stores run a compare-and-swap or otherwise race-prone write through. A
/// store declares its attempt budget, the lost races it retries, and an optional backoff; each call supplies one attempt
/// and what running out of attempts means for that store's contract. Every storage decision stays in the store: this
/// type counts attempts, waits between them, and enforces the rule below.
/// </summary>
/// <remarks>
/// A transient provider conflict is never retried while a transaction is still open on the store's context after the
/// attempt ended. An attempt disposes the transactions it begins, so an open one belongs to the caller, and the provider
/// may already have rolled it back: another attempt would run outside that transaction or inside an aborted one. The
/// conflict is rethrown unchanged, and the caller retries its whole unit.
/// </remarks>
public sealed class EfWriteRetry
{
    /// <summary>The attempt budget of every store whose budget no test or specification pins.</summary>
    public const int DefaultMaxAttempts = 16;

    private readonly Func<Exception, bool> isRetryable;
    private readonly Func<int, TimeSpan>? delay;

    /// <param name="maxAttempts">Attempts, the first included, before the call site's exhaustion outcome applies.</param>
    /// <param name="retryOn">The lost races a thrown exception must represent to be retried.</param>
    /// <param name="delay">The wait after the given failed attempt (numbered from 1) before the next one; none when omitted.</param>
    public EfWriteRetry(int maxAttempts, EfWriteConflict retryOn, Func<int, TimeSpan>? delay = null)
        : this(maxAttempts, exception => EfRelationalExceptionClassifier.IsWriteConflict(exception, retryOn), delay)
    {
    }

    /// <param name="maxAttempts">Attempts, the first included, before the call site's exhaustion outcome applies.</param>
    /// <param name="isRetryable">Whether a thrown exception is a lost race to retry.</param>
    /// <param name="delay">The wait after the given failed attempt (numbered from 1) before the next one; none when omitted.</param>
    public EfWriteRetry(int maxAttempts, Func<Exception, bool> isRetryable, Func<int, TimeSpan>? delay = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentNullException.ThrowIfNull(isRetryable);
        MaxAttempts = maxAttempts;
        this.isRetryable = isRetryable;
        this.delay = delay;
    }

    public int MaxAttempts { get; }

    /// <summary>
    /// Whether an attempt that failed with <paramref name="exception"/> may be retried: the exception is a retryable lost
    /// race, not a cancellation, and not a transient conflict inside a transaction still open on <paramref name="context"/>.
    /// An attempt that maps failures to its store's exceptions asks this first, so a refused conflict goes through that mapping.
    /// </summary>
    public bool ShouldRetry(DbContext? context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is not OperationCanceledException && isRetryable(exception) && !IsInCallerTransaction(context, exception);
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> until it returns. A thrown exception this retry classifies as a lost race starts the
    /// next attempt; any other exception propagates. When the budget runs out, <paramref name="exhausted"/> receives the
    /// last lost race and decides what the call returns or throws.
    /// </summary>
    /// <param name="context">The context the attempt writes through, or <c>null</c> when every attempt opens its own context, so no caller transaction can be in scope.</param>
    public ValueTask<T> RunAsync<T>(
        DbContext? context,
        Func<ValueTask<T>> attempt,
        Func<Exception?, ValueTask<T>> exhausted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return RunUntilSettledAsync<T>(
            context,
            async () =>
            {
                try
                {
                    return await attempt();
                }
                catch (Exception exception) when (exception is not OperationCanceledException && isRetryable(exception))
                {
                    return EfWriteAttempt<T>.Retry(exception);
                }
            },
            exhausted,
            cancellationToken);
    }

    /// <inheritdoc cref="RunAsync{T}"/>
    public async ValueTask RunAsync(
        DbContext? context,
        Func<ValueTask> attempt,
        Func<Exception?, ValueTask> exhausted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(exhausted);
        await RunAsync(
            context,
            async () =>
            {
                await attempt();
                return true;
            },
            async conflict =>
            {
                await exhausted(conflict);
                return false;
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> until it returns a settled value. Only an attempt that returns
    /// <see cref="EfWriteAttempt{T}.Retry"/> starts the next one; an exception it throws always propagates. Use this when
    /// the attempt itself decides, branch by branch, which races it lost.
    /// </summary>
    /// <param name="context">The context the attempt writes through, or <c>null</c> when every attempt opens its own context, so no caller transaction can be in scope.</param>
    public async ValueTask<T> RunUntilSettledAsync<T>(
        DbContext? context,
        Func<ValueTask<EfWriteAttempt<T>>> attempt,
        Func<Exception?, ValueTask<T>> exhausted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(exhausted);
        for (var number = 1; ; number++)
        {
            var outcome = await attempt();
            if (outcome.IsSettled)
                return outcome.Value;
            if (outcome.Conflict is { } conflict && IsInCallerTransaction(context, conflict))
                ExceptionDispatchInfo.Throw(conflict);
            if (number == MaxAttempts)
                return await exhausted(outcome.Conflict);
            if (delay is not null)
                await Task.Delay(delay(number), cancellationToken);
        }
    }

    private static bool IsInCallerTransaction(DbContext? context, Exception exception) =>
        context?.Database.CurrentTransaction is not null && EfRelationalExceptionClassifier.IsTransientWriteConflict(exception);
}

/// <summary>What one attempt run by <see cref="EfWriteRetry.RunUntilSettledAsync{T}"/> produced: a settled value, or a lost race to retry.</summary>
public readonly struct EfWriteAttempt<T>
{
    private EfWriteAttempt(bool isSettled, T value, Exception? conflict)
    {
        IsSettled = isSettled;
        Value = value;
        Conflict = conflict;
    }

    internal bool IsSettled { get; }

    internal T Value { get; }

    internal Exception? Conflict { get; }

    /// <summary>
    /// Asks for another attempt. Pass the exception that lost the race when there is one: exhaustion receives it, and a
    /// transient one inside a caller's transaction is rethrown instead of retried.
    /// </summary>
    public static EfWriteAttempt<T> Retry(Exception? conflict = null) => new(false, default!, conflict);

    public static implicit operator EfWriteAttempt<T>(T value) => new(true, value, null);
}
