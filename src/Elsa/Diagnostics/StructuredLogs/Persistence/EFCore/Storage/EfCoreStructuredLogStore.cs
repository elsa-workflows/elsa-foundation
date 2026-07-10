using System.Threading.Channels;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;

/// <summary>
/// A durable <see cref="IStructuredLogStore"/> backed by EF Core. Writes are decoupled from the capture
/// hot path: <see cref="Append"/> only enqueues onto a bounded channel (oldest dropped under sustained
/// overload) and a single background drain loop batch-inserts via the <see cref="IDbContextFactory{T}"/>.
/// History queries read the database directly and are defensive — any provider error degrades to an empty
/// result rather than throwing into the diagnostics endpoints.
/// </summary>
public sealed class EfCoreStructuredLogStore : IStructuredLogStore, IDisposable
{
    private const int BatchSize = 200;
    // Exponential backoff (issue #607, parity with EfCoreOpenTelemetryStore): the old fixed 1s x 5 was both
    // too slow for transient lock contention and too eager to give up under sustained reader load.
    private const int MaxBatchRetries = 8;
    private const int DefaultMaxRetainedEntries = 100_000;
    private const int DefaultPruneInterval = 5_000;
    private const long ShedLogIntervalMs = 30_000;
    private static readonly TimeSpan DefaultBaseRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IDbContextFactory<StructuredLogsDbContext> _dbContextFactory;
    private readonly int _maxRecentQuerySize;
    private readonly int _maxRetainedEntries;
    private readonly int _pruneInterval;
    private readonly Channel<StructuredLogEntry> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<EfCoreStructuredLogStore> _logger;
    private readonly TimeSpan _baseRetryDelay;
    private int _draining;
    private int _insertedSincePrune;
    private int _disposed;
    private Task? _drainLoop;
    private long _shedEntries;
    private long _lastShedLogTicks;

    public EfCoreStructuredLogStore(
        IDbContextFactory<StructuredLogsDbContext> dbContextFactory,
        IOptions<StructuredLogsOptions> options,
        ILogger<EfCoreStructuredLogStore>? logger = null)
        : this(dbContextFactory, options, DefaultMaxRetainedEntries, DefaultPruneInterval, logger)
    {
    }

    /// <summary>
    /// Constructor with explicit retention tuning, primarily for tests that want a small cap.
    /// <paramref name="maxRetainedEntries"/> bounds the durable table the same way the in-memory ring
    /// buffer bounds live history; <paramref name="pruneInterval"/> is how many inserts elapse between
    /// prune sweeps. <paramref name="baseRetryDelay"/> overrides the first backoff delay (subsequent
    /// retries double it up to a fixed ceiling) so retry-exhaustion tests do not have to wait out the
    /// production backoff schedule.
    /// </summary>
    public EfCoreStructuredLogStore(
        IDbContextFactory<StructuredLogsDbContext> dbContextFactory,
        IOptions<StructuredLogsOptions> options,
        int maxRetainedEntries,
        int pruneInterval,
        ILogger<EfCoreStructuredLogStore>? logger = null,
        TimeSpan? baseRetryDelay = null)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger ?? NullLogger<EfCoreStructuredLogStore>.Instance;
        _baseRetryDelay = baseRetryDelay ?? DefaultBaseRetryDelay;
        var value = options.Value;
        _maxRecentQuerySize = Math.Max(1, value.MaxRecentQuerySize);
        _maxRetainedEntries = Math.Max(1, maxRetainedEntries);
        _pruneInterval = Math.Max(1, pruneInterval);
        var capacity = Math.Max(value.BufferCapacity, BatchSize) * 4;
        _channel = Channel.CreateBounded<StructuredLogEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        }, OnEntryShed);
    }

    /// <inheritdoc />
    public void Append(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Non-blocking; the bounded channel drops the oldest queued entry under sustained overload so the
        // capture path never stalls a host thread on database I/O.
        _channel.Writer.TryWrite(entry);
    }

    /// <summary>
    /// Starts the background drain loop. Idempotent so it can be invoked from a per-tenant startup task
    /// without spawning multiple loops.
    /// </summary>
    public void StartDraining()
    {
        if (Interlocked.Exchange(ref _draining, 1) == 1)
            return;

        _drainLoop = Task.Run(() => RunDrainLoopAsync(_cts.Token));
    }

    /// <inheritdoc />
    public async Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await db.StructuredLogEntries.MaxAsync(x => (long?)x.Sequence, cancellationToken) ?? 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // The table may not exist yet (queried before migrations run). Treat as empty.
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var max = filter.MaxCount is { } requested
            ? Math.Clamp(requested, 0, _maxRecentQuerySize)
            : _maxRecentQuerySize;

        if (max == 0)
            return [];

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await ApplyFilter(db.StructuredLogEntries.AsNoTracking(), filter)
                .OrderByDescending(x => x.Id)
                .Take(max)
                .ToListAsync(cancellationToken);

            rows.Reverse(); // Contract: newest last.
            return rows.Select(StructuredLogEntryMapper.ToModel).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StructuredLogEntry>> GetAfterAsync(long afterSequence, StructuredLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await ApplyFilter(db.StructuredLogEntries.AsNoTracking(), filter)
                .Where(x => x.Sequence > afterSequence)
                .OrderBy(x => x.Id)
                .Take(_maxRecentQuerySize)
                .ToListAsync(cancellationToken);

            return rows.Select(StructuredLogEntryMapper.ToModel).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static IQueryable<PersistedStructuredLogEntry> ApplyFilter(IQueryable<PersistedStructuredLogEntry> query, StructuredLogFilter filter)
    {
        if (filter.MinimumLevel is { } minimumLevel)
        {
            var min = (int)minimumLevel;
            query = query.Where(x => x.Level >= min);
        }

        if (!string.IsNullOrEmpty(filter.Category))
            query = query.Where(x => x.Category == filter.Category);

        if (!string.IsNullOrEmpty(filter.SourceId))
            query = query.Where(x => x.SourceId == filter.SourceId);

        return query;
    }

    private async Task RunDrainLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;
        var batch = new List<StructuredLogEntry>(BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                batch.Clear();
                while (batch.Count < BatchSize && reader.TryRead(out var entry))
                    batch.Add(entry);

                if (batch.Count > 0)
                {
                    await PersistBatchAsync(batch, cancellationToken);
                    await MaybePruneAsync(batch.Count, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }

    private async Task PersistBatchAsync(IReadOnlyList<StructuredLogEntry> batch, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                foreach (var entry in batch)
                    db.StructuredLogEntries.Add(StructuredLogEntryMapper.ToEntity(entry));

                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxBatchRetries)
            {
                // Transient (e.g. the migration has not finished creating the table yet, or SQLite lock
                // contention). Retry with backoff.
                var delay = GetRetryDelay(attempt);
                _logger.LogDebug(ex, "Transient failure persisting a structured log batch (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.", attempt + 1, MaxBatchRetries + 1, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                // Best-effort: drop this batch rather than block the drain loop forever. Diagnostics
                // persistence tolerates loss; the live feed and in-memory history are unaffected.
                _logger.LogError(ex, "Dropping a structured log batch of {EntryCount} entries after {MaxAttempts} failed persistence attempts.", batch.Count, MaxBatchRetries + 1);
                return;
            }
        }
    }

    private async Task MaybePruneAsync(int inserted, CancellationToken cancellationToken)
    {
        _insertedSincePrune += inserted;
        if (_insertedSincePrune < _pruneInterval)
            return;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var maxId = await db.StructuredLogEntries.MaxAsync(x => (long?)x.Id, cancellationToken) ?? 0;
                var threshold = maxId - _maxRetainedEntries;

                if (threshold > 0)
                {
                    // Durable ordering is by Id, so deleting everything at or below the threshold keeps
                    // the newest _maxRetainedEntries rows. Runs off the capture hot path inside the
                    // drain loop.
                    await db.StructuredLogEntries
                        .Where(x => x.Id <= threshold)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                // Reset only on success (issue #403, parity with EfCoreOpenTelemetryStore): a reset
                // before the attempt let a single transient failure abandon retention permanently.
                _insertedSincePrune = 0;
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxBatchRetries)
            {
                // Transient (e.g. SQLite busy/lock contention). Retry with backoff.
                var delay = GetRetryDelay(attempt);
                _logger.LogDebug(ex, "Transient failure pruning structured log retention (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.", attempt + 1, MaxBatchRetries + 1, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                // Best-effort retention: a failed prune must not break the drain loop. Keep the
                // counter armed so the next persisted batch retries pruning.
                _logger.LogWarning(ex, "Giving up pruning structured log retention after {MaxAttempts} attempts; the next persisted batch retries.", MaxBatchRetries + 1);
                return;
            }
        }
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        var delay = _baseRetryDelay * Math.Pow(2, attempt);
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    /// <summary>
    /// Invoked by the bounded channel when it evicts the oldest queued entry to make room (issue #607):
    /// the drain writer is not keeping up with capture. Logged rate-limited so a sustained overload does
    /// not flood the log with one warning per shed entry.
    /// </summary>
    private void OnEntryShed(StructuredLogEntry entry)
    {
        var shed = Interlocked.Increment(ref _shedEntries);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastShedLogTicks);
        if (last != 0 && now - last < ShedLogIntervalMs)
            return;
        if (Interlocked.CompareExchange(ref _lastShedLogTicks, now, last) != last)
            return;

        _logger.LogWarning("Structured log drain channel is full; shedding the oldest queued entry ({ShedEntryCount} entries shed since startup). The database writer is not keeping up with capture.", shed);
    }

    public void Dispose()
    {
        // Idempotent (issue #403, parity with EfCoreOpenTelemetryStore): a second call must not throw
        // ObjectDisposedException from the already-disposed CancellationTokenSource.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }
}
