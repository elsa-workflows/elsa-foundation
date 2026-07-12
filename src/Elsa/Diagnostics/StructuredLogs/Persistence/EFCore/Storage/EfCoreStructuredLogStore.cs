using System.Collections.Concurrent;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
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
/// hot path: <see cref="AppendAsync"/> only enqueues onto a bounded channel (oldest dropped under sustained
/// overload) and its acknowledgement completes after a single background drain loop batch-inserts via the
/// <see cref="IDbContextFactory{T}"/>.
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
    private const string HighWaterStateSourceId = "__elsa_structured_logs_high_water__";

    private readonly IDbContextFactory<StructuredLogsDbContext> _dbContextFactory;
    private readonly int _maxRecentQuerySize;
    private readonly int _maxRetainedEntries;
    private readonly StructuredLogStoreBinding _binding = StructuredLogStoreBinding.Default;
    private readonly ConcurrentDictionary<StructuredLogEntry, ConcurrentQueue<TaskCompletionSource<StructuredLogEntry>>> _pending =
        new(ReferenceEqualityComparer.Instance);

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
        _ = AppendAsync(entry);
    }

    /// <inheritdoc />
    public ValueTask<StructuredLogEntry> AppendAsync(
        StructuredLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsHighWaterState(entry))
            throw new ArgumentException("The structured log entry uses a representation reserved by the EF compatibility store.", nameof(entry));
        var completion = new TaskCompletionSource<StructuredLogEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.GetOrAdd(entry, _ => new()).Enqueue(completion);
        if (!TryWrite(entry))
            CompletePending(entry, exception: new StructuredLogsException("The structured log store is not accepting appends."));

        return new(completion.Task.WaitAsync(cancellationToken));
    }

    /// <inheritdoc />
    public async Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            return await db.StructuredLogEntries.MaxAsync(x => (long?)x.Sequence, cancellationToken) ?? 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsMissingLegacySqliteTable(db, exception))
        {
            // The temporary SQLite compatibility feature can seed the sink before its existing migration
            // creates the table. Only that explicit pre-migration condition means "never written"; every
            // other provider, connectivity, corruption, and query failure must remain observable.
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
            var rows = await ApplyFilter(DataRows(db.StructuredLogEntries.AsNoTracking()), filter)
                .OrderByDescending(x => x.Id)
                .Take(max)
                .ToListAsync(cancellationToken);

            rows.Reverse(); // Contract: newest last.
            return rows.Select(ToCommittedModel).ToList();
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
    public async Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tail = await DataRows(db.StructuredLogEntries.AsNoTracking())
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return tail is null ? null : ToCommittedModel(tail).ReplayCursor;
    }

    /// <inheritdoc />
    public async Task<StructuredLogReadPage> ReadAfterAsync(
        StructuredLogReplayCursor? afterCursor,
        StructuredLogFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        var limit = Math.Min(maxCount, _maxRecentQuerySize);

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var afterId = 0L;
            if (afterCursor is { } cursor)
            {
                if (!EfCoreReplayCursorCodec.TryDecode(cursor, _binding, out var parts) ||
                    !long.TryParse(parts.ProviderPosition, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out afterId))
                    throw new StructuredLogReplayCursorUnavailableException();
                var anchor = await DataRows(db.StructuredLogEntries.AsNoTracking())
                    .SingleOrDefaultAsync(x => x.Id == afterId, cancellationToken);
                if (anchor is null ||
                    !StringComparer.Ordinal.Equals(anchor.SourceId, parts.EntrySourceId) ||
                    !StringComparer.Ordinal.Equals(RecordToken(anchor), parts.RecordToken))
                    throw new StructuredLogReplayCursorUnavailableException();
            }

            var snapshot = await DataRows(db.StructuredLogEntries)
                .MaxAsync(x => (long?)x.Id, cancellationToken) ?? afterId;
            var rows = await DataRows(db.StructuredLogEntries.AsNoTracking())
                .Where(x => x.Id > afterId && x.Id <= snapshot)
                .OrderBy(x => x.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            var entries = rows.Select(ToCommittedModel).Where(filter.Matches).ToArray();
            var next = rows.Count == 0 ? afterCursor : ToCommittedModel(rows[^1]).ReplayCursor;
            return new(entries, next, rows.Count > 0 && rows[^1].Id < snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (StructuredLogReplayCursorUnavailableException)
        {
            throw;
        }
        catch
        {
            throw new StructuredLogReplayCursorUnavailableException();
        }
    }

    /// <inheritdoc />
    public async Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureHighWaterStateAsync(db, [], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (keepNewest == 0)
        {
            await DataRows(db.StructuredLogEntries).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var threshold = await DataRows(db.StructuredLogEntries)
            .OrderByDescending(x => x.Id)
            .Skip(keepNewest - 1)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (threshold is { } cursor)
            await DataRows(db.StructuredLogEntries).Where(x => x.Id < cursor).ExecuteDeleteAsync(cancellationToken);
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
        var rows = new List<PersistedStructuredLogEntry>(batch.Count);
        foreach (var entry in batch)
        {
            var row = StructuredLogEntryMapper.ToEntity(entry);
            rows.Add(row);
            db.StructuredLogEntries.Add(row);
        }

        await EnsureHighWaterStateAsync(db, batch, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        for (var index = 0; index < batch.Count; index++)
            CompletePending(batch[index], ToCommittedModel(rows[index]));
        return batch.Count;
    }

    protected override int OnBatchDropped(IReadOnlyList<StructuredLogEntry> batch, Exception exception, int attempts)
    {
        // Best-effort: drop this batch rather than block the drain loop forever. Diagnostics persistence
        // tolerates loss; the live feed and in-memory history are unaffected.
        Logger.LogError(exception, "Dropping a structured log batch of {EntryCount} entries after {MaxAttempts} failed persistence attempts.", batch.Count, attempts);
        // Historically a dropped batch still counted toward the prune interval (the drain loop advanced the
        // counter by batch size regardless of persistence outcome).
        foreach (var entry in batch)
            CompletePending(entry, exception: new StructuredLogsException("The structured log append could not be committed.", exception));
        return batch.Count;
    }

    protected override void OnItemShed(StructuredLogEntry entry) =>
        CompletePending(entry, exception: new StructuredLogsException("The structured log append was shed before commit."));

    protected override void OnDrainStopped()
    {
        var exception = new StructuredLogsException("The structured log append was canceled before commit.");
        foreach (var (entry, completions) in _pending)
        {
            if (!_pending.TryRemove(entry, out _))
                continue;
            while (completions.TryDequeue(out var completion))
                completion.TrySetException(exception);
        }
    }

    protected override async Task PruneAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureHighWaterStateAsync(db, [], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var threshold = await DataRows(db.StructuredLogEntries)
            .OrderByDescending(x => x.Id)
            .Skip(_maxRetainedEntries)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (threshold is { } oldestRetainedBoundary)
        {
            await DataRows(db.StructuredLogEntries)
                .Where(x => x.Id <= oldestRetainedBoundary)
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

    private StructuredLogEntry ToCommittedModel(PersistedStructuredLogEntry row)
    {
        var entry = StructuredLogEntryMapper.ToModel(row);
        return entry with
        {
            ReplayCursor = EfCoreReplayCursorCodec.Encode(
                _binding,
                row.SourceId,
                RecordToken(row),
                row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
    }

    private static string RecordToken(PersistedStructuredLogEntry row)
    {
        var canonical = $"{row.Id}:{row.SourceId.Length}:{row.SourceId}:{row.Sequence}:{row.Timestamp.UtcTicks}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    private void CompletePending(
        StructuredLogEntry entry,
        StructuredLogEntry? committed = null,
        Exception? exception = null)
    {
        if (!_pending.TryGetValue(entry, out var completions) || !completions.TryDequeue(out var completion))
            return;
        if (completions.IsEmpty)
            _pending.TryRemove(entry, out _);

        if (exception is null)
            completion.TrySetResult(committed!);
        else
            completion.TrySetException(exception);
    }

    private static IQueryable<PersistedStructuredLogEntry> DataRows(IQueryable<PersistedStructuredLogEntry> query) =>
        query.Where(x =>
            x.SourceId != HighWaterStateSourceId ||
            x.Category != HighWaterStateSourceId ||
            x.EventName != HighWaterStateSourceId ||
            x.MessageTemplate != HighWaterStateSourceId);

    private static bool IsHighWaterState(StructuredLogEntry entry) =>
        entry.SourceId == HighWaterStateSourceId &&
        entry.Category == HighWaterStateSourceId &&
        entry.EventName == HighWaterStateSourceId &&
        entry.MessageTemplate == HighWaterStateSourceId;

    private static bool IsMissingLegacySqliteTable(StructuredLogsDbContext db, Exception exception)
    {
        var entityType = db.Model.FindEntityType(typeof(PersistedStructuredLogEntry))
                         ?? throw new InvalidOperationException("The Structured Logs EF model does not contain its persistence entity.");
        var tableName = entityType.GetTableName()
                        ?? throw new InvalidOperationException("The Structured Logs EF entity is not mapped to a table.");
        var providerFailure = exception.GetBaseException();
        return StringComparer.Ordinal.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite") &&
               StringComparer.Ordinal.Equals(providerFailure.GetType().FullName, "Microsoft.Data.Sqlite.SqliteException") &&
               StringComparer.Ordinal.Equals(
                   providerFailure.Message,
                   $"SQLite Error 1: 'no such table: {tableName}'.");
    }

    private static async Task EnsureHighWaterStateAsync(
        StructuredLogsDbContext db,
        IReadOnlyList<StructuredLogEntry> pending,
        CancellationToken cancellationToken)
    {
        var retainedHighWater = await db.StructuredLogEntries
            .MaxAsync(x => (long?)x.Sequence, cancellationToken) ?? 0;
        var pendingHighWater = pending.Count == 0 ? 0 : pending.Max(x => x.Sequence);
        var highWater = Math.Max(retainedHighWater, pendingHighWater);
        if (highWater == 0 && !await db.StructuredLogEntries.AnyAsync(cancellationToken))
            return;

        var states = await db.StructuredLogEntries
            .Where(x =>
                x.SourceId == HighWaterStateSourceId &&
                x.Category == HighWaterStateSourceId &&
                x.EventName == HighWaterStateSourceId &&
                x.MessageTemplate == HighWaterStateSourceId)
            .ToListAsync(cancellationToken);
        if (states.Count == 0)
        {
            db.StructuredLogEntries.Add(new PersistedStructuredLogEntry
            {
                Sequence = highWater,
                Timestamp = DateTimeOffset.UnixEpoch,
                Level = (int)Microsoft.Extensions.Logging.LogLevel.None,
                Category = HighWaterStateSourceId,
                EventName = HighWaterStateSourceId,
                Message = string.Empty,
                MessageTemplate = HighWaterStateSourceId,
                SourceId = HighWaterStateSourceId
            });
        }
        else
        {
            foreach (var state in states)
                if (highWater > state.Sequence)
                    state.Sequence = highWater;
        }
    }
}
