using System.Diagnostics;

namespace Kernel.EntityFramework;

/// <summary>One client deadline for the node's renewal work.</summary>
internal sealed class LeaseIoDeadline : IAsyncDisposable
{
    private readonly CancellationToken _caller;
    private readonly TimeSpan _budget;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly CancellationTokenSource _source;
    private readonly CancellationTokenSource? _wait;
    private readonly Task<AggregateException?>? _relay;

    internal LeaseIoDeadline(CancellationToken caller, TimeSpan budget, bool containCallbacks)
    {
        _caller = caller;
        _budget = budget;
        if (containCallbacks)
        {
            // Connector/NET can throw from a callback during TLS setup. Its token is never linked directly
            // to a caller or armed with CancelAfter. The awaited relay owns callback dispatch and its failures.
            _source = new CancellationTokenSource();
            _wait = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _relay = RelayAsync(_wait.Token);
        }
        else
        {
            _source = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _source.CancelAfter(budget);
        }
    }

    internal CancellationToken Token => _source.Token;
    internal bool Expired => _source.IsCancellationRequested ||
        (_relay is not null && _budget != Timeout.InfiniteTimeSpan && Stopwatch.GetElapsedTime(_started) >= _budget);
    internal bool CallerCancelled => _relay is not null && _caller.IsCancellationRequested && _source.IsCancellationRequested;

    internal void CheckRenewalWindow()
    {
        if (_relay is null) return;
        _caller.ThrowIfCancellationRequested();
        // The timer callback can be delayed, or the driver can return after ignoring cancellation.
        if (Expired) throw new OperationCanceledException(Token);
    }

    internal async ValueTask<Exception> IncludeCallbackFailureAsync(Exception operation)
    {
        var callbackFailure = _relay is null ? null : await _relay;
        return callbackFailure is null ? operation : new AggregateException(operation, callbackFailure);
    }

    internal async ValueTask<OperationCanceledException> CallerExceptionAsync(Exception operation) =>
        new("The caller cancelled the MySQL node-lease operation.", await IncludeCallbackFailureAsync(operation).ConfigureAwait(false), _caller);

    private async Task<AggregateException?> RelayAsync(CancellationToken stop)
    {
        try { await Task.Delay(_budget, stop).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!_caller.IsCancellationRequested) return null;
        }

        try { await _source.CancelAsync().ConfigureAwait(false); }
        catch (AggregateException error) { return error; }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        using var source = _source;
        using var wait = _wait;
        if (wait is null) return;
        // Disarm the delay and let an already-started cancellation dispatch finish before disposing its token.
        wait.Cancel();
        await _relay!.ConfigureAwait(false);
    }
}
