using System.Runtime.ExceptionServices;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Capture.Core;

namespace Elsa.Workflows.Publishing.Releases.Capture;

/// <summary>One awaited retention callback; source adapters cannot swallow or replace its primary failure.</summary>
internal static class RetainedCaptureCallback
{
    internal static async ValueTask ExecuteAsync<T>(Func<Func<T, CancellationToken, ValueTask>, CancellationToken, ValueTask> execute,
        Func<T, CancellationToken, ValueTask> capture, CancellationToken token) where T : class
    {
        var calls = 0; var completed = 0; var returned = 0; ExceptionDispatchInfo? callbackFailure = null; ExceptionDispatchInfo? protocolFailure = null;
        Task? pending = null; ExceptionDispatchInfo? sourceFailure = null;
        var gate = new object(); var pendingAtReturn = false;
        using var lifetime = new CancellationTokenSource();
        try
        {
            await execute((value, sourceToken) =>
            {
                lock (gate)
                {
                    if (Interlocked.Increment(ref calls) != 1 || Volatile.Read(ref returned) != 0 || value is null)
                    {
                        var exception = new ReleaseCaptureException("capture_source_callback_invalid");
                        Interlocked.CompareExchange(ref protocolFailure, ExceptionDispatchInfo.Capture(exception), null);
                        return ValueTask.FromException(exception);
                    }
                    pending = InvokeAsync(value, sourceToken);
                    return new ValueTask(pending);
                }
            }, token);
        }
        catch (Exception exception) { sourceFailure = ExceptionDispatchInfo.Capture(exception); }
        finally
        {
            lock (gate)
            {
                Volatile.Write(ref returned, 1);
                pendingAtReturn = pending is { IsCompleted: false };
            }
            if (pendingAtReturn)
                try { lifetime.Cancel(); }
                catch (Exception exception) { sourceFailure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        // Retention must not be released while an invalid source's detached callback is still using it.
        if (pending is not null)
            try { await pending; }
            catch { /* InvokeAsync retained the original callback failure, including unknown archive commits. */ }
        if (pendingAtReturn && callbackFailure?.SourceException is OperationCanceledException && !token.IsCancellationRequested)
            throw new ReleaseCaptureException("capture_source_callback_invalid");
        callbackFailure?.Throw();
        protocolFailure?.Throw();
        sourceFailure?.Throw();
        if (pendingAtReturn || Volatile.Read(ref calls) != 1 || Volatile.Read(ref completed) != 1)
            throw new ReleaseCaptureException("capture_source_callback_invalid");

        async Task InvokeAsync(T value, CancellationToken sourceToken)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, sourceToken, lifetime.Token);
                linked.Token.ThrowIfCancellationRequested();
                await capture(value, linked.Token);
                Volatile.Write(ref completed, 1);
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref callbackFailure, ExceptionDispatchInfo.Capture(exception), null);
                throw;
            }
        }
    }
}
