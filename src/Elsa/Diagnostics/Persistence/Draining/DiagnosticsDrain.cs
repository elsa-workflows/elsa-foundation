using System.Collections.Concurrent;
using System.Threading.Channels;
using Elsa.Diagnostics.Persistence.Observability;

namespace Elsa.Diagnostics.Persistence.Draining;

/// <summary>
/// Composes one concrete target with Elsa-owned bounded queue, retry, acknowledgement, retention, and
/// shutdown policy. It contains no provider types and never emits captured diagnostic payloads.
/// </summary>
public sealed class DiagnosticsDrain<TItem, TResult> : IDisposable, IAsyncDisposable where TItem : notnull
{
    private readonly IDiagnosticsDrainTarget<TItem, TResult> _target;
    private readonly DiagnosticsDrainOptions _options;
    private readonly IDiagnosticsPersistenceObserver _observer;
    private readonly Channel<PendingItem> _channel;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<PendingItem, byte> _accepted = new();
    private readonly object _lifecycleGate = new();
    private Task? _drainLoop;
    private Task<DiagnosticsDrainStopResult>? _stopTask;
    private long _retentionUnits;
    private int _accepting = 1;
    private int _disposed;
    private int _state = (int)DiagnosticsDrainState.Created;

    public DiagnosticsDrain(
        IDiagnosticsDrainTarget<TItem, TResult> target,
        DiagnosticsDrainOptions options,
        IDiagnosticsPersistenceObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _target = target;
        _options = options;
        _observer = observer ?? NullDiagnosticsPersistenceObserver.Instance;
        _channel = Channel.CreateBounded<PendingItem>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        }, OnItemShed);
        Observe(observer => observer.RecordState(DiagnosticsDrainState.Created));
    }

    public DiagnosticsDrainState State => (DiagnosticsDrainState)Volatile.Read(ref _state);

    /// <summary>Starts the single drain loop. Calling this more than once is harmless.</summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_drainLoop is not null)
                return;
            if (Volatile.Read(ref _accepting) == 0)
                throw new InvalidOperationException("The diagnostics drain is closing and cannot be started.");
            StartCore();
        }
    }

    /// <summary>
    /// Accepts without waiting for provider I/O. The returned acknowledgement completes exactly once with
    /// the authoritative commit result or an explicit loss failure.
    /// </summary>
    public bool TryEnqueue(TItem item, out Task<TResult> acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Volatile.Read(ref _accepting) == 0)
        {
            acknowledgement = RejectedAcknowledgement(DiagnosticsPersistenceLossReason.WriteAfterClosure);
            return false;
        }

        var pending = new PendingItem(item);
        acknowledgement = pending.Completion.Task;
        _accepted.TryAdd(pending, 0);
        if (_channel.Writer.TryWrite(pending))
            return true;

        Fail(pending, DiagnosticsPersistenceLossReason.WriteAfterClosure, "The diagnostics drain is not accepting captures.");
        return false;
    }

    public ValueTask<TResult> EnqueueAsync(TItem item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryEnqueue(item, out var acknowledgement);
        return new(acknowledgement.WaitAsync(cancellationToken));
    }

    /// <summary>
    /// Closes producers, drains within the configured window, applies final retention, and returns an
    /// observable stopped or timed-out outcome. Caller cancellation only cancels that caller's wait; the
    /// shared stop continues so accepted acknowledgements are never abandoned.
    /// </summary>
    public async Task<DiagnosticsDrainStopResult> StopAsync(CancellationToken cancellationToken = default)
    {
        Task<DiagnosticsDrainStopResult> stopTask;
        lock (_lifecycleGate)
        {
            if (_stopTask is null)
            {
                _drainLoop ??= StartCore();
                Volatile.Write(ref _accepting, 0);
                SetState(DiagnosticsDrainState.Closing);
                _channel.Writer.TryComplete();
                SetState(DiagnosticsDrainState.Draining);
                _stopTask = StopCoreAsync(_drainLoop);
            }

            stopTask = _stopTask;
        }

        return await stopTask.WaitAsync(cancellationToken);
    }

    private Task StartCore()
    {
        SetState(DiagnosticsDrainState.Running);
        return _drainLoop = Task.Run(() => RunAsync(_shutdown.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;
        var batch = new List<PendingItem>(_options.BatchSize);
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                batch.Clear();
                while (batch.Count < _options.BatchSize && reader.TryRead(out var item))
                    batch.Add(item);
                if (batch.Count == 0)
                    continue;

                var committedUnits = await CommitWithRetryAsync(batch.ToArray(), cancellationToken);
                await ApplyPeriodicRetentionAsync(committedUnits, cancellationToken);
            }

            await ApplyRetentionWithRetryAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The bounded stop path settles every accepted acknowledgement below.
        }
        finally
        {
            if (!_accepted.IsEmpty)
                FailAll(DiagnosticsPersistenceLossReason.ShutdownTimeout, "The diagnostics capture was canceled before commit.");
        }
    }

    private async Task<int> CommitWithRetryAsync(
        IReadOnlyList<PendingItem> pending,
        CancellationToken cancellationToken)
    {
        var batch = new DiagnosticsDrainBatch<TItem>(DiagnosticsDrainBatchId.New(), pending.Select(item => item.Item).ToArray());
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try
            {
                var commit = await _target.CommitAsync(batch, cancellationToken);
                if (commit.Results.Count != pending.Count)
                    throw new InvalidOperationException("The diagnostics target returned an incomplete commit result.");
                ArgumentOutOfRangeException.ThrowIfNegative(commit.RetentionUnits);
                for (var index = 0; index < pending.Count; index++)
                    Complete(pending[index], commit.Results[index]);
                return commit.RetentionUnits;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                if (attempt == _options.MaxAttempts)
                    break;
                Observe(observer => observer.RecordRetry(DiagnosticsPersistenceOperation.Commit, attempt, _options.MaxAttempts));
                await Task.Delay(RetryDelay(attempt), cancellationToken);
            }
        }

        Observe(observer => observer.RecordOperationFailure(DiagnosticsPersistenceOperation.Commit));
        foreach (var item in pending)
            Fail(item, DiagnosticsPersistenceLossReason.RetryExhausted, "The diagnostics capture could not be committed after bounded retries.", lastFailure);
        return 0;
    }

    private async Task ApplyPeriodicRetentionAsync(int committedUnits, CancellationToken cancellationToken)
    {
        _retentionUnits += committedUnits;
        if (_retentionUnits >= _options.RetentionInterval)
            await ApplyRetentionWithRetryAsync(cancellationToken);
    }

    private async Task ApplyRetentionWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try
            {
                var deleted = await _target.ApplyRetentionAsync(cancellationToken);
                ArgumentOutOfRangeException.ThrowIfNegative(deleted);
                _retentionUnits = 0;
                if (deleted > 0)
                    Observe(observer => observer.RecordLoss(DiagnosticsPersistenceLossReason.DurableRetentionDeletion, deleted));
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (attempt < _options.MaxAttempts)
            {
                Observe(observer => observer.RecordRetry(DiagnosticsPersistenceOperation.Retention, attempt, _options.MaxAttempts));
                await Task.Delay(RetryDelay(attempt), cancellationToken);
            }
            catch
            {
                Observe(observer => observer.RecordOperationFailure(DiagnosticsPersistenceOperation.Retention));
                return;
            }
        }
    }

    private async Task<DiagnosticsDrainStopResult> StopCoreAsync(Task drainLoop)
    {
        try
        {
            await drainLoop.WaitAsync(_options.ShutdownTimeout);
            SetState(DiagnosticsDrainState.Stopped);
            return new(State, Drained: true);
        }
        catch (TimeoutException)
        {
            SetState(DiagnosticsDrainState.TimedOut);
            await _shutdown.CancelAsync();
            FailAll(DiagnosticsPersistenceLossReason.ShutdownTimeout, "The diagnostics drain exceeded its shutdown window.");
            return new(State, Drained: false);
        }
        catch
        {
            SetState(DiagnosticsDrainState.TimedOut);
            await _shutdown.CancelAsync();
            FailAll(DiagnosticsPersistenceLossReason.ShutdownTimeout, "The diagnostics drain stopped before every acknowledgement completed.");
            return new(State, Drained: false);
        }
    }

    private TimeSpan RetryDelay(int completedAttempt)
    {
        var multiplier = Math.Pow(2, completedAttempt - 1);
        var delay = _options.BaseRetryDelay * multiplier;
        return delay <= _options.MaxRetryDelay ? delay : _options.MaxRetryDelay;
    }

    private void OnItemShed(PendingItem pending) =>
        Fail(pending, DiagnosticsPersistenceLossReason.QueueOverflow, "The diagnostics capture was shed before commit because the queue was full.");

    private Task<TResult> RejectedAcknowledgement(DiagnosticsPersistenceLossReason reason)
    {
        Observe(observer => observer.RecordLoss(reason, 1));
        return Task.FromException<TResult>(new DiagnosticsDrainException(reason, "The diagnostics drain is closed."));
    }

    private void Complete(PendingItem pending, TResult result)
    {
        if (!pending.Completion.TrySetResult(result))
            return;
        _accepted.TryRemove(pending, out _);
    }

    private void Fail(
        PendingItem pending,
        DiagnosticsPersistenceLossReason reason,
        string message,
        Exception? innerException = null)
    {
        if (!pending.Completion.TrySetException(new DiagnosticsDrainException(reason, message, innerException)))
            return;
        _accepted.TryRemove(pending, out _);
        Observe(observer => observer.RecordLoss(reason, 1));
    }

    private void FailAll(DiagnosticsPersistenceLossReason reason, string message)
    {
        foreach (var pending in _accepted.Keys)
            Fail(pending, reason, message);
    }

    private void SetState(DiagnosticsDrainState state)
    {
        Volatile.Write(ref _state, (int)state);
        Observe(observer => observer.RecordState(state));
    }

    private void Observe(Action<IDiagnosticsPersistenceObserver> report)
    {
        try
        {
            report(_observer);
        }
        catch
        {
            // Observability is deliberately outside the persistence correctness boundary.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Volatile.Write(ref _accepting, 0);
        _channel.Writer.TryComplete();
        SetState(DiagnosticsDrainState.TimedOut);
        _shutdown.Cancel();
        FailAll(DiagnosticsPersistenceLossReason.ShutdownTimeout, "The diagnostics drain was synchronously disposed before commit.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync();
        if (_drainLoop is { IsCompleted: true })
            _shutdown.Dispose();
        else if (_drainLoop is { } drainLoop)
            _ = drainLoop.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                _shutdown,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private sealed class PendingItem(TItem item)
    {
        public TItem Item { get; } = item;
        public TaskCompletionSource<TResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
