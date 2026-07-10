using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Mapping;
using Elsa.Persistence.EFCore.Storage;
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
/// result rather than throwing into the diagnostics endpoints. The channel and drain lifecycle
/// (start/complete/dispose, retries, shed accounting) live in <see cref="ChannelDrainingStoreBase{TItem}"/>;
/// this class contributes the EF Core persistence and the single-table retention pruning.
/// </summary>
public sealed class EfCoreStructuredLogStore : ChannelDrainingStoreBase<StructuredLogEntry>, IStructuredLogStore
{
    private const int BatchSize = 200;
    private const int DefaultMaxRetainedEntries = 100_000;
    private const int DefaultPruneInterval = 5_000;

    private readonly IDbContextFactory<StructuredLogsDbContext> _dbContextFactory;
    private readonly int _maxRecentQuerySize;
    private readonly int _maxRetainedEntries;

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
        : base(
            BatchSize,
            pruneInterval,
            Math.Max(options.Value.BufferCapacity, BatchSize) * 4,
            options.Value.ShutdownDrainTimeout,
            logger ?? NullLogger<EfCoreStructuredLogStore>.Instance,
            baseRetryDelay)
    {
        _dbContextFactory = dbContextFactory;
        _maxRecentQuerySize = Math.Max(1, options.Value.MaxRecentQuerySize);
        _maxRetainedEntries = Math.Max(1, maxRetainedEntries);
    }

    /// <inheritdoc />
    public void Append(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Non-blocking; the bounded channel drops the oldest queued entry under sustained overload so the
        // capture path never stalls a host thread on database I/O.
        TryWrite(entry);
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

    protected override async Task<int> PersistBatchAsync(IReadOnlyList<StructuredLogEntry> batch, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        foreach (var entry in batch)
            db.StructuredLogEntries.Add(StructuredLogEntryMapper.ToEntity(entry));

        await db.SaveChangesAsync(cancellationToken);
        return batch.Count;
    }

    protected override int OnBatchDropped(IReadOnlyList<StructuredLogEntry> batch, Exception exception, int attempts)
    {
        // Best-effort: drop this batch rather than block the drain loop forever. Diagnostics persistence
        // tolerates loss; the live feed and in-memory history are unaffected.
        Logger.LogError(exception, "Dropping a structured log batch of {EntryCount} entries after {MaxAttempts} failed persistence attempts.", batch.Count, attempts);
        // Historically a dropped batch still counted toward the prune interval (the drain loop advanced the
        // counter by batch size regardless of persistence outcome).
        return batch.Count;
    }

    protected override async Task PruneAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var maxId = await db.StructuredLogEntries.MaxAsync(x => (long?)x.Id, cancellationToken) ?? 0;
        var threshold = maxId - _maxRetainedEntries;

        if (threshold > 0)
        {
            // Durable ordering is by Id, so deleting everything at or below the threshold keeps the newest
            // _maxRetainedEntries rows. Runs off the capture hot path inside the drain loop.
            await db.StructuredLogEntries
                .Where(x => x.Id <= threshold)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    protected override void LogShedWarning(long totalShed) =>
        Logger.LogWarning("Structured log drain channel is full; shedding the oldest queued entry ({ShedEntryCount} entries shed since startup). The database writer is not keeping up with capture.", totalShed);

    protected override void LogTransientPersistFailure(Exception exception, int attempt, int maxAttempts, TimeSpan delay) =>
        // Transient (e.g. the migration has not finished creating the table yet, or SQLite lock contention).
        Logger.LogDebug(exception, "Transient failure persisting a structured log batch (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.", attempt, maxAttempts, delay);

    protected override void LogTransientPruneFailure(Exception exception, int attempt, int maxAttempts, TimeSpan delay) =>
        // Transient (e.g. SQLite busy/lock contention). Retry with backoff.
        Logger.LogDebug(exception, "Transient failure pruning structured log retention (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.", attempt, maxAttempts, delay);

    protected override void LogPruneGivenUp(int maxAttempts) =>
        Logger.LogWarning("Giving up pruning structured log retention after {MaxAttempts} attempts; the next persisted batch retries.", maxAttempts);
}
