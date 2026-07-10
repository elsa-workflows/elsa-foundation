using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Mapping;
using Elsa.Persistence.EFCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;

/// <summary>
/// Durable <see cref="IOpenTelemetryStore"/> backed by EF Core. Writes enqueue onto a bounded channel so
/// anonymous OTLP collector requests do not wait on database I/O; a single background drain loop persists
/// batches and prunes each high-volume signal table to the configured retention capacity. The channel and
/// drain lifecycle (start/complete/dispose, retries, shed accounting) live in
/// <see cref="ChannelDrainingStoreBase{TItem}"/>; this class contributes the EF Core persistence, the
/// per-signal retention pruning, and the per-signal dropped-count diagnostics.
/// </summary>
public sealed class EfCoreOpenTelemetryStore : ChannelDrainingStoreBase<OpenTelemetryBatch>, IOpenTelemetryStore
{
    private const int BatchSize = 64;
    private const int DefaultPruneInterval = 500;

    private readonly IDbContextFactory<OpenTelemetryDbContext> _dbContextFactory;
    private readonly IOpenTelemetrySourceRegistry _sourceRegistry;
    private readonly int _traceCapacity;
    private readonly int _spanCapacity;
    private readonly int _metricPointCapacity;
    private readonly int _logRecordCapacity;
    private readonly int _resourceCapacity;
    private readonly int _maxQuerySize;
    private long _droppedTraces;
    private long _droppedSpans;
    private long _droppedMetricPoints;
    private long _droppedLogRecords;

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
        : base(
            BatchSize,
            pruneInterval,
            Math.Max(BatchSize, options.Value.SubscriberChannelCapacity) * 4,
            options.Value.ShutdownDrainTimeout,
            logger ?? NullLogger<EfCoreOpenTelemetryStore>.Instance,
            baseRetryDelay)
    {
        _dbContextFactory = dbContextFactory;
        _sourceRegistry = sourceRegistry;
        var value = options.Value;
        _traceCapacity = ClampCapacity(value.TraceCapacity);
        _spanCapacity = ClampCapacity(value.SpanCapacity);
        _metricPointCapacity = ClampCapacity(value.MetricPointCapacity);
        _logRecordCapacity = ClampCapacity(value.LogRecordCapacity);
        _resourceCapacity = ClampCapacity(value.ResourceCapacity);
        _maxQuerySize = ClampCapacity(value.MaxQuerySize);
    }

    public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();

        // A false TryWrite means the writer was completed (store stopping/stopped): drop the batch without
        // marking its resources seen. This only covers the completed-writer path — under overflow, DropOldest
        // accepts the write and silently evicts the oldest queued batch, whose already-marked resources never
        // persist.
        if (!TryWrite(batch))
            return ValueTask.CompletedTask;

        foreach (var resource in batch.Resources)
            _sourceRegistry.MarkSeen(resource);

        return ValueTask.CompletedTask;
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

    protected override async Task<int> PersistBatchAsync(IReadOnlyList<OpenTelemetryBatch> batch, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        foreach (var resource in batch.SelectMany(x => x.Resources))
            await UpsertResourceAsync(db, resource, cancellationToken);
        foreach (var instrument in batch.SelectMany(x => x.Instruments))
            await UpsertInstrumentAsync(db, instrument, cancellationToken);

        foreach (var trace in batch.SelectMany(x => x.Traces))
            db.TelemetryTraces.Add(OpenTelemetryMapper.ToEntity(trace));
        foreach (var span in batch.SelectMany(x => x.Spans))
            db.TelemetrySpans.Add(OpenTelemetryMapper.ToEntity(span));
        foreach (var point in batch.SelectMany(x => x.MetricPoints))
            db.MetricPoints.Add(OpenTelemetryMapper.ToEntity(point));
        foreach (var log in batch.SelectMany(x => x.Logs))
            db.OtlpLogRecords.Add(OpenTelemetryMapper.ToEntity(log));

        var inserted = batch.Sum(x => x.Resources.Count + x.Traces.Count + x.Spans.Count + x.MetricPoints.Count + x.Logs.Count);
        await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    protected override int OnBatchDropped(IReadOnlyList<OpenTelemetryBatch> batch, Exception exception, int attempts)
    {
        CountDropped(batch);
        Logger.LogError(exception, "Dropping a telemetry batch after {MaxAttempts} failed persistence attempts: {TraceCount} traces, {SpanCount} spans, {MetricPointCount} metric points, {LogRecordCount} log records lost.",
            attempts,
            batch.Sum(x => x.Traces.Count),
            batch.Sum(x => x.Spans.Count),
            batch.Sum(x => x.MetricPoints.Count),
            batch.Sum(x => x.Logs.Count));
        // Dropped signals were never inserted, so they contribute nothing to the prune interval.
        return 0;
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

    protected override async Task PruneAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await PruneTracesAsync(db, cancellationToken);
        await PruneSpansAsync(db, cancellationToken);
        await PruneMetricPointsAsync(db, cancellationToken);
        await PruneLogsAsync(db, cancellationToken);
        await PruneResourcesAsync(db, cancellationToken);
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

    /// <summary>
    /// Every shed batch counts toward the per-signal dropped-count diagnostics, independent of the
    /// rate-limited shed warning.
    /// </summary>
    protected override void OnItemShed(OpenTelemetryBatch item) => CountDropped([item]);

    protected override void LogShedWarning(long totalShed) =>
        Logger.LogWarning("Telemetry drain channel is full; shedding the oldest queued batch ({ShedBatchCount} batches shed since startup). The database writer is not keeping up with ingest.", totalShed);

    protected override void LogTransientPersistFailure(Exception exception, int attempt, int maxAttempts, TimeSpan delay) =>
        Logger.LogDebug(exception, "Transient failure persisting a telemetry batch (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.", attempt, maxAttempts, delay);

    protected override void LogTransientPruneFailure(Exception exception, int attempt, int maxAttempts, TimeSpan delay) =>
        Logger.LogDebug(exception, "Transient failure pruning telemetry retention (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.", attempt, maxAttempts, delay);

    protected override void LogPruneGivenUp(int maxAttempts) =>
        Logger.LogWarning("Giving up pruning telemetry retention after {MaxAttempts} attempts; the next persisted batch retries.", maxAttempts);

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
}
