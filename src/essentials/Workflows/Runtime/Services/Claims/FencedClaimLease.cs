using System.Runtime.ExceptionServices;

namespace Elsa.Workflows.Runtime.Services.Claims;

/// <summary>
/// Renews a fenced claim on a claimable row. Returns the renewed claim, or <see langword="null"/> when the presented claim
/// no longer holds the row: another claimant re-claimed it, or it was completed or removed.
/// </summary>
public delegate ValueTask<TClaim?> FencedClaimRenewal<TClaim>(
    TClaim claim,
    DateTimeOffset now,
    TimeSpan visibilityTimeout,
    CancellationToken cancellationToken) where TClaim : class;

/// <summary>
/// The one rule every claim → side effect → fenced-complete loop over claimable rows follows (#2195): <b>renew the claim
/// immediately before the side effect</b>, and never run the side effect under a claim that could not be renewed.
/// </summary>
/// <remarks>
/// <para>
/// A pump that claims a batch of rows under one visibility timeout and then works through them one at a time can outlive
/// that timeout: the rows at the end of a long batch lapse, a peer re-claims them, and the original claimant would
/// otherwise act on them as well and then fail its completion. Renewing first turns that into a lost claim reported before
/// anything happened. The renewal is a compare-and-set on the claim's owner and fence, so it is the renewal, not the
/// visibility deadline, that makes the side effect exclusive: a claim that lapsed but that nobody re-claimed is still
/// renewed, and a re-claimed one never is.
/// </para>
/// <para>
/// <see cref="RunAsync{TResult}"/> renews before the side effect only. <see cref="RunRenewingAsync{TResult}"/> also
/// renews every third of the visibility timeout while the side effect runs, and a renewal that fails cancels the side
/// effect. Use the renewing entry point only when the side effect does not share the renewing store's unit of work: a
/// renewal running beside a side effect on the same <c>DbContext</c> would collide with it.
/// </para>
/// <para>
/// Completing or releasing the row stays with the caller, which presents <see cref="FencedClaimRun{TClaim,TResult}.Claim"/>
/// to its own fenced transition and treats a stale answer the way it treats a lost claim here: it logs the row and moves
/// on to the next one. One lost row never ends the caller's sweep.
/// </para>
/// <para>
/// Cancellation of the caller's token always propagates as <see cref="OperationCanceledException"/>. A side effect this
/// lease cancelled because its claim was lost is reported as <see cref="FencedClaimRunStatus.LostDuringSideEffect"/>,
/// never as a cancellation and never as a failure of the row.
/// </para>
/// </remarks>
public sealed class FencedClaimLease<TClaim> where TClaim : class
{
    private readonly FencedClaimRenewal<TClaim> _renew;
    private readonly TimeSpan _visibilityTimeout;
    private readonly TimeSpan _renewalCadence;
    private readonly TimeProvider _timeProvider;

    public FencedClaimLease(
        FencedClaimRenewal<TClaim> renew,
        TimeSpan visibilityTimeout,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(renew);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "A claim visibility timeout must be greater than zero.");

        _renew = renew;
        _visibilityTimeout = visibilityTimeout;
        _renewalCadence = TimeSpan.FromTicks(Math.Max(1, visibilityTimeout.Ticks / 3));
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Renews <paramref name="claim"/>, then runs <paramref name="sideEffect"/> once if the renewal held. Nothing renews the
    /// claim while the side effect runs.
    /// </summary>
    public async ValueTask<FencedClaimRun<TClaim, TResult>> RunAsync<TResult>(
        TClaim claim,
        Func<CancellationToken, ValueTask<TResult>> sideEffect,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sideEffect);
        var (renewed, lost) = await RenewBeforeSideEffectAsync<TResult>(claim, cancellationToken);
        if (lost is not null)
            return lost;

        try
        {
            return new(FencedClaimRunStatus.Completed, renewed!, await sideEffect(cancellationToken), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(FencedClaimRunStatus.Faulted, renewed!, default, exception);
        }
    }

    /// <summary>
    /// Renews <paramref name="claim"/>, then runs <paramref name="sideEffect"/> if the renewal held, renewing the claim
    /// every third of the visibility timeout until the side effect finishes. A renewal that fails cancels the side effect
    /// and the run reports <see cref="FencedClaimRunStatus.LostDuringSideEffect"/>.
    /// </summary>
    public async ValueTask<FencedClaimRun<TClaim, TResult>> RunRenewingAsync<TResult>(
        TClaim claim,
        Func<CancellationToken, ValueTask<TResult>> sideEffect,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sideEffect);
        var (renewed, lost) = await RenewBeforeSideEffectAsync<TResult>(claim, cancellationToken);
        if (lost is not null)
            return lost;

        var holding = new Holding(renewed!);
        using var stopRenewing = new CancellationTokenSource();
        using var sideEffectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewing = KeepRenewingAsync(holding, stopRenewing.Token, sideEffectCancellation);
        TResult? result = default;
        Exception? failure = null;
        try
        {
            result = await sideEffect(sideEffectCancellation.Token);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            await stopRenewing.CancelAsync();
            await renewing;
        }

        // The caller's own cancellation is shutdown, not an outcome of this row, so it wins over everything. Any other
        // cancellation the lease did not cause propagates as it would without a lease.
        if (failure is OperationCanceledException && (cancellationToken.IsCancellationRequested || !holding.Lost))
            ExceptionDispatchInfo.Capture(failure).Throw();

        // A lost claim outranks the side effect's own outcome, success included: the row now belongs to the next
        // claimant, so nothing this run did may be completed, released or recorded as a failure.
        if (holding.Lost)
            return new(FencedClaimRunStatus.LostDuringSideEffect, holding.Claim, default, holding.LossException);

        return failure is null
            ? new(FencedClaimRunStatus.Completed, holding.Claim, result, null)
            : new(FencedClaimRunStatus.Faulted, holding.Claim, default, failure);
    }

    private async ValueTask<(TClaim? Renewed, FencedClaimRun<TClaim, TResult>? Lost)> RenewBeforeSideEffectAsync<TResult>(
        TClaim claim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _renew(claim, _timeProvider.GetUtcNow(), _visibilityTimeout, cancellationToken) is { } renewed
                ? (renewed, null)
                : (null, new(FencedClaimRunStatus.LostBeforeSideEffect, claim, default, null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // An unconfirmed claim is not acted on. The row stays claimed until its visibility lapses, then returns.
            return (null, new(FencedClaimRunStatus.LostBeforeSideEffect, claim, default, exception));
        }
    }

    private async Task KeepRenewingAsync(
        Holding holding,
        CancellationToken stopToken,
        CancellationTokenSource sideEffectCancellation)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_renewalCadence, _timeProvider, stopToken);
                var renewed = await _renew(holding.Claim, _timeProvider.GetUtcNow(), _visibilityTimeout, stopToken);
                if (renewed is null)
                    break;
                holding.Claim = renewed;
            }

            holding.Lose(exception: null);
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // The side effect finished before the next renewal was due.
            return;
        }
        catch (Exception exception)
        {
            holding.Lose(exception);
        }

        await sideEffectCancellation.CancelAsync();
    }

    private sealed class Holding(TClaim claim)
    {
        public TClaim Claim { get; set; } = claim;
        public bool Lost { get; private set; }
        public Exception? LossException { get; private set; }

        public void Lose(Exception? exception)
        {
            Lost = true;
            LossException = exception;
        }
    }
}

/// <summary>How one <see cref="FencedClaimLease{TClaim}.RunAsync{TResult}"/> ended.</summary>
public enum FencedClaimRunStatus
{
    /// <summary>The side effect returned while the claim was still held. Complete the row with the run's claim.</summary>
    Completed,

    /// <summary>The side effect threw while the claim was still held. Record the failure with the run's claim.</summary>
    Faulted,

    /// <summary>The claim could not be renewed, so the side effect never ran. Skip the row.</summary>
    LostBeforeSideEffect,

    /// <summary>
    /// The claim was lost while the side effect ran, and the side effect was cancelled. Whatever it did belongs to the next
    /// claimant, which repeats it idempotently. Skip the row.
    /// </summary>
    LostDuringSideEffect
}

/// <summary>The outcome of running one side effect under a <see cref="FencedClaimLease{TClaim}"/>.</summary>
public sealed class FencedClaimRun<TClaim, TResult> where TClaim : class
{
    internal FencedClaimRun(FencedClaimRunStatus status, TClaim claim, TResult? result, Exception? exception)
    {
        Status = status;
        Claim = claim;
        Result = result;
        Exception = exception;
    }

    public FencedClaimRunStatus Status { get; }

    /// <summary>
    /// The latest claim the run held, which is the one to complete or release the row with. When the claim was lost it is
    /// the last one held, useful only to identify the row.
    /// </summary>
    public TClaim Claim { get; }

    /// <summary>The side effect's result when <see cref="Status"/> is <see cref="FencedClaimRunStatus.Completed"/>.</summary>
    public TResult? Result { get; }

    /// <summary>
    /// The side effect's exception when it <see cref="FencedClaimRunStatus.Faulted"/>; for a lost claim, the renewal's
    /// exception when the renewal threw rather than reporting the claim gone.
    /// </summary>
    public Exception? Exception { get; }

    public bool ClaimLost => Status is FencedClaimRunStatus.LostBeforeSideEffect or FencedClaimRunStatus.LostDuringSideEffect;
}
