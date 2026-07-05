using System.Threading.Channels;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Mapping;
using Microsoft.EntityFrameworkCore;
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
    private const int MaxBatchRetries = 5;
    private const int DefaultMaxRetainedEntries = 100_000;
    private const int DefaultPruneInterval = 5_000;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly IDbContextFactory<StructuredLogsDbContext> _dbContextFactory;
    private readonly int _maxRecentQuerySize;
    private readonly int _maxRetainedEntries;
    private readonly int _pruneInterval;
    private readonly Channel<StructuredLogEntry> _channel;
    private readonly CancellationTokenSource _cts = new();
    private int _draining;
    private int _insertedSincePrune;
    private Task? _drainLoop;

    public EfCoreStructuredLogStore(IDbContextFactory<StructuredLogsDbContext> dbContextFactory, IOptions<StructuredLogsOptions> options)
        : this(dbContextFactory, options, DefaultMaxRetainedEntries, DefaultPruneInterval)
    {
    }

    /// <summary>
    /// Constructor with explicit retention tuning, primarily for tests that want a small cap.
    /// <paramref name="maxRetainedEntries"/> bounds the durable table the same way the in-memory ring
    /// buffer bounds live history; <paramref name="pruneInterval"/> is how many inserts elapse between
    /// prune sweeps.
    /// </summary>
    public EfCoreStructuredLogStore(
        IDbContextFactory<StructuredLogsDbContext> dbContextFactory,
        IOptions<StructuredLogsOptions> options,
        int maxRetainedEntries,
        int pruneInterval)
    {
        _dbContextFactory = dbContextFactory;
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
        });
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
            catch when (attempt < MaxBatchRetries)
            {
                // Transient (e.g. the migration has not finished creating the table yet). Retry.
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch
            {
                // Best-effort: drop this batch rather than block the drain loop forever. Diagnostics
                // persistence tolerates loss; the live feed and in-memory history are unaffected.
                return;
            }
        }
    }

    private async Task MaybePruneAsync(int inserted, CancellationToken cancellationToken)
    {
        _insertedSincePrune += inserted;
        if (_insertedSincePrune < _pruneInterval)
            return;

        _insertedSincePrune = 0;

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var maxId = await db.StructuredLogEntries.MaxAsync(x => (long?)x.Id, cancellationToken) ?? 0;
            var threshold = maxId - _maxRetainedEntries;
            if (threshold <= 0)
                return;

            // Durable ordering is by Id, so deleting everything at or below the threshold keeps the
            // newest _maxRetainedEntries rows. Runs off the capture hot path inside the drain loop.
            await db.StructuredLogEntries
                .Where(x => x.Id <= threshold)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best-effort retention: a failed prune must not break the drain loop.
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }
}
