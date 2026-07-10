using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EFCore.Storage;

/// <summary>
/// Shared channel-drain lifecycle for durable diagnostics stores that decouple a hot capture path from
/// database I/O: writes enqueue onto a bounded channel (oldest dropped under sustained overload) and a
/// single background drain loop batch-persists with exponential-backoff retries, applying retention
/// pruning every <paramref name="pruneInterval"/> inserts. On graceful shutdown the shell provider
/// disposes the store via <see cref="DisposeAsync"/>, which drains the channel before cancelling so
/// buffered items are not discarded (issue #606). Extracted from the hand-mirrored twins
/// <c>EfCoreOpenTelemetryStore</c>/<c>EfCoreStructuredLogStore</c> (issues #403/#606/#607).
/// </summary>
/// <typeparam name="TItem">The unit enqueued by the capture path and batch-persisted by the drain loop.</typeparam>
public abstract class ChannelDrainingStoreBase<TItem> : IDisposable, IAsyncDisposable where TItem : notnull
{
    // Exponential backoff (issue #607): a fixed 1s x 5 was both too slow for transient lock contention
    // and too eager to give up under sustained reader load.
    private const int MaxBatchRetries = 8;
    private const long ShedLogIntervalMs = 30_000;
    private static readonly TimeSpan DefaultBaseRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DrainCompletionTimeout = TimeSpan.FromMinutes(2);

    private readonly int _batchSize;
    private readonly int _pruneInterval;
    private readonly Channel<TItem> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _baseRetryDelay;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly object _drainStartLock = new();
    private int _insertedSincePrune;
    private Task? _drainLoop;
    private int _disposed;
    private long _shedCount;
    private long _lastShedLogTicks;

    /// <param name="batchSize">Maximum number of items the drain loop persists per batch.</param>
    /// <param name="pruneInterval">How many inserts elapse between retention prune sweeps (clamped to at least 1).</param>
    /// <param name="channelCapacity">Bounded channel capacity; the oldest queued item is shed when full.</param>
    /// <param name="shutdownDrainTimeout">
    /// Window <see cref="DisposeAsync"/> gives the drain loop before the hard cancel (negative clamps to
    /// zero, zero = immediate hard stop).
    /// </param>
    /// <param name="logger">Sink for the derived store's drain/shed/retry log messages.</param>
    /// <param name="baseRetryDelay">
    /// Overrides the first backoff delay (subsequent retries double it up to a fixed ceiling) so
    /// retry-exhaustion tests do not have to wait out the production backoff schedule.
    /// </param>
    protected ChannelDrainingStoreBase(
        int batchSize,
        int pruneInterval,
        int channelCapacity,
        TimeSpan shutdownDrainTimeout,
        ILogger logger,
        TimeSpan? baseRetryDelay = null)
    {
        _batchSize = batchSize;
        _pruneInterval = Math.Max(1, pruneInterval);
        _shutdownDrainTimeout = shutdownDrainTimeout < TimeSpan.Zero ? TimeSpan.Zero : shutdownDrainTimeout;
        Logger = logger;
        _baseRetryDelay = baseRetryDelay ?? DefaultBaseRetryDelay;
        _channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        }, OnItemShedCore);
    }

    /// <summary>The logger the derived store's hook implementations write to.</summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Enqueues an item without blocking. Returns <c>false</c> when the writer was completed (store
    /// stopping/stopped) and the item was discarded. This only covers the completed-writer path — under
    /// overflow, DropOldest accepts the write and silently evicts the oldest queued item instead.
    /// </summary>
    protected bool TryWrite(TItem item) => _channel.Writer.TryWrite(item);

    /// <summary>
    /// Starts the background drain loop. Idempotent so it can be invoked from a per-tenant startup task
    /// without spawning multiple loops.
    /// </summary>
    public void StartDraining()
    {
        if (Volatile.Read(ref _drainLoop) is not null)
            return;

        lock (_drainStartLock)
        {
            // _drainLoop is the single "draining started" signal: it is only ever assigned here, fully
            // constructed, so a concurrent DisposeAsync/CompleteDrainingAsync either sees null (drain never
            // started) or the live loop task — never a started-but-unpublished in-between that would make
            // shutdown skip the graceful drain (#606 follow-up).
            _drainLoop ??= Task.Run(() => RunDrainLoopAsync(_cts.Token));
        }
    }

    /// <summary>
    /// Stops accepting writes, waits for the drain loop to finish attempting persistence of every item
    /// already enqueued (bounded retries; a persistently failing batch is dropped), then applies retention
    /// pruning once more on the same best-effort basis. Awaiting this is a completion signal rather than a
    /// timing guess. Throws <see cref="InvalidOperationException"/> when draining was never started and
    /// <see cref="TimeoutException"/> when the loop fails to finish within a generous ceiling.
    /// </summary>
    public async Task CompleteDrainingAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _drainLoop) is not { } drainLoop)
            throw new InvalidOperationException($"{nameof(StartDraining)} must be called before {nameof(CompleteDrainingAsync)}.");

        _channel.Writer.TryComplete();
        await drainLoop.WaitAsync(DrainCompletionTimeout, cancellationToken);

        // Apply retention once more so completion implies the retention cap holds even when the tail of
        // inserts never reached the prune interval. This runs here rather than in the drain loop so the
        // Dispose path (which cancels instead of draining) does no post-completion database work.
        if (_insertedSincePrune > 0)
            await PruneWithRetryAsync(cancellationToken);
    }

    /// <summary>
    /// Persists one batch in a single attempt and returns the number of rows counted toward the prune
    /// interval. The base class owns the retry loop: any exception other than a cancellation is retried
    /// with exponential backoff, and after <c>maxAttempts</c> failures the batch is dropped via
    /// <see cref="OnBatchDropped"/>.
    /// </summary>
    protected abstract Task<int> PersistBatchAsync(IReadOnlyList<TItem> batch, CancellationToken cancellationToken);

    /// <summary>
    /// Applies retention pruning in a single attempt. The base class owns the retry loop and resets the
    /// inserted-since-prune counter only on success (issue #403): a reset before the attempt let a single
    /// transient failure abandon retention permanently. Transient failures back off via
    /// <see cref="LogTransientPruneFailure"/>; exhaustion gives up via <see cref="LogPruneGivenUp"/> and
    /// keeps the counter armed so the next persisted batch retries.
    /// </summary>
    protected abstract Task PruneAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Handles a batch dropped after retry exhaustion: account for the loss and log it. Returns the count
    /// this dropped batch contributes to the prune interval (the structured log store historically counts
    /// dropped entries toward the next prune sweep; the telemetry store contributes zero).
    /// </summary>
    protected abstract int OnBatchDropped(IReadOnlyList<TItem> batch, Exception exception, int attempts);

    /// <summary>
    /// Invoked for every item the bounded channel evicts to make room (issue #607): the drain writer is
    /// not keeping up with ingest. Runs before the rate-limited <see cref="LogShedWarning"/>, so per-item
    /// drop accounting belongs here.
    /// </summary>
    protected virtual void OnItemShed(TItem item)
    {
    }

    /// <summary>Logs the rate-limited shed warning with the total items shed since startup.</summary>
    protected abstract void LogShedWarning(long totalShed);

    /// <summary>Logs a transient batch-persistence failure before the backoff delay.</summary>
    protected abstract void LogTransientPersistFailure(Exception exception, int attempt, int maxAttempts, TimeSpan delay);

    /// <summary>Logs a transient retention-prune failure before the backoff delay.</summary>
    protected abstract void LogTransientPruneFailure(Exception exception, int attempt, int maxAttempts, TimeSpan delay);

    /// <summary>Logs that retention pruning was abandoned after exhausting its attempts.</summary>
    protected abstract void LogPruneGivenUp(int maxAttempts);

    private async Task RunDrainLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;
        var batch = new List<TItem>(_batchSize);

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                batch.Clear();
                while (batch.Count < _batchSize && reader.TryRead(out var item))
                    batch.Add(item);

                if (batch.Count > 0)
                {
                    var inserted = await PersistBatchWithRetryAsync(batch, cancellationToken);
                    await MaybePruneAsync(inserted, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }

    private async Task<int> PersistBatchWithRetryAsync(IReadOnlyList<TItem> batch, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await PersistBatchAsync(batch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxBatchRetries)
            {
                var delay = GetRetryDelay(attempt);
                LogTransientPersistFailure(ex, attempt + 1, MaxBatchRetries + 1, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                return OnBatchDropped(batch, ex, MaxBatchRetries + 1);
            }
        }
    }

    private async Task MaybePruneAsync(int inserted, CancellationToken cancellationToken)
    {
        _insertedSincePrune += inserted;
        if (_insertedSincePrune >= _pruneInterval)
            await PruneWithRetryAsync(cancellationToken);
    }

    private async Task PruneWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await PruneAsync(cancellationToken);
                _insertedSincePrune = 0;
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxBatchRetries)
            {
                var delay = GetRetryDelay(attempt);
                LogTransientPruneFailure(ex, attempt + 1, MaxBatchRetries + 1, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch
            {
                // Best-effort retention: a failed prune must not break the drain loop. Keep the counter
                // armed so the next persisted batch retries pruning.
                LogPruneGivenUp(MaxBatchRetries + 1);
                return;
            }
        }
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        var delay = _baseRetryDelay * Math.Pow(2, attempt);
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    private void OnItemShedCore(TItem item)
    {
        OnItemShed(item);
        var shed = Interlocked.Increment(ref _shedCount);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastShedLogTicks);
        if (last != 0 && now - last < ShedLogIntervalMs)
            return;
        if (Interlocked.CompareExchange(ref _lastShedLogTicks, now, last) != last)
            return;

        LogShedWarning(shed);
    }

    /// <summary>
    /// Hard-stop for synchronous disposal contexts only: completes the writer and immediately cancels the
    /// drain loop, discarding whatever is still queued in the channel. Best-effort by design — a graceful
    /// host shutdown goes through <see cref="DisposeAsync"/> instead, which drains before cancelling.
    /// Idempotent (issue #403): a second call must not throw ObjectDisposedException from the
    /// already-disposed CancellationTokenSource.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }

    /// <summary>
    /// Graceful shutdown path (issue #606): completes the writer and gives the drain loop the bounded
    /// shutdown window to persist what is still buffered before the hard cancel. The shell provider is
    /// disposed asynchronously on host shutdown, so this — not <see cref="Dispose"/> — is the path a
    /// graceful shutdown takes; loss past the window is accepted rather than stalling shutdown
    /// indefinitely (async disposal carries no cancellation token, so the configured window is the only
    /// bound).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();

        if (Volatile.Read(ref _drainLoop) is { } drainLoop)
        {
            try
            {
                await drainLoop.WaitAsync(_shutdownDrainTimeout);
            }
            catch (TimeoutException)
            {
                // The shutdown window elapsed; fall through to the hard cancel and accept the loss.
            }
        }

        _cts.Cancel();
        _cts.Dispose();
    }
}
