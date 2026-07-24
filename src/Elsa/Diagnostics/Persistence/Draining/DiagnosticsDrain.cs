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
    private readonly object _queueGate = new();
    private readonly object _shutdownGate = new();
    private Task? _drainLoop;
    private Task<DiagnosticsDrainStopResult>? _stopTask;
    private Task? _asyncDisposeTask;
    private Task? _shutdownCancellationTask;
    private long _retentionUnits;
    private long _queueDepth;
    private long _queueSequence;
    private int _accepting = 1;
    private int _forcedTermination;
    private int _shutdownDisposed;
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
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
        Observe(observer => observer.RecordState(DiagnosticsDrainState.Created));
    }

    public DiagnosticsDrainState State => (DiagnosticsDrainState)Volatile.Read(ref _state);

    /// <summary>Starts the single drain loop. Calling this more than once is harmless.</summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_drainLoop is not null)
            {
                if (_stopTask is null && Volatile.Read(ref _forcedTermination) == 0)
                    return;
                throw new InvalidOperationException("The diagnostics drain has entered its terminal lifecycle and cannot be restarted.");
            }
            if (Volatile.Read(ref _accepting) == 0 || _stopTask is not null)
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
        var pending = new PendingItem(item);
        acknowledgement = pending.Completion.Task;
        _accepted.TryAdd(pending, 0);
        PendingItem? shed = null;
        var accepted = false;
        QueueDepthUpdate? queueDepthUpdate = null;
        lock (_queueGate)
        {
            if (Volatile.Read(ref _accepting) != 0 && _channel.Writer.TryWrite(pending))
            {
                queueDepthUpdate = IncrementQueueDepth();
                accepted = true;
            }
            else if (Volatile.Read(ref _accepting) != 0 && _channel.Reader.TryRead(out shed))
            {
                _ = DecrementQueueDepth();
                if (_channel.Writer.TryWrite(pending))
                {
                    queueDepthUpdate = IncrementQueueDepth();
                    accepted = true;
                }
                else
                {
                    queueDepthUpdate = new(Volatile.Read(ref _queueDepth), Volatile.Read(ref _queueSequence));
                }
            }
        }

        if (queueDepthUpdate is { } update)
            ReportQueueDepth(update);
        if (shed is not null)
            Fail(shed, DiagnosticsPersistenceLossReason.QueueOverflow, "The diagnostics capture was shed before commit because the queue was full.");
        if (accepted)
        {
            Observe(observer => observer.RecordAccepted(1));
            return true;
        }

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
                if (Volatile.Read(ref _forcedTermination) != 0)
                    _stopTask = Task.FromResult(new DiagnosticsDrainStopResult(DiagnosticsDrainState.TimedOut, Drained: false));
                else
                {
                    _drainLoop ??= StartCore();
                    Volatile.Write(ref _accepting, 0);
                    SetState(DiagnosticsDrainState.Closing);
                    CompleteWriter();
                    SetState(DiagnosticsDrainState.Draining);
                    _stopTask = StopCoreAsync(_drainLoop);
                }
            }

            stopTask = _stopTask;
        }

        return await stopTask.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Stops a lifecycle-started drain without initiating provider work when host startup never reached this
    /// component. The never-started path is terminal so a late host callback cannot leak a background loop.
    /// </summary>
    public async Task<DiagnosticsDrainStopResult> StopIfStartedAsync(
        CancellationToken cancellationToken = default)
    {
        Task<DiagnosticsDrainStopResult>? neverStartedStop = null;
        var hadAcceptedItems = false;
        lock (_lifecycleGate)
        {
            if (_drainLoop is null && _stopTask is null)
            {
                Volatile.Write(ref _accepting, 0);
                CompleteWriter();
                hadAcceptedItems = !_accepted.IsEmpty;
                SetState(DiagnosticsDrainState.Stopped);
                neverStartedStop = _stopTask = Task.FromResult(
                    new DiagnosticsDrainStopResult(DiagnosticsDrainState.Stopped, Drained: !hadAcceptedItems));
            }
        }

        if (neverStartedStop is null)
            return await StopAsync(cancellationToken);

        if (hadAcceptedItems)
        {
            FailAll(
                DiagnosticsPersistenceLossReason.ShutdownTimeout,
                "The diagnostics host stopped before its capture drain started.");
        }
        DisposeShutdownSource();
        return await neverStartedStop.WaitAsync(cancellationToken);
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
                while (batch.Count < _options.BatchSize && TryReadQueuedItem(out var item, out var queueDepthUpdate))
                {
                    ReportQueueDepth(queueDepthUpdate);
                    batch.Add(item);
                }
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
            lock (_lifecycleGate)
            {
                if (Volatile.Read(ref _forcedTermination) != 0)
                    return new(DiagnosticsDrainState.TimedOut, Drained: false);
                SetState(DiagnosticsDrainState.Stopped);
            }
            DisposeShutdownSource();
            return new(DiagnosticsDrainState.Stopped, Drained: true);
        }
        catch (TimeoutException)
        {
            return TransitionToTimedOut(
                drainLoop,
                "The diagnostics drain exceeded its shutdown window.");
        }
        catch
        {
            return TransitionToTimedOut(
                drainLoop,
                "The diagnostics drain stopped before every acknowledgement completed.");
        }
    }

    private DiagnosticsDrainStopResult TransitionToTimedOut(Task drainLoop, string message)
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _forcedTermination) != 0)
                return new(DiagnosticsDrainState.TimedOut, Drained: false);
            Volatile.Write(ref _forcedTermination, 1);
            Volatile.Write(ref _accepting, 0);
            CompleteWriter();
            SetState(DiagnosticsDrainState.TimedOut);
        }

        var cancellation = RequestShutdownCancellation();
        FailAll(DiagnosticsPersistenceLossReason.ShutdownTimeout, message);
        ScheduleShutdownDisposal(drainLoop, cancellation);
        return new(DiagnosticsDrainState.TimedOut, Drained: false);
    }

    private TimeSpan RetryDelay(int completedAttempt)
    {
        var multiplier = Math.Pow(2, completedAttempt - 1);
        var delay = _options.BaseRetryDelay * multiplier;
        return delay <= _options.MaxRetryDelay ? delay : _options.MaxRetryDelay;
    }

    private bool TryReadQueuedItem(out PendingItem item, out QueueDepthUpdate queueDepthUpdate)
    {
        lock (_queueGate)
        {
            if (!_channel.Reader.TryRead(out var queuedItem))
            {
                item = null!;
                queueDepthUpdate = default;
                return false;
            }
            item = queuedItem;
            queueDepthUpdate = DecrementQueueDepth();
            return true;
        }
    }

    private void CompleteWriter()
    {
        lock (_queueGate)
            _channel.Writer.TryComplete();
    }

    private QueueDepthUpdate IncrementQueueDepth() => new(
        Interlocked.Increment(ref _queueDepth),
        Interlocked.Increment(ref _queueSequence));

    private QueueDepthUpdate DecrementQueueDepth()
    {
        var depth = Interlocked.Decrement(ref _queueDepth);
        if (depth < 0)
            throw new InvalidOperationException("The diagnostics drain queue depth became negative.");
        return new(depth, Interlocked.Increment(ref _queueSequence));
    }

    private void ReportQueueDepth(QueueDepthUpdate update)
    {
        Observe(observer => observer.RecordQueueDepth(update.Depth, update.Sequence));
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
        Task? drainLoop;
        lock (_lifecycleGate)
        {
            if (State == DiagnosticsDrainState.Stopped)
            {
                ScheduleShutdownDisposal(_drainLoop);
                return;
            }
            if (Volatile.Read(ref _forcedTermination) != 0)
                return;

            Volatile.Write(ref _forcedTermination, 1);
            Volatile.Write(ref _accepting, 0);
            CompleteWriter();
            SetState(DiagnosticsDrainState.TimedOut);
            _stopTask ??= Task.FromResult(new DiagnosticsDrainStopResult(DiagnosticsDrainState.TimedOut, Drained: false));
            drainLoop = _drainLoop;
        }

        var cancellation = RequestShutdownCancellation();
        FailAll(DiagnosticsPersistenceLossReason.ShutdownTimeout, "The diagnostics drain was synchronously disposed before commit.");
        ScheduleShutdownDisposal(drainLoop, cancellation);
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleGate)
            return new(_asyncDisposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync() => await StopIfStartedAsync();

    private Task RequestShutdownCancellation()
    {
        lock (_shutdownGate)
        {
            if (_shutdownCancellationTask is not null)
                return _shutdownCancellationTask;

            try
            {
                return _shutdownCancellationTask = _shutdown.CancelAsync();
            }
            catch (Exception exception)
            {
                return _shutdownCancellationTask = Task.FromException(exception);
            }
        }
    }

    private void ScheduleShutdownDisposal(Task? drainLoop, Task? cancellation = null)
    {
        var cleanup = Task.WhenAll(
            drainLoop ?? Task.CompletedTask,
            cancellation ?? Task.CompletedTask);
        if (cleanup.IsCompleted)
        {
            _ = cleanup.Exception;
            DisposeShutdownSource();
            return;
        }

        _ = cleanup.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;
                ((DiagnosticsDrain<TItem, TResult>)state!).DisposeShutdownSource();
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void DisposeShutdownSource()
    {
        if (Interlocked.Exchange(ref _shutdownDisposed, 1) == 0)
            _shutdown.Dispose();
    }

    private sealed class PendingItem(TItem item)
    {
        public TItem Item { get; } = item;
        public TaskCompletionSource<TResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly record struct QueueDepthUpdate(long Depth, long Sequence);
}
