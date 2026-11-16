using System.Threading.Tasks.Sources;
using Elsa.Workflows.Runtime.Services.Claims;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class FencedClaimLeaseTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Finishing_the_side_effect_keeps_an_applied_renewal_alive_until_its_acknowledgement(bool fault, bool providerCancellation)
    {
        var body = new InlineBody();
        var bodyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledge = new TaskCompletionSource<Claim?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        CancellationToken renewalToken = default;
        Claim? physical = null;
        var failure = new InvalidOperationException("body fault");
        var lease = new FencedClaimLease<Claim>((claim, _, _, ct) =>
        {
            physical = claim with { Version = claim.Version + 1 };
            if (Interlocked.Increment(ref calls) == 1) return ValueTask.FromResult(physical);
            // The store accepted the revision; acknowledgement is deliberately delayed. Cancelling now can discard
            // its new revision even though the write happened, so completion with the prior claim would be stale.
            renewalToken = ct;
            renewalApplied.TrySetResult();
            return new ValueTask<Claim?>(acknowledge.Task.WaitAsync(ct));
        }, VisibilityTimeout, _clock);
        var running = Task.Run(async () => await lease.RunRenewingAsync(_claim, _ =>
        {
            bodyEntered.TrySetResult();
            return body.Task;
        }));
        await bodyEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await body.AwaitRegistered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _clock.Advance(Cadence);
        await renewalApplied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        FencedClaimRun<Claim, int>? result = null;
        try
        {
            // The value-task source resumes the lease inline without a captured test synchronization context.
            // Returning from this call means its completion/finally path has run up to its next await.
            await Task.Run(() => { if (fault) body.Fail(failure); else body.Complete(42); });
            Assert.False(renewalToken.IsCancellationRequested);
            Assert.False(running.IsCompleted);
        }
        finally
        {
            if (providerCancellation) acknowledge.TrySetException(new OperationCanceledException("unrequested provider cancellation"));
            else acknowledge.TrySetResult(physical);
            result = await running.WaitAsync(TimeSpan.FromSeconds(10));
        }
        if (providerCancellation)
        {
            Assert.Equal(FencedClaimRunStatus.LostDuringSideEffect, result.Status);
            Assert.True(result.ClaimLost);
            Assert.Equal(2, result.Claim.Version); // applied revision has no accepted acknowledgement
            Assert.IsType<OperationCanceledException>(result.Exception);
            return;
        }
        Assert.Equal(3, result.Claim.Version);
        Assert.Equal(physical, result.Claim);
        Assert.Equal(fault ? FencedClaimRunStatus.Faulted : FencedClaimRunStatus.Completed, result.Status);
        if (fault) Assert.Same(failure, result.Exception);
        else Assert.Equal(42, result.Result);
    }

    [Fact]
    public async Task Caller_shutdown_still_cancels_an_inflight_renewal()
    {
        using var shutdown = new CancellationTokenSource();
        var renewalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bodyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        CancellationToken renewalToken = default;
        var lease = new FencedClaimLease<Claim>(async (claim, _, _, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1) return claim with { Version = 2 };
            renewalToken = ct;
            renewalEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return claim;
        }, VisibilityTimeout, _clock);
        var running = lease.RunRenewingAsync(_claim, async ct =>
        {
            bodyEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return true;
        }, shutdown.Token).AsTask();
        await bodyEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _clock.Advance(Cadence);
        await renewalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await shutdown.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(renewalToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Caller_shutdown_after_the_body_finishes_still_refuses_settlement_of_an_inflight_renewal()
    {
        using var shutdown = new CancellationTokenSource();
        var body = new InlineBody();
        var bodyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledge = new TaskCompletionSource<Claim?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var lease = new FencedClaimLease<Claim>((claim, _, _, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1) return ValueTask.FromResult<Claim?>(claim with { Version = 2 });
            renewalApplied.TrySetResult();
            return new ValueTask<Claim?>(acknowledge.Task.WaitAsync(ct));
        }, VisibilityTimeout, _clock);
        var running = Task.Run(async () => await lease.RunRenewingAsync(_claim, _ =>
        {
            bodyEntered.TrySetResult();
            return body.Task;
        }, shutdown.Token));
        await bodyEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await body.AwaitRegistered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _clock.Advance(Cadence);
        await renewalApplied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Run(() => body.Complete(42));
        await shutdown.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private sealed class InlineBody : IValueTaskSource<int>
    {
        private ManualResetValueTaskSourceCore<int> _source;
        public TaskCompletionSource AwaitRegistered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<int> Task => new(this, _source.Version);
        public void Complete(int value) => _source.SetResult(value);
        public void Fail(Exception error) => _source.SetException(error);
        public int GetResult(short token) => _source.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            _source.OnCompleted(continuation, state, token, flags);
            AwaitRegistered.TrySetResult();
        }
    }
}
