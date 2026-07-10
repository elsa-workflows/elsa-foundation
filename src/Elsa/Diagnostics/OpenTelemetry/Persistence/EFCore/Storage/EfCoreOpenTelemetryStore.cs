using System.Threading.Channels;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;

/// <summary>
/// Durable <see cref="IOpenTelemetryStore"/> backed by EF Core. Writes enqueue onto a bounded channel so
/// anonymous OTLP collector requests do not wait on database I/O; a single background drain loop persists
/// batches and prunes each high-volume signal table to the configured retention capacity. On graceful
/// shutdown the shell provider disposes the store via <see cref="DisposeAsync"/>, which drains the channel
/// before cancelling so buffered telemetry is not discarded (issue #606).
/// </summary>
public sealed class EfCoreOpenTelemetryStore : IOpenTelemetryStore, IDisposable, IAsyncDisposable
{
    private const int BatchSize = 64;
    // Exponential backoff (issue #607, parity with EfCoreStructuredLogStore): the old fixed 1s x 5 was both
    // too slow for transient lock contention and too eager to give up under sustained reader load.
    private const int MaxBatchRetries = 8;
    private const int DefaultPruneInterval = 500;
    private const long ShedLogIntervalMs = 30_000;
    private static readonly TimeSpan DefaultBaseRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DrainCompletionTimeout = TimeSpan.FromMinutes(2);

    private readonly IDbContextFactory<OpenTelemetryDbContext> _dbContextFactory;
    private readonly IOpenTelemetrySourceRegistry _sourceRegistry;
    private readonly OpenTelemetryDiagnosticsOptions _options;
    private readonly int _traceCapacity;
    private readonly int _spanCapacity;
    private readonly int _metricPointCapacity;
    private readonly int _logRecordCapacity;
    private readonly int _resourceCapacity;
    private readonly int _maxQuerySize;
    private readonly int _pruneInterval;
    private readonly Channel<OpenTelemetryBatch> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<EfCoreOpenTelemetryStore> _logger;
    private readonly TimeSpan _baseRetryDelay;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly object _drainStartLock = new();
    private int _insertedSincePrune;
    private Task? _drainLoop;
    private int _disposed;
    private long _droppedTraces;
    private long _droppedSpans;
    private long _droppedMetricPoints;
    private long _droppedLogRecords;
    private long _shedBatches;
    private long _lastShedLogTicks;

    public EfCoreOpenTelemetryStore(
        IDbContextFactory<OpenTelemetryDbContext> dbContextFactory,
        IOptions<OpenTelemetryDiagnosticsOptions> options,
        IOpenTelemetrySourceRegistry sourceRegistry,
        ILogger<EfCoreOpenTelemetryStore>? logger = null)
        : this(dbContextFactory, options, sourceRegistry, DefaultPruneInterval, logger)
    {
    }

    /// <summary>
    /// Constructor with explicit tuning, primarily for tests. <paramref name="baseRetryDelay"/> overrides
    /// the first backoff delay (subsequent retries double it up to a fixed ceiling) so retry-exhaustion
    /// tests do not have to wait out the production backoff schedule.
    /// </summary>
    public EfCoreOpenTelemetryStore(
        IDbContextFactory<OpenTelemetryDbContext> dbContextFactory,
        IOptions<OpenTelemetryDiagnosticsOptions> options,
        IOpenTelemetrySourceRegistry sourceRegistry,
        int pruneInterval,
        ILogger<EfCoreOpenTelemetryStore>? logger = null,
        TimeSpan? baseRetryDelay = null)
    {
        _dbContextFactory = dbContextFactory;
        _sourceRegistry = sourceRegistry;
        _logger = logger ?? NullLogger<EfCoreOpenTelemetryStore>.Instance;
        _baseRetryDelay = baseRetryDelay ?? DefaultBaseRetryDelay;
        _options = options.Value;
        _traceCapacity = ClampCapacity(_options.TraceCapacity);
        _spanCapacity = ClampCapacity(_options.SpanCapacity);
        _metricPointCapacity = ClampCapacity(_options.MetricPointCapacity);
        _logRecordCapacity = ClampCapacity(_options.LogRecordCapacity);
        _resourceCapacity = ClampCapacity(_options.ResourceCapacity);
        _maxQuerySize = ClampCapacity(_options.MaxQuerySize);
        _shutdownDrainTimeout = _options.ShutdownDrainTimeout < TimeSpan.Zero ? TimeSpan.Zero : _options.ShutdownDrainTimeout;
        _pruneInterval = Math.Max(1, pruneInterval);
        var capacity = Math.Max(BatchSize, _options.SubscriberChannelCapacity) * 4;
        _channel = Channel.CreateBounded<OpenTelemetryBatch>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        }, OnBatchShed);
    }

    public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();

        // A false TryWrite means the writer was completed (store stopping/stopped): drop the batch without
        // marking its resources seen. This only covers the completed-writer path — under overflow, DropOldest
        // accepts the write and silently evicts the oldest queued batch, whose already-marked resources never
        // persist.
        if (!_channel.Writer.TryWrite(batch))
            return ValueTask.CompletedTask;

        foreach (var resource in batch.Resources)
            _sourceRegistry.MarkSeen(resource);

        return ValueTask.CompletedTask;
    }

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
    /// Stops accepting writes, waits for the drain loop to finish attempting persistence of every batch
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

        // Apply retention once more so completion implies the capacities hold even when the tail of inserts
        // never reached the prune interval. This runs here rather than in the drain loop so the Dispose path
        // (which cancels instead of draining) does no post-completion database work.
        if (_insertedSincePrune > 0)
            await PruneWithRetryAsync(cancellationToken);
    }

    public async ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new OpenTelemetryResourceResult([], _sourceRegistry.DroppedCount);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var resources = (await db.TelemetryResources.AsNoTracking()
                .OrderByDescending(x => x.LastSeen)
                .ThenBy(x => x.ServiceName)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken))
            .Select(OpenTelemetryMapper.ToModel)
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Where(x => string.IsNullOrWhiteSpace(filter.ServiceName) || string.Equals(x.ServiceName, filter.ServiceName, StringComparison.OrdinalIgnoreCase))
            .Where(x => filter.Status == null || x.Status == filter.Status)
            .Where(x => string.IsNullOrWhiteSpace(filter.Search) || Matches(x.ServiceName, filter.Search) || Matches(x.Id, filter.Search))
            .Take(take)
            .ToList();

        return new OpenTelemetryResourceResult(resources, _sourceRegistry.DroppedCount);
    }

    public async ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new OpenTelemetryTraceResult([], 0);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var serviceResourceIds = await ResolveResourceIdsAsync(db, filter.ServiceName, cancellationToken);
        var query = db.TelemetryTraces.AsNoTracking().AsQueryable();

        if (filter.Status != null)
            query = query.Where(x => x.Status == (int)filter.Status);
        if (filter.From != null)
            query = query.Where(x => x.StartTime >= filter.From);
        if (filter.To != null)
            query = query.Where(x => x.StartTime <= filter.To);

        var traces = (await query.OrderBy(x => x.StartTime).ThenBy(x => x.Id).ToListAsync(cancellationToken))
            .Select(OpenTelemetryMapper.ToModel)
            .Where(x => string.IsNullOrWhiteSpace(filter.TraceId) || Matches(x.TraceId, filter.TraceId))
            .Where(x => string.IsNullOrWhiteSpace(filter.Search) || Matches(x.TraceId, filter.Search) || Matches(x.Name, filter.Search))
            .Where(x => string.IsNullOrWhiteSpace(filter.WorkflowInstanceId) || x.WorkflowInstanceIds.Any(id => Matches(id, filter.WorkflowInstanceId)))
            .Where(x => string.IsNullOrWhiteSpace(filter.ResourceId) || x.ResourceIds.Contains(filter.ResourceId, StringComparer.OrdinalIgnoreCase))
            .Where(x => serviceResourceIds == null || x.ResourceIds.Any(serviceResourceIds.Contains))
            .GroupBy(x => x.TraceId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .TakeLast(take)
            .ToList();

        return new OpenTelemetryTraceResult(traces, 0);
    }

    public async ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var trace = (await db.TelemetryTraces.AsNoTracking()
                .OrderByDescending(x => x.StartTime)
                .ThenByDescending(x => x.Id)
                .ToListAsync(cancellationToken))
            .FirstOrDefault(x => string.Equals(x.TraceId, traceId, StringComparison.OrdinalIgnoreCase));

        if (trace == null)
            return null;

        var traceModel = OpenTelemetryMapper.ToModel(trace);
        var resourceIds = traceModel.ResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var spans = (await db.TelemetrySpans.AsNoTracking()
                .OrderBy(x => x.StartTime)
                .ThenBy(x => x.SpanId)
                .ToListAsync(cancellationToken))
            .Select(OpenTelemetryMapper.ToModel)
            .Where(x => string.Equals(x.TraceId, traceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var resources = (await db.TelemetryResources.AsNoTracking()
                .Where(x => resourceIds.Contains(x.Id))
                .OrderBy(x => x.ServiceName)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken))
            .Select(OpenTelemetryMapper.ToModel)
            .ToList();
        var logs = (await db.OtlpLogRecords.AsNoTracking()
                .OrderBy(x => x.Timestamp)
                .ThenBy(x => x.LogRecordId)
                .ToListAsync(cancellationToken))
            .Select(OpenTelemetryMapper.ToModel)
            .Where(x => string.Equals(x.TraceId, traceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new OpenTelemetryTraceDetail(traceModel, spans, resources, logs);
    }

    public async ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new OpenTelemetryMetricResult([], [], 0);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var serviceResourceIds = await ResolveResourceIdsAsync(db, filter.ServiceName, cancellationToken);
        var instruments = (await db.MetricInstruments.AsNoTracking().ToListAsync(cancellationToken))
            .Select(OpenTelemetryMapper.ToModel)
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var instrumentFilterIds = string.IsNullOrWhiteSpace(filter.InstrumentName)
            ? null
            : instruments.Values
                .Where(x => Matches(x.Name, filter.InstrumentName))
                .Select(x => x.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var query = db.MetricPoints.AsNoTracking().AsQueryable();
        if (filter.From != null)
            query = query.Where(x => x.Timestamp >= filter.From);
        if (filter.To != null)
            query = query.Where(x => x.Timestamp <= filter.To);

        var points = (await query.OrderBy(x => x.Timestamp).ThenBy(x => x.Id).ToListAsync(cancellationToken))
            .Select(OpenTelemetryMapper.ToModel)
            .Where(x => string.IsNullOrWhiteSpace(filter.ResourceId) || string.Equals(x.ResourceId, filter.ResourceId, StringComparison.OrdinalIgnoreCase))
            .Where(x => serviceResourceIds == null || serviceResourceIds.Contains(x.ResourceId))
            .Where(x => instrumentFilterIds == null || instrumentFilterIds.Contains(x.InstrumentId))
            .TakeLast(take)
            .ToList();
        var selectedInstruments = points
            .Select(x => x.InstrumentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(instruments.ContainsKey)
            .Select(x => instruments[x])
            .ToList();

        return new OpenTelemetryMetricResult(selectedInstruments, points, 0);
    }

    public async ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var take = ClampTake(filter.Take);
        if (take == 0)
            return new OpenTelemetryLogResult([], 0);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var serviceResourceIds = await ResolveResourceIdsAsync(db, filter.ServiceName, cancellationToken);
        var query = db.OtlpLogRecords.AsNoTracking().AsQueryable();

        if (filter.From != null)
            query = query.Where(x => x.Timestamp >= filter.From);
        if (filter.To != null)
            query = query.Where(x => x.Timestamp <= filter.To);

        var logs = (await query.OrderBy(x => x.Timestamp).ThenBy(x => x.Id).ToListAsync(cancellationToken))
            .Select(OpenTelemetryMapper.ToModel)
            .Where(x => string.IsNullOrWhiteSpace(filter.ResourceId) || string.Equals(x.ResourceId, filter.ResourceId, StringComparison.OrdinalIgnoreCase))
            .Where(x => serviceResourceIds == null || serviceResourceIds.Contains(x.ResourceId))
            .Where(x => string.IsNullOrWhiteSpace(filter.TraceId) || Matches(x.TraceId, filter.TraceId))
            .Where(x => string.IsNullOrWhiteSpace(filter.SpanId) || Matches(x.SpanId, filter.SpanId))
            .Where(x => string.IsNullOrWhiteSpace(filter.Severity) || Matches(x.SeverityText, filter.Severity))
            .Where(x => string.IsNullOrWhiteSpace(filter.Search) || Matches(x.Body, filter.Search))
            .TakeLast(take)
            .ToList();

        return new OpenTelemetryLogResult(logs, 0);
    }

    public async ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return new OpenTelemetryStorageDiagnostics(
            _traceCapacity,
            _spanCapacity,
            _metricPointCapacity,
            _logRecordCapacity,
            await db.TelemetryResources.CountAsync(cancellationToken),
            await db.TelemetryTraces.CountAsync(cancellationToken),
            await db.TelemetrySpans.CountAsync(cancellationToken),
            await db.MetricInstruments.CountAsync(cancellationToken),
            await db.MetricPoints.CountAsync(cancellationToken),
            await db.OtlpLogRecords.CountAsync(cancellationToken),
            Interlocked.Read(ref _droppedTraces),
            Interlocked.Read(ref _droppedSpans),
            Interlocked.Read(ref _droppedMetricPoints),
            Interlocked.Read(ref _droppedLogRecords));
    }

    private async Task RunDrainLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;
        var batch = new List<OpenTelemetryBatch>(BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                batch.Clear();
                while (batch.Count < BatchSize && reader.TryRead(out var item))
                    batch.Add(item);

                if (batch.Count > 0)
                {
                    var inserted = await PersistBatchAsync(batch, cancellationToken);
                    await MaybePruneAsync(inserted, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }

    private async Task<int> PersistBatchAsync(IReadOnlyCollection<OpenTelemetryBatch> batches, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                foreach (var resource in batches.SelectMany(x => x.Resources))
                    await UpsertResourceAsync(db, resource, cancellationToken);
                foreach (var instrument in batches.SelectMany(x => x.Instruments))
                    await UpsertInstrumentAsync(db, instrument, cancellationToken);

                foreach (var trace in batches.SelectMany(x => x.Traces))
                    db.TelemetryTraces.Add(OpenTelemetryMapper.ToEntity(trace));
                foreach (var span in batches.SelectMany(x => x.Spans))
                    db.TelemetrySpans.Add(OpenTelemetryMapper.ToEntity(span));
                foreach (var point in batches.SelectMany(x => x.MetricPoints))
                    db.MetricPoints.Add(OpenTelemetryMapper.ToEntity(point));
                foreach (var log in batches.SelectMany(x => x.Logs))
                    db.OtlpLogRecords.Add(OpenTelemetryMapper.ToEntity(log));

                var inserted = batches.Sum(x => x.Resources.Count + x.Traces.Count + x.Spans.Count + x.MetricPoints.Count + x.Logs.Count);
                await db.SaveChangesAsync(cancellationToken);
                return inserted;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxBatchRetries)
            {
                var delay = GetRetryDelay(attempt);
                _logger.LogDebug(ex, "Transient failure persisting a telemetry batch (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.", attempt + 1, MaxBatchRetries + 1, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                CountDropped(batches);
                _logger.LogError(ex, "Dropping a telemetry batch after {MaxAttempts} failed persistence attempts: {TraceCount} traces, {SpanCount} spans, {MetricPointCount} metric points, {LogRecordCount} log records lost.",
                    MaxBatchRetries + 1,
                    batches.Sum(x => x.Traces.Count),
                    batches.Sum(x => x.Spans.Count),
                    batches.Sum(x => x.MetricPoints.Count),
                    batches.Sum(x => x.Logs.Count));
                return 0;
            }
        }
    }

    private static async Task UpsertResourceAsync(OpenTelemetryDbContext db, TelemetryResource resource, CancellationToken cancellationToken)
    {
        var entity = await FindResourceAsync(db, resource.Id, cancellationToken);
        if (entity == null)
            db.TelemetryResources.Add(OpenTelemetryMapper.ToEntity(resource));
        else
            OpenTelemetryMapper.CopyToEntity(resource, entity);
    }

    private static async Task UpsertInstrumentAsync(OpenTelemetryDbContext db, MetricInstrument instrument, CancellationToken cancellationToken)
    {
        var entity = await FindInstrumentAsync(db, instrument.Id, cancellationToken);
        if (entity == null)
            db.MetricInstruments.Add(OpenTelemetryMapper.ToEntity(instrument));
        else
            OpenTelemetryMapper.CopyToEntity(instrument, entity);
    }

    private static async Task<PersistedTelemetryResource?> FindResourceAsync(OpenTelemetryDbContext db, string id, CancellationToken cancellationToken)
    {
        var local = db.TelemetryResources.Local.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (local != null)
            return local;

        return (await db.TelemetryResources.ToListAsync(cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<PersistedMetricInstrument?> FindInstrumentAsync(OpenTelemetryDbContext db, string id, CancellationToken cancellationToken)
    {
        var local = db.MetricInstruments.Local.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (local != null)
            return local;

        return (await db.MetricInstruments.ToListAsync(cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
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
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                await PruneTracesAsync(db, cancellationToken);
                await PruneSpansAsync(db, cancellationToken);
                await PruneMetricPointsAsync(db, cancellationToken);
                await PruneLogsAsync(db, cancellationToken);
                await PruneResourcesAsync(db, cancellationToken);
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
                _logger.LogDebug(ex, "Transient failure pruning telemetry retention (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.", attempt + 1, MaxBatchRetries + 1, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                // Best-effort retention: a transient prune failure must not stop the drain loop. Keep the
                // counter armed so the next persisted batch retries pruning.
                _logger.LogWarning(ex, "Giving up pruning telemetry retention after {MaxAttempts} attempts; the next persisted batch retries.", MaxBatchRetries + 1);
                return;
            }
        }
    }

    private async Task PruneTracesAsync(OpenTelemetryDbContext db, CancellationToken cancellationToken)
    {
        var maxId = await db.TelemetryTraces.MaxAsync(x => (long?)x.Id, cancellationToken) ?? 0;
        var threshold = maxId - _traceCapacity;
        if (threshold <= 0)
            return;

        await db.TelemetryTraces.Where(x => x.Id <= threshold).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task PruneSpansAsync(OpenTelemetryDbContext db, CancellationToken cancellationToken)
    {
        var maxId = await db.TelemetrySpans.MaxAsync(x => (long?)x.Id, cancellationToken) ?? 0;
        var threshold = maxId - _spanCapacity;
        if (threshold <= 0)
            return;

        await db.TelemetrySpans.Where(x => x.Id <= threshold).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task PruneMetricPointsAsync(OpenTelemetryDbContext db, CancellationToken cancellationToken)
    {
        var maxId = await db.MetricPoints.MaxAsync(x => (long?)x.Id, cancellationToken) ?? 0;
        var threshold = maxId - _metricPointCapacity;
        if (threshold <= 0)
            return;

        await db.MetricPoints.Where(x => x.Id <= threshold).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task PruneLogsAsync(OpenTelemetryDbContext db, CancellationToken cancellationToken)
    {
        var maxId = await db.OtlpLogRecords.MaxAsync(x => (long?)x.Id, cancellationToken) ?? 0;
        var threshold = maxId - _logRecordCapacity;
        if (threshold <= 0)
            return;

        await db.OtlpLogRecords.Where(x => x.Id <= threshold).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task PruneResourcesAsync(OpenTelemetryDbContext db, CancellationToken cancellationToken)
    {
        var oldIds = await db.TelemetryResources.OrderByDescending(x => x.LastSeen)
            .Skip(_resourceCapacity)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (oldIds.Count == 0)
            return;

        await db.TelemetryResources.Where(x => oldIds.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<HashSet<string>?> ResolveResourceIdsAsync(OpenTelemetryDbContext db, string? serviceName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return null;

        return (await db.TelemetryResources.AsNoTracking()
                .Select(x => new { x.Id, x.ServiceName })
                .ToListAsync(cancellationToken))
            .Where(x => string.Equals(x.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        var delay = _baseRetryDelay * Math.Pow(2, attempt);
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    /// <summary>
    /// Invoked by the bounded channel when it evicts the oldest queued batch to make room (issue #607):
    /// the drain writer is not keeping up with ingest. Counted per signal and logged rate-limited so a
    /// sustained overload does not flood the log with one warning per shed batch.
    /// </summary>
    private void OnBatchShed(OpenTelemetryBatch batch)
    {
        CountDropped([batch]);
        var shed = Interlocked.Increment(ref _shedBatches);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastShedLogTicks);
        if (last != 0 && now - last < ShedLogIntervalMs)
            return;
        if (Interlocked.CompareExchange(ref _lastShedLogTicks, now, last) != last)
            return;

        _logger.LogWarning("Telemetry drain channel is full; shedding the oldest queued batch ({ShedBatchCount} batches shed since startup). The database writer is not keeping up with ingest.", shed);
    }

    private void CountDropped(IReadOnlyCollection<OpenTelemetryBatch> batches)
    {
        Interlocked.Add(ref _droppedTraces, batches.Sum(x => x.Traces.Count));
        Interlocked.Add(ref _droppedSpans, batches.Sum(x => x.Spans.Count));
        Interlocked.Add(ref _droppedMetricPoints, batches.Sum(x => x.MetricPoints.Count));
        Interlocked.Add(ref _droppedLogRecords, batches.Sum(x => x.Logs.Count));
    }

    private int ClampTake(int? take) => Math.Clamp(take ?? _maxQuerySize, 0, _maxQuerySize);

    private static int ClampCapacity(int capacity) => Math.Max(1, capacity);

    private static bool Matches(string? candidate, string? search) =>
        !string.IsNullOrEmpty(candidate) && !string.IsNullOrEmpty(search) && candidate.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Hard-stop for synchronous disposal contexts only: completes the writer and immediately cancels the
    /// drain loop, discarding whatever is still queued in the channel. Best-effort by design — a graceful
    /// host shutdown goes through <see cref="DisposeAsync"/> instead, which drains before cancelling.
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
    /// Graceful shutdown path (issue #606): completes the writer and gives the drain loop a bounded window
    /// (<see cref="OpenTelemetryDiagnosticsOptions.ShutdownDrainTimeout"/>) to persist what is still
    /// buffered before the hard cancel. The shell provider is disposed asynchronously on host shutdown, so
    /// this — not <see cref="Dispose"/> — is the path a graceful shutdown takes; loss past the window is
    /// accepted rather than stalling shutdown indefinitely (async disposal carries no cancellation token,
    /// so the configured window is the only bound).
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
