using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Provider-neutral EF Core OpenTelemetry adapter.  The four provider contexts only bind a host supplied
/// provider; this class owns the durable contract, projections and retention lifecycle.
/// </summary>
public sealed class EfOpenTelemetryStore : IOpenTelemetryStore, IDiagnosticsPersistenceDrain, IDisposable, IAsyncDisposable
{
    // One queue item is one capture identity. Keeping the drain batch at one means each all-signal
    // capture has one transaction and one replay ledger, while still retaining the shared lifecycle.
    private const int DrainBatchSize = 1;
    private const int MaxDrainAttempts = 3;
    private const int MaxSummaryRetry = 3;
    private const int MaximumAffectedSummaryKeys = 100_000;
    private static readonly TimeSpan AppendIdempotencyWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IServiceScopeFactory scopeFactory;
    private readonly EfOpenTelemetryBinding binding;
    private readonly IOpenTelemetrySourceRegistry? sourceRegistry;
    private readonly DiagnosticsDrain<OpenTelemetryBatch, bool> drain;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly TimeProvider timeProvider;
    private readonly int traceCapacity;
    private readonly int spanCapacity;
    private readonly int metricPointCapacity;
    private readonly int logCapacity;
    private readonly int resourceCapacity;
    private readonly int instrumentCapacity;
    private readonly int maxQuerySize;
    // EnqueueAsync acknowledges every accepted capture, so this adapter never silently drops a
    // signal. Keep the counters explicit because they are part of the shared diagnostics contract.
    private long droppedTraces = 0;
    private long droppedSpans = 0;
    private long droppedMetricPoints = 0;
    private long droppedLogs = 0;
    private int disposed;

    public EfOpenTelemetryStore(
        IServiceScopeFactory scopeFactory,
        IOptions<OpenTelemetryDiagnosticsOptions> options,
        EfOpenTelemetryBinding binding,
        IOpenTelemetrySourceRegistry? sourceRegistry = null,
        IDiagnosticsPersistenceObserver? observer = null,
        TimeProvider? timeProvider = null)
    {
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentNullException.ThrowIfNull(options);
        this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
        binding.Validate();
        this.sourceRegistry = sourceRegistry;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        traceCapacity = Clamp(options.Value.TraceCapacity);
        spanCapacity = Clamp(options.Value.SpanCapacity);
        metricPointCapacity = Clamp(options.Value.MetricPointCapacity);
        logCapacity = Clamp(options.Value.LogRecordCapacity);
        resourceCapacity = Clamp(options.Value.ResourceCapacity);
        instrumentCapacity = Clamp(options.Value.MetricInstrumentCapacity);
        maxQuerySize = Math.Max(1, options.Value.MaxQuerySize);
        drain = new(
            new DrainTarget(this),
            new DiagnosticsDrainOptions
            {
                BatchSize = DrainBatchSize,
                QueueCapacity = Math.Max(DrainBatchSize, options.Value.SubscriberChannelCapacity) * 4,
                RetentionInterval = 1,
                MaxAttempts = MaxDrainAttempts,
                BaseRetryDelay = RetryDelay,
                MaxRetryDelay = TimeSpan.FromSeconds(5),
                ShutdownTimeout = options.Value.ShutdownDrainTimeout <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : options.Value.ShutdownDrainTimeout
            }, observer);
    }

    public void Start() => drain.Start();
    public Task StopAsync(CancellationToken cancellationToken = default) => drain.StopIfStartedAsync(cancellationToken);
    public Task CompleteDrainingAsync(CancellationToken cancellationToken = default) => drain.StopIfStartedAsync(cancellationToken);
    public Task ApplyPendingRetentionAsync(CancellationToken cancellationToken = default) => drain.ApplyPendingRetentionAsync(cancellationToken);

    public async ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (drain.State == DiagnosticsDrainState.Created)
            throw new InvalidOperationException("The EF OpenTelemetry capture drain must be started before use.");
        ValidateBatchContent(batch);
        try
        {
            await drain.EnqueueAsync(batch, cancellationToken);
            await drain.ApplyPendingRetentionAsync(cancellationToken);
            if (sourceRegistry is not null)
                foreach (var resource in batch.Resources)
                    sourceRegistry.MarkSeen(resource);
        }
        catch (DiagnosticsDrainException exception) when (exception.InnerException is OpenTelemetryPersistenceException persistence)
        {
            throw persistence;
        }
    }

    /// <summary>Direct durable entry point used by deterministic adapter tests and replay integrations.</summary>
    public ValueTask WriteAsync(DiagnosticsDrainBatchId batchId, OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batchId.Value == Guid.Empty)
            throw new ArgumentException("The diagnostics drain batch identity cannot be empty.", nameof(batchId));
        return new(CommitDurablyAsync(batchId, batch, cancellationToken));
    }

    /// <summary>Durably applies identified captures in order; each identity remains replay-safe.</summary>
    public async ValueTask WriteGroupAsync(IReadOnlyList<(DiagnosticsDrainBatchId BatchId, OpenTelemetryBatch Batch)> batches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batches);
        if (batches.Select(x => x.BatchId.Value).Distinct().Count() != batches.Count)
            throw new ArgumentException("A durable capture group cannot carry the same batch identity twice.", nameof(batches));
        await CommitGroupDurablyAsync(batches, cancellationToken);
    }

    public async ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var take = ClampTake(filter.Take);
        if (take == 0 || resourceCapacity == 0)
            return new([], sourceRegistry?.DroppedCount ?? 0);
        return await ExecuteAsync("QueryResources", async (db, ct) =>
        {
            var query = db.Resources.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey);
            if (!string.IsNullOrWhiteSpace(filter.ServiceName))
            {
                var key = OpenTelemetrySearchKeys.ServiceName(filter.ServiceName);
                query = query.Where(x => x.ServiceNameKey == OpenTelemetrySearchKeys.Hash(key) && x.ServiceNameSearchKey == key);
            }
            if (filter.Status is { } status)
                query = query.Where(x => x.Status == (int)status);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var key = OpenTelemetrySearchKeys.Key(filter.Search, nameof(filter.Search));
                query = query.Where(x => x.IdSearchKey.Contains(key) || x.ServiceNameSearchKey.Contains(key));
            }
            var rows = await query.OrderByDescending(x => x.LastSeenTicks).ThenBy(x => x.IdOrderKey).ThenBy(x => x.IdSearchKey).Take(take).ToListAsync(ct);
            return new OpenTelemetryResourceResult(rows.Select(ToResource).ToArray(), sourceRegistry?.DroppedCount ?? 0);
        }, cancellationToken);
    }

    public async ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.From, filter.To);
        var take = Math.Min(ClampTake(filter.Take), traceCapacity);
        if (take == 0)
            return new([], Interlocked.Read(ref droppedTraces));
        return await ExecuteAsync("QueryTraces", async (db, ct) =>
        {
            var query = db.TraceSummaries.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey);
            if (!string.IsNullOrWhiteSpace(filter.TraceId))
                query = query.Where(x => x.TraceIdSearchKey.Contains(OpenTelemetrySearchKeys.Key(filter.TraceId, nameof(filter.TraceId))));
            if (filter.Status is { } status)
                query = query.Where(x => x.Status == (int)status);
            if (filter.From is { } from)
                query = query.Where(x => x.StartTimeTicks >= from.UtcTicks);
            if (filter.To is { } to)
                query = query.Where(x => x.StartTimeTicks <= to.UtcTicks);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var key = OpenTelemetrySearchKeys.Key(filter.Search, nameof(filter.Search));
                query = query.Where(x => x.TraceIdSearchKey.Contains(key) || (x.NameSearchKey != null && x.NameSearchKey.Contains(key)));
            }
            if (!string.IsNullOrWhiteSpace(filter.ResourceId))
            {
                var key = OpenTelemetrySearchKeys.ResourceId(filter.ResourceId);
                var hash = OpenTelemetrySearchKeys.Hash(key);
                query = query.Where(x => db.TraceSummaryMemberships.Any(m => m.ScopeKey == binding.ScopeKey && m.Kind == OpenTelemetryTraceSummaryMembershipKind.Resource && m.ValueKey == hash && m.ValueSearchKey == key && m.TraceKey == x.TraceKey));
            }
            if (!string.IsNullOrWhiteSpace(filter.ServiceName))
            {
                var key = OpenTelemetrySearchKeys.ServiceName(filter.ServiceName);
                var hash = OpenTelemetrySearchKeys.Hash(key);
                query = query.Where(x => db.TraceSummaryMemberships.Any(m => m.ScopeKey == binding.ScopeKey && m.Kind == OpenTelemetryTraceSummaryMembershipKind.Service && m.ValueKey == hash && m.ValueSearchKey == key && m.TraceKey == x.TraceKey));
            }
            if (!string.IsNullOrWhiteSpace(filter.WorkflowInstanceId))
                query = query.Where(x => db.TraceSummaryMemberships.Any(m => m.ScopeKey == binding.ScopeKey && m.Kind == OpenTelemetryTraceSummaryMembershipKind.WorkflowInstance && m.ValueSearchKey.Contains(OpenTelemetrySearchKeys.Key(filter.WorkflowInstanceId, nameof(filter.WorkflowInstanceId))) && m.TraceKey == x.TraceKey));
            var rows = await query.OrderByDescending(x => x.StartTimeTicks).ThenBy(x => x.TraceKey).Take(take).ToListAsync(ct);
            return new OpenTelemetryTraceResult(rows.Select(ToTrace).Reverse().ToArray(), Interlocked.Read(ref droppedTraces));
        }, cancellationToken);
    }

    public async ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);
        var traceKey = OpenTelemetrySearchKeys.TraceKey(traceId);
        return await ExecuteAsync("GetTrace", async (db, ct) =>
        {
            var summary = await db.TraceSummaries.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == binding.ScopeKey && x.TraceKey == traceKey, ct);
            var traceSearchKey = OpenTelemetrySearchKeys.TraceId(traceId);
            if (summary is null || !StringComparer.Ordinal.Equals(summary.TraceIdSearchKey, traceSearchKey))
                return null;
            // These columns are a portable integrity projection as well as a corruption probe; filters use
            // normalized membership rows, never provider JSON operators.
            _ = Deserialize<string[]>(summary.ServiceMembershipJson);
            _ = Deserialize<string[]>(summary.WorkflowMembershipJson);
            var trace = ToTrace(summary);
            var spans = await db.Spans.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey && x.TraceKey == traceKey).OrderBy(x => x.StartTimeTicks).ThenBy(x => x.SpanIdOrderKey).ThenBy(x => x.Sequence).ToListAsync(ct);
            var logs = await db.Logs.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey && x.TraceIdSearchKey == traceSearchKey).OrderBy(x => x.TimestampTicks).ThenBy(x => x.IdOrderKey).ThenBy(x => x.Sequence).ToListAsync(ct);
            var resourceIds = await db.TraceSummaryMemberships.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey && x.TraceKey == traceKey && x.Kind == OpenTelemetryTraceSummaryMembershipKind.Resource).Select(x => x.Value).ToListAsync(ct);
            var resourceKeys = resourceIds.Select(id => OpenTelemetrySearchKeys.Key(id)).ToArray();
            var resources = await db.Resources.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey && resourceKeys.Contains(x.IdSearchKey)).ToListAsync(ct);
            return new OpenTelemetryTraceDetail(trace,
                spans.Select(ToSpan).ToArray(),
                resources.OrderBy(x => x.ServiceName, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).Select(ToResource).ToArray(),
                logs.Select(ToLog).ToArray());
        }, cancellationToken);
    }

    public async ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.From, filter.To);
        var take = Math.Min(ClampTake(filter.Take), metricPointCapacity);
        if (take == 0)
            return new([], [], Interlocked.Read(ref droppedMetricPoints));
        return await ExecuteAsync("QueryMetrics", async (db, ct) =>
        {
            var query = db.MetricPoints.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey);
            if (!string.IsNullOrWhiteSpace(filter.ResourceId))
                query = query.Where(x => x.ResourceIdSearchKey == OpenTelemetrySearchKeys.ResourceId(filter.ResourceId));
            if (!string.IsNullOrWhiteSpace(filter.ServiceName))
                query = query.Where(x => x.ServiceNameKey == OpenTelemetrySearchKeys.Hash(OpenTelemetrySearchKeys.ServiceName(filter.ServiceName)));
            if (!string.IsNullOrWhiteSpace(filter.InstrumentName))
            {
                var key = OpenTelemetrySearchKeys.Key(filter.InstrumentName, nameof(filter.InstrumentName));
                query = query.Where(x => x.InstrumentNameSearchKey.Contains(key));
            }
            if (filter.From is { } from)
                query = query.Where(x => x.TimestampTicks >= from.UtcTicks);
            if (filter.To is { } to)
                query = query.Where(x => x.TimestampTicks <= to.UtcTicks);
            var points = await query.OrderByDescending(x => x.TimestampTicks).ThenBy(x => x.IdOrderKey).ThenBy(x => x.Sequence).Take(take).ToListAsync(ct);
            var instrumentKeys = points.Select(x => x.InstrumentIdSearchKey).Distinct(StringComparer.Ordinal).ToArray();
            var instruments = (await db.Instruments.AsNoTracking()
                    .Where(x => x.ScopeKey == binding.ScopeKey && instrumentKeys.Contains(x.IdSearchKey))
                    .ToListAsync(ct))
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .Select(ToInstrument)
                .ToArray();
            return new OpenTelemetryMetricResult(instruments, points.Select(ToMetricPoint).Reverse().ToArray(), Interlocked.Read(ref droppedMetricPoints));
        }, cancellationToken);
    }

    public async ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.From, filter.To);
        var take = Math.Min(ClampTake(filter.Take), logCapacity);
        if (take == 0)
            return new([], Interlocked.Read(ref droppedLogs));
        return await ExecuteAsync("QueryLogs", async (db, ct) =>
        {
            var query = db.Logs.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey);
            if (!string.IsNullOrWhiteSpace(filter.ResourceId))
                query = query.Where(x => x.ResourceIdSearchKey == OpenTelemetrySearchKeys.ResourceId(filter.ResourceId));
            if (!string.IsNullOrWhiteSpace(filter.ServiceName))
                query = query.Where(x => x.ServiceNameKey == OpenTelemetrySearchKeys.Hash(OpenTelemetrySearchKeys.ServiceName(filter.ServiceName)));
            if (!string.IsNullOrWhiteSpace(filter.TraceId))
                query = query.Where(x => x.TraceIdSearchKey != null && x.TraceIdSearchKey.Contains(OpenTelemetrySearchKeys.Key(filter.TraceId, nameof(filter.TraceId))));
            if (!string.IsNullOrWhiteSpace(filter.SpanId))
                query = query.Where(x => x.SpanIdSearchKey != null && x.SpanIdSearchKey.Contains(OpenTelemetrySearchKeys.Key(filter.SpanId, nameof(filter.SpanId))));
            if (!string.IsNullOrWhiteSpace(filter.Severity))
                query = query.Where(x => x.SeveritySearchKey.Contains(OpenTelemetrySearchKeys.Key(filter.Severity, nameof(filter.Severity))));
            if (!string.IsNullOrWhiteSpace(filter.Search))
                query = query.Where(x => x.BodySearchKey.Contains(OpenTelemetrySearchKeys.Key(filter.Search, nameof(filter.Search))));
            if (filter.From is { } from)
                query = query.Where(x => x.TimestampTicks >= from.UtcTicks);
            if (filter.To is { } to)
                query = query.Where(x => x.TimestampTicks <= to.UtcTicks);
            var rows = await query.OrderByDescending(x => x.TimestampTicks).ThenBy(x => x.IdOrderKey).ThenBy(x => x.Sequence).Take(take).ToListAsync(ct);
            return new OpenTelemetryLogResult(rows.Select(ToLog).Reverse().ToArray(), Interlocked.Read(ref droppedLogs));
        }, cancellationToken);
    }

    public async ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        await ExecuteAsync("GetDiagnostics", async (db, ct) => new OpenTelemetryStorageDiagnostics(
            traceCapacity, spanCapacity, metricPointCapacity, logCapacity,
            await db.Resources.CountAsync(x => x.ScopeKey == binding.ScopeKey, ct),
            await db.TraceSummaries.CountAsync(x => x.ScopeKey == binding.ScopeKey, ct),
            await db.Spans.CountAsync(x => x.ScopeKey == binding.ScopeKey, ct),
            await db.Instruments.CountAsync(x => x.ScopeKey == binding.ScopeKey, ct),
            await db.MetricPoints.CountAsync(x => x.ScopeKey == binding.ScopeKey, ct),
            await db.Logs.CountAsync(x => x.ScopeKey == binding.ScopeKey, ct),
            Interlocked.Read(ref droppedTraces), Interlocked.Read(ref droppedSpans), Interlocked.Read(ref droppedMetricPoints), Interlocked.Read(ref droppedLogs)), cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        drain.Dispose();
        operationGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        await drain.DisposeAsync();
        operationGate.Dispose();
    }

    private async Task CommitDurablyAsync(DiagnosticsDrainBatchId batchId, OpenTelemetryBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batchId.Value == Guid.Empty)
            throw new ArgumentException("The diagnostics drain batch identity cannot be empty.", nameof(batchId));
        await CommitCapturesDurablyAsync("Write", [(batchId, batch)], cancellationToken);
    }

    private async Task CommitGroupDurablyAsync(IReadOnlyList<(DiagnosticsDrainBatchId BatchId, OpenTelemetryBatch Batch)> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;
        foreach (var item in items)
            if (item.BatchId.Value == Guid.Empty)
                throw new ArgumentException("The diagnostics drain batch identity cannot be empty.", nameof(items));
        await CommitCapturesDurablyAsync("WriteGroup", items, cancellationToken);
    }

    private async Task CommitCapturesDurablyAsync(
        string operation,
        IReadOnlyList<(DiagnosticsDrainBatchId BatchId, OpenTelemetryBatch Batch)> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item.Batch);
            ValidateBatchContent(item.Batch);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprints = items.Select(x => Fingerprint(x.Batch)).ToArray();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            Exception? retryFailure = null;
            for (var attempt = 1; attempt <= MaxSummaryRetry; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                try
                {
                    var pending = new List<(DiagnosticsDrainBatchId BatchId, OpenTelemetryBatch Batch, string Fingerprint)>();
                    foreach (var (item, index) in items.Select((item, index) => (item, index)))
                    {
                        var now = timeProvider.GetUtcNow();
                        if (item.BatchId.IssuedAt <= now.Subtract(AppendIdempotencyWindow))
                            throw new OpenTelemetryPersistenceExpiredException(operation, "The OpenTelemetry capture operation has expired.", ScopeContext(), new InvalidOperationException("The operation identity is outside the replay window."));
                        var ledger = await db.CaptureLedger.SingleOrDefaultAsync(x => x.ScopeKey == binding.ScopeKey && x.BatchId == item.BatchId.Value, cancellationToken);
                        if (ledger is not null)
                        {
                            if (ledger.IssuedAtTicks != item.BatchId.IssuedAt.UtcTicks ||
                                !StringComparer.Ordinal.Equals(ledger.Fingerprint, fingerprints[index]))
                                throw new OpenTelemetryPersistenceConflictException(operation, "The OpenTelemetry capture operation was reused with different metadata or content.", ScopeContext(), new InvalidOperationException("Capture identity metadata or fingerprint differs."));
                        }
                        else
                            pending.Add((item.BatchId, item.Batch, fingerprints[index]));
                    }

                    if (pending.Count == 0)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return;
                    }

                    var merged = MergeBatches(pending.Select(x => x.Batch));
                    await UpsertCatalogAsync(db, merged, cancellationToken);
                    var services = await ResolveServicesAsync(db, merged, cancellationToken);
                    await AppendSignalsAsync(db, merged, services, cancellationToken);
                    await MergeSummariesAsync(db, merged, services, cancellationToken);
                    foreach (var item in pending)
                        db.CaptureLedger.Add(new OpenTelemetryCaptureLedgerEntity
                        {
                            ScopeKey = binding.ScopeKey,
                            BatchId = item.BatchId.Value,
                            Fingerprint = item.Fingerprint,
                            IssuedAtTicks = item.BatchId.IssuedAt.UtcTicks,
                            IssuedAtOffsetMinutes = Offset(item.BatchId.IssuedAt),
                            Status = 1
                        });
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
                catch (Exception exception) when (attempt < MaxSummaryRetry && IsRetryableWriteConflict(exception))
                {
                    retryFailure = exception;
                    await transaction.RollbackAsync(cancellationToken);
                    await Task.Delay(RetryDelay * attempt + TimeSpan.FromMilliseconds(Random.Shared.Next(1, 16)), cancellationToken);
                }
            }

            throw new OpenTelemetryPersistenceUnavailableException(operation, "The OpenTelemetry capture could not be committed after bounded retries.", ScopeContext(), retryFailure ?? new IOException("Optimistic-concurrency retries were exhausted."));
        }
        catch (OpenTelemetryPersistenceException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (ArgumentException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (JsonException exception) { throw new InvalidDataException("The persisted OpenTelemetry capture contains malformed JSON.", exception); }
        catch (Exception exception) { throw new OpenTelemetryPersistenceUnavailableException(operation, "The OpenTelemetry capture could not be committed.", ScopeContext(), exception); }
        finally { operationGate.Release(); }
    }

    private static OpenTelemetryBatch MergeBatches(IEnumerable<OpenTelemetryBatch> batches)
    {
        var items = batches.ToArray();
        return new(
            items.SelectMany(x => x.Resources).ToArray(),
            items.SelectMany(x => x.Traces).ToArray(),
            items.SelectMany(x => x.Spans).ToArray(),
            items.SelectMany(x => x.Instruments).ToArray(),
            items.SelectMany(x => x.MetricPoints).ToArray(),
            items.SelectMany(x => x.Logs).ToArray());
    }

    private static bool IsRetryableWriteConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException ||
        exception is DbUpdateException updateException && EfRelationalExceptionClassifier.IsUniqueConstraintViolation(updateException) ||
        EfRelationalExceptionClassifier.IsTransientWriteConflict(exception);

    private async ValueTask<DiagnosticsDrainCommit<bool>> CommitBatchAsync(DiagnosticsDrainBatch<OpenTelemetryBatch> batch, CancellationToken cancellationToken)
    {
        await CommitDurablyAsync(batch.Id, batch.Items.Single(), cancellationToken);
        return new DiagnosticsDrainCommit<bool>([true], 0);
    }

    private async Task<int> ApplyRetentionCoreAsync(CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var affected = await TrimTracesAsync(db, cancellationToken);
            // Summary recomputation must observe the post-retention raw trace set, not entities still
            // marked Deleted in this change tracker.
            await db.SaveChangesAsync(cancellationToken);
            var deleted = affected.Deleted;
            deleted += await TrimSpansAsync(db, spanCapacity, cancellationToken);
            deleted += await TrimPointsAsync(db, cancellationToken);
            deleted += await TrimLogsAsync(db, cancellationToken);
            deleted += await TrimResourcesAsync(db, cancellationToken);
            deleted += await TrimInstrumentsAsync(db, cancellationToken);
            var cutoff = timeProvider.GetUtcNow().Subtract(AppendIdempotencyWindow).UtcTicks;
            var oldLedger = await db.CaptureLedger.Where(x => x.ScopeKey == binding.ScopeKey && x.IssuedAtTicks <= cutoff).ToListAsync(cancellationToken);
            db.CaptureLedger.RemoveRange(oldLedger);
            deleted += oldLedger.Count;
            await RecomputeSummariesAsync(db, affected.Keys, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return deleted;
        }
        finally { operationGate.Release(); }
    }

    private async Task<(HashSet<string> Keys, int Deleted)> TrimTracesAsync(OpenTelemetryDbContext db, CancellationToken ct)
    {
        var expired = db.Traces
            .Where(x => x.ScopeKey == binding.ScopeKey)
            .OrderByDescending(x => x.Sequence)
            .ThenByDescending(x => x.TraceKey)
            .Skip(traceCapacity);
        var projectedKeys = await expired
            .Select(x => x.TraceKey)
            .Distinct()
            .Take(MaximumAffectedSummaryKeys + 1)
            .ToListAsync(ct);
        if (projectedKeys.Count > MaximumAffectedSummaryKeys)
            throw new OpenTelemetryPersistenceUnavailableException(
                "Retention",
                $"Trace retention affected more than {MaximumAffectedSummaryKeys} summary keys.",
                ScopeContext(),
                new InvalidOperationException("The trace-retention affected-key safety bound was exceeded."));

        var rows = await expired.ToListAsync(ct);
        var keys = projectedKeys.ToHashSet(StringComparer.Ordinal);
        db.Traces.RemoveRange(rows);
        return (keys, rows.Count);
    }

    private async Task<int> TrimSpansAsync(OpenTelemetryDbContext db, int capacity, CancellationToken ct) => await TrimAsync(db.Spans.Where(x => x.ScopeKey == binding.ScopeKey).OrderByDescending(x => x.Sequence).Skip(capacity), db, ct);
    private async Task<int> TrimPointsAsync(OpenTelemetryDbContext db, CancellationToken ct) => await TrimAsync(db.MetricPoints.Where(x => x.ScopeKey == binding.ScopeKey).OrderByDescending(x => x.Sequence).Skip(metricPointCapacity), db, ct);
    private async Task<int> TrimLogsAsync(OpenTelemetryDbContext db, CancellationToken ct) => await TrimAsync(db.Logs.Where(x => x.ScopeKey == binding.ScopeKey).OrderByDescending(x => x.Sequence).Skip(logCapacity), db, ct);
    private async Task<int> TrimResourcesAsync(OpenTelemetryDbContext db, CancellationToken ct) => await TrimAsync(db.Resources.Where(x => x.ScopeKey == binding.ScopeKey).OrderByDescending(x => x.LastSeenTicks).ThenBy(x => x.IdOrderKey).ThenBy(x => x.IdSearchKey).Skip(resourceCapacity), db, ct);
    private async Task<int> TrimInstrumentsAsync(OpenTelemetryDbContext db, CancellationToken ct) => await TrimAsync(db.Instruments.Where(x => x.ScopeKey == binding.ScopeKey).OrderByDescending(x => x.LastSeenTicks).ThenBy(x => x.IdOrderKey).ThenBy(x => x.IdSearchKey).Skip(instrumentCapacity), db, ct);

    private static async Task<int> TrimAsync<T>(IQueryable<T> query, OpenTelemetryDbContext db, CancellationToken ct) where T : class
    {
        var rows = await query.ToListAsync(ct);
        db.Set<T>().RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private async Task UpsertCatalogAsync(OpenTelemetryDbContext db, OpenTelemetryBatch batch, CancellationToken ct)
    {
        foreach (var resource in batch.Resources.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()))
        {
            var row = await db.Resources.SingleOrDefaultAsync(x => x.ScopeKey == binding.ScopeKey && x.IdSearchKey == OpenTelemetrySearchKeys.ResourceId(resource.Id), ct);
            var projection = ToResourceEntity(resource);
            if (row is null)
                db.Resources.Add(projection);
            else
                Copy(projection, row);
        }
        foreach (var instrument in batch.Instruments.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()))
        {
            var row = await db.Instruments.SingleOrDefaultAsync(x => x.ScopeKey == binding.ScopeKey && x.IdSearchKey == OpenTelemetrySearchKeys.SummaryElement(instrument.Id), ct);
            var projection = ToInstrumentEntity(instrument);
            if (row is null)
                db.Instruments.Add(projection);
            else
                Copy(projection, row);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<string, string>> ResolveServicesAsync(OpenTelemetryDbContext db, OpenTelemetryBatch batch, CancellationToken ct)
    {
        var result = batch.Resources.ToDictionary(x => x.Id, x => x.ServiceName, StringComparer.OrdinalIgnoreCase);
        var ids = batch.Traces.SelectMany(x => x.ResourceIds).Concat(batch.MetricPoints.Select(x => x.ResourceId)).Concat(batch.Logs.Select(x => x.ResourceId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0)
            return result;
        var idKeys = ids.Select(OpenTelemetrySearchKeys.ResourceId).ToArray();
        var existing = await db.Resources.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey && idKeys.Contains(x.IdSearchKey)).ToListAsync(ct);
        foreach (var row in existing)
            result.TryAdd(row.Id, row.ServiceName);
        return result;
    }

    private async Task AppendSignalsAsync(OpenTelemetryDbContext db, OpenTelemetryBatch batch, IReadOnlyDictionary<string, string> services, CancellationToken ct)
    {
        var traceSequence = await NextSequenceAsync(db.Traces, ct);
        var spanSequence = await NextSequenceAsync(db.Spans, ct);
        var pointSequence = await NextSequenceAsync(db.MetricPoints, ct);
        var logSequence = await NextSequenceAsync(db.Logs, ct);
        foreach (var trace in batch.Traces)
        {
            db.Traces.Add(ToTraceEntity(trace, traceSequence++));
        }
        foreach (var span in batch.Spans)
            db.Spans.Add(ToSpanEntity(span, spanSequence++));
        foreach (var point in batch.MetricPoints)
            db.MetricPoints.Add(ToMetricPointEntity(point, pointSequence++, services.GetValueOrDefault(point.ResourceId)));
        foreach (var log in batch.Logs)
            db.Logs.Add(ToLogEntity(log, logSequence++, services.GetValueOrDefault(log.ResourceId)));
        await db.SaveChangesAsync(ct);
    }

    private async Task MergeSummariesAsync(OpenTelemetryDbContext db, OpenTelemetryBatch batch, IReadOnlyDictionary<string, string> services, CancellationToken ct)
    {
        foreach (var group in batch.Traces.GroupBy(x => OpenTelemetrySearchKeys.TraceKey(x.TraceId), StringComparer.Ordinal))
        {
            var existing = await db.TraceSummaries.SingleOrDefaultAsync(x => x.ScopeKey == binding.ScopeKey && x.TraceKey == group.Key, ct);
            var records = new List<TelemetryTrace>();
            if (existing is not null)
                records.Add(Deserialize<TelemetryTrace>(existing.PayloadJson));
            records.AddRange(group);
            var merged = NormalizeSummary(TelemetryTraceMerger.Merge(records));
            var row = existing ?? new OpenTelemetryTraceSummaryEntity { ScopeKey = binding.ScopeKey, TraceKey = group.Key, Version = Guid.NewGuid() };
            var retainedServices = existing is null
                ? []
                : ValidatePersistedMemberships(existing.ServiceMembershipJson, nameof(existing.ServiceMembershipJson));
            if (existing is not null)
                _ = ValidatePersistedMemberships(existing.WorkflowMembershipJson, nameof(existing.WorkflowMembershipJson));
            var serviceNames = CanonicalSummaryElements(
                retainedServices.Concat(merged.ResourceIds.Select(id => services.GetValueOrDefault(id)).OfType<string>()),
                nameof(OpenTelemetryTraceSummaryEntity.ServiceMembershipJson));
            ApplySummary(row, merged, serviceNames);
            if (existing is null)
                db.TraceSummaries.Add(row);
            var memberships = await db.TraceSummaryMemberships.Where(x => x.ScopeKey == binding.ScopeKey && x.TraceKey == group.Key).ToListAsync(ct);
            db.TraceSummaryMemberships.RemoveRange(memberships);
            await db.SaveChangesAsync(ct);
            foreach (var value in merged.ResourceIds)
                AddMembership(db, group.Key, OpenTelemetryTraceSummaryMembershipKind.Resource, value);
            foreach (var value in merged.WorkflowInstanceIds)
                AddMembership(db, group.Key, OpenTelemetryTraceSummaryMembershipKind.WorkflowInstance, value);
            foreach (var value in serviceNames)
                AddMembership(db, group.Key, OpenTelemetryTraceSummaryMembershipKind.Service, value);
        }
    }

    private async Task RecomputeSummariesAsync(OpenTelemetryDbContext db, IEnumerable<string> keys, CancellationToken ct)
    {
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            var records = await db.Traces.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey && x.TraceKey == key).OrderBy(x => x.Sequence).ToListAsync(ct);
            var summary = await db.TraceSummaries.SingleOrDefaultAsync(x => x.ScopeKey == binding.ScopeKey && x.TraceKey == key, ct);
            var memberships = await db.TraceSummaryMemberships.Where(x => x.ScopeKey == binding.ScopeKey && x.TraceKey == key).ToListAsync(ct);
            if (records.Count == 0)
            {
                if (summary is not null)
                    db.TraceSummaries.Remove(summary);
                db.TraceSummaryMemberships.RemoveRange(memberships);
                continue;
            }
            var merged = NormalizeSummary(TelemetryTraceMerger.Merge(records.Select(x => Deserialize<TelemetryTrace>(x.PayloadJson)).ToArray()));
            if (summary is null)
            { summary = new OpenTelemetryTraceSummaryEntity { ScopeKey = binding.ScopeKey, TraceKey = key }; db.TraceSummaries.Add(summary); }
            var ids = merged.ResourceIds.Select(OpenTelemetrySearchKeys.ResourceId).ToArray();
            var resources = await db.Resources.AsNoTracking().Where(x => x.ScopeKey == binding.ScopeKey && ids.Contains(x.IdSearchKey)).ToListAsync(ct);
            var serviceNames = CanonicalSummaryElements(resources.Select(x => x.ServiceName), nameof(OpenTelemetryTraceSummaryEntity.ServiceMembershipJson));
            ApplySummary(summary, merged, serviceNames);
            db.TraceSummaryMemberships.RemoveRange(memberships);
            await db.SaveChangesAsync(ct);
            foreach (var value in merged.ResourceIds)
                AddMembership(db, key, OpenTelemetryTraceSummaryMembershipKind.Resource, value);
            foreach (var value in merged.WorkflowInstanceIds)
                AddMembership(db, key, OpenTelemetryTraceSummaryMembershipKind.WorkflowInstance, value);
            foreach (var value in serviceNames)
                AddMembership(db, key, OpenTelemetryTraceSummaryMembershipKind.Service, value);
        }
    }

    private static TelemetryTrace NormalizeSummary(TelemetryTrace trace)
    {
        ValidateSummary(trace);
        return trace with
        {
            Name = string.IsNullOrWhiteSpace(trace.Name) ? null : trace.Name,
            ResourceIds = CanonicalSummaryElements(trace.ResourceIds, nameof(trace.ResourceIds)),
            WorkflowInstanceIds = CanonicalSummaryElements(trace.WorkflowInstanceIds, nameof(trace.WorkflowInstanceIds))
        };
    }

    private static string[] CanonicalSummaryElements(IEnumerable<string> values, string field)
    {
        var canonical = values
            .Select((value, index) => (Value: value, Key: OpenTelemetrySearchKeys.SummaryElement(value)))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => group.OrderBy(x => x.Value, StringComparer.Ordinal).First())
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Value)
            .ToArray();
        if (canonical.Length > OpenTelemetrySearchKeys.MaximumSummaryElementCount)
            throw new ArgumentOutOfRangeException(field, canonical.Length, $"OpenTelemetry field '{field}' exceeds the declared {OpenTelemetrySearchKeys.MaximumSummaryElementCount}-element bound.");
        return canonical;
    }

    private static string[] ValidatePersistedMemberships(string json, string field)
    {
        var values = Deserialize<string[]>(json);
        var canonical = CanonicalSummaryElements(values, field);
        if (!values.SequenceEqual(canonical, StringComparer.Ordinal))
            throw new InvalidDataException($"The persisted OpenTelemetry field '{field}' was not strictly ordered and unique.");
        return values;
    }

    private static void ApplySummary(OpenTelemetryTraceSummaryEntity row, TelemetryTrace trace, IReadOnlyCollection<string> serviceNames)
    {
        row.TraceId = trace.TraceId;
        row.TraceIdSearchKey = OpenTelemetrySearchKeys.TraceId(trace.TraceId);
        row.RootSpanId = trace.RootSpanId;
        row.Name = trace.Name;
        row.NameSearchKey = trace.Name is null ? null : OpenTelemetrySearchKeys.SummaryName(trace.Name);
        row.Status = (int)trace.Status;
        row.StartTimeTicks = trace.StartTime.UtcTicks;
        row.StartTimeOffsetMinutes = Offset(trace.StartTime);
        row.EndTimeTicks = trace.EndTime.UtcTicks;
        row.EndTimeOffsetMinutes = Offset(trace.EndTime);
        row.SpanCount = trace.SpanCount;
        row.PayloadJson = Serialize(trace);
        row.ServiceMembershipJson = Serialize(serviceNames);
        row.WorkflowMembershipJson = Serialize(trace.WorkflowInstanceIds);
        row.Version = Guid.NewGuid();
    }

    private void AddMembership(OpenTelemetryDbContext db, string traceKey, OpenTelemetryTraceSummaryMembershipKind kind, string value)
    {
        var searchKey = OpenTelemetrySearchKeys.SummaryElement(value);
        db.TraceSummaryMemberships.Add(new OpenTelemetryTraceSummaryMembershipEntity { ScopeKey = binding.ScopeKey, TraceKey = traceKey, Kind = kind, Value = value, ValueSearchKey = searchKey, ValueKey = OpenTelemetrySearchKeys.Hash(searchKey) });
    }

    private async Task<long> NextSequenceAsync<T>(DbSet<T> set, CancellationToken ct) where T : EfOpenTelemetrySignalEntity => (await set.Where(x => x.ScopeKey == binding.ScopeKey).Select(x => (long?)x.Sequence).MaxAsync(ct) ?? 0) + 1;

    private async Task<T> ExecuteAsync<T>(string operation, Func<OpenTelemetryDbContext, CancellationToken, Task<T>> callback, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await operationGate.WaitAsync(ct);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenTelemetryDbContext>();
            return await callback(db, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OpenTelemetryPersistenceException) { throw; }
        catch (JsonException exception) { throw new OpenTelemetryPersistenceDataException(operation, "The durable OpenTelemetry payload is malformed.", ScopeContext(), exception); }
        catch (Exception exception) { throw new OpenTelemetryPersistenceUnavailableException(operation, "The OpenTelemetry persistence provider failed.", ScopeContext(), exception); }
        finally { operationGate.Release(); }
    }

    private Dictionary<string, string> ScopeContext() => new() { ["scope"] = binding.ScopeKey };
    private static int Clamp(int value) => Math.Max(0, value);
    private int ClampTake(int? take) => Math.Clamp(take ?? maxQuerySize, 0, maxQuerySize);
    private static void ValidateRange(DateTimeOffset? from, DateTimeOffset? to) { if (from is { } f && to is { } t && f > t) throw new ArgumentException("The OpenTelemetry time range is invalid."); }
    private static void ValidateBatchShape(OpenTelemetryBatch batch) { ArgumentNullException.ThrowIfNull(batch.Resources); ArgumentNullException.ThrowIfNull(batch.Traces); ArgumentNullException.ThrowIfNull(batch.Spans); ArgumentNullException.ThrowIfNull(batch.Instruments); ArgumentNullException.ThrowIfNull(batch.MetricPoints); ArgumentNullException.ThrowIfNull(batch.Logs); }
    private static void ValidateBatchContent(OpenTelemetryBatch batch)
    {
        ValidateBatchShape(batch);
        foreach (var trace in batch.Traces)
            ValidateSummary(trace);
        foreach (var resource in batch.Resources)
        {
            _ = OpenTelemetrySearchKeys.ResourceId(resource.Id);
            _ = OpenTelemetrySearchKeys.ServiceName(resource.ServiceName);
        }
        foreach (var span in batch.Spans)
        {
            _ = OpenTelemetrySearchKeys.SignalId(span.Id);
            _ = OpenTelemetrySearchKeys.TraceId(span.TraceId);
            _ = OpenTelemetrySearchKeys.SpanId(span.SpanId);
            _ = OpenTelemetrySearchKeys.ResourceId(span.ResourceId);
            _ = OpenTelemetrySearchKeys.RequiredKey(span.Name, nameof(span.Name));
        }
        foreach (var instrument in batch.Instruments)
        {
            _ = OpenTelemetrySearchKeys.SummaryElement(instrument.Id);
            _ = OpenTelemetrySearchKeys.ResourceId(instrument.ResourceId);
            _ = OpenTelemetrySearchKeys.RequiredKey(instrument.Name, nameof(instrument.Name));
        }
        foreach (var point in batch.MetricPoints)
        {
            _ = OpenTelemetrySearchKeys.SignalId(point.Id);
            _ = OpenTelemetrySearchKeys.InstrumentId(point.InstrumentId);
            _ = OpenTelemetrySearchKeys.RequiredKey(point.InstrumentName, nameof(point.InstrumentName));
            _ = OpenTelemetrySearchKeys.ResourceId(point.ResourceId);
        }
        foreach (var log in batch.Logs)
        {
            _ = OpenTelemetrySearchKeys.SignalId(log.Id);
            _ = OpenTelemetrySearchKeys.ResourceId(log.ResourceId);
            if (log.TraceId is not null)
                _ = OpenTelemetrySearchKeys.TraceId(log.TraceId);
            if (log.SpanId is not null)
                _ = OpenTelemetrySearchKeys.LogSpanId(log.SpanId);
            _ = OpenTelemetrySearchKeys.RequiredKey(log.SeverityText, nameof(log.SeverityText));
            _ = OpenTelemetrySearchKeys.RequiredKey(log.Body, nameof(log.Body));
        }
    }
    private static short Offset(DateTimeOffset value) => checked((short)value.Offset.TotalMinutes);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);
    private static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, SerializerOptions) ?? throw new JsonException("The persisted OpenTelemetry payload was empty.");

    private string Fingerprint(OpenTelemetryBatch batch)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, batch.Resources);
        Append(hash, batch.Instruments);
        Append(hash, batch.Traces);
        Append(hash, batch.Spans);
        Append(hash, batch.MetricPoints);
        Append(hash, batch.Logs);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static void Append<T>(IncrementalHash hash, IEnumerable<T> values) { foreach (var value in values) { var bytes = Encoding.UTF8.GetBytes(Serialize(value)); hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes); } }

    private OpenTelemetryResourceEntity ToResourceEntity(TelemetryResource x) => new() { ScopeKey = binding.ScopeKey, Id = x.Id, IdSearchKey = OpenTelemetrySearchKeys.ResourceId(x.Id), IdOrderKey = OpenTelemetrySearchKeys.OrderKey(x.Id), ServiceName = x.ServiceName, ServiceNameSearchKey = OpenTelemetrySearchKeys.ServiceName(x.ServiceName), ServiceNameKey = OpenTelemetrySearchKeys.Hash(OpenTelemetrySearchKeys.ServiceName(x.ServiceName)), Status = (int)x.Status, LastSeenTicks = x.LastSeen.UtcTicks, LastSeenOffsetMinutes = Offset(x.LastSeen), PayloadJson = Serialize(x) };
    private OpenTelemetryTraceEntity ToTraceEntity(TelemetryTrace x, long sequence)
    {
        ValidateSummary(x);
        return new() { ScopeKey = binding.ScopeKey, Sequence = sequence, Id = x.TraceId, IdSearchKey = OpenTelemetrySearchKeys.TraceId(x.TraceId), IdOrderKey = OpenTelemetrySearchKeys.OrderKey(x.TraceId), TraceId = x.TraceId, TraceIdSearchKey = OpenTelemetrySearchKeys.TraceId(x.TraceId), TraceKey = OpenTelemetrySearchKeys.TraceKey(x.TraceId), RootSpanId = x.RootSpanId, Name = x.Name, NameSearchKey = string.IsNullOrWhiteSpace(x.Name) ? null : OpenTelemetrySearchKeys.SummaryName(x.Name), Status = (int)x.Status, StartTimeTicks = x.StartTime.UtcTicks, StartTimeOffsetMinutes = Offset(x.StartTime), EndTimeTicks = x.EndTime.UtcTicks, EndTimeOffsetMinutes = Offset(x.EndTime), SpanCount = x.SpanCount, PayloadJson = Serialize(x) };
    }

    private static void ValidateSummary(TelemetryTrace trace)
    {
        _ = OpenTelemetrySearchKeys.TraceId(trace.TraceId);
        if (trace.RootSpanId is not null)
            _ = OpenTelemetrySearchKeys.TraceId(trace.RootSpanId);
        if (!string.IsNullOrWhiteSpace(trace.Name))
            _ = OpenTelemetrySearchKeys.SummaryName(trace.Name);
        if (trace.ResourceIds.Count == 0)
            throw new ArgumentException("OpenTelemetry trace summaries require at least one resource identity.", nameof(trace));
        if (trace.ResourceIds.Count > OpenTelemetrySearchKeys.MaximumSummaryElementCount || trace.WorkflowInstanceIds.Count > OpenTelemetrySearchKeys.MaximumSummaryElementCount)
            throw new ArgumentOutOfRangeException(nameof(trace), "OpenTelemetry trace summary membership cardinality exceeds 5000.");
        foreach (var value in trace.ResourceIds.Concat(trace.WorkflowInstanceIds))
            _ = OpenTelemetrySearchKeys.SummaryElement(value);
    }
    private OpenTelemetrySpanEntity ToSpanEntity(TelemetrySpan x, long sequence) => new() { ScopeKey = binding.ScopeKey, Sequence = sequence, Id = x.Id, IdSearchKey = OpenTelemetrySearchKeys.SignalId(x.Id), IdOrderKey = OpenTelemetrySearchKeys.OrderKey(x.Id), TraceId = x.TraceId, TraceIdSearchKey = OpenTelemetrySearchKeys.TraceId(x.TraceId), TraceKey = OpenTelemetrySearchKeys.TraceKey(x.TraceId), SpanId = x.SpanId, SpanIdSearchKey = OpenTelemetrySearchKeys.SpanId(x.SpanId), SpanIdOrderKey = OpenTelemetrySearchKeys.OrderKey(x.SpanId), ResourceId = x.ResourceId, ResourceIdSearchKey = OpenTelemetrySearchKeys.ResourceId(x.ResourceId), Name = x.Name, NameSearchKey = OpenTelemetrySearchKeys.RequiredKey(x.Name, nameof(x.Name)), Status = (int)x.Status, StartTimeTicks = x.StartTime.UtcTicks, StartTimeOffsetMinutes = Offset(x.StartTime), EndTimeTicks = x.EndTime.UtcTicks, EndTimeOffsetMinutes = Offset(x.EndTime), PayloadJson = Serialize(x) };
    private OpenTelemetryMetricInstrumentEntity ToInstrumentEntity(MetricInstrument x) => new() { ScopeKey = binding.ScopeKey, Id = x.Id, IdSearchKey = OpenTelemetrySearchKeys.SummaryElement(x.Id), IdOrderKey = OpenTelemetrySearchKeys.OrderKey(x.Id), ResourceId = x.ResourceId, ResourceIdSearchKey = OpenTelemetrySearchKeys.ResourceId(x.ResourceId), Name = x.Name, NameSearchKey = OpenTelemetrySearchKeys.RequiredKey(x.Name, nameof(x.Name)), LastSeenTicks = timeProvider.GetUtcNow().UtcTicks, LastSeenOffsetMinutes = 0, PayloadJson = Serialize(x) };
    private OpenTelemetryMetricPointEntity ToMetricPointEntity(MetricPoint x, long sequence, string? service) => new() { ScopeKey = binding.ScopeKey, Sequence = sequence, Id = x.Id, IdSearchKey = OpenTelemetrySearchKeys.SignalId(x.Id), IdOrderKey = OpenTelemetrySearchKeys.OrderKey(x.Id), InstrumentId = x.InstrumentId, InstrumentIdSearchKey = OpenTelemetrySearchKeys.InstrumentId(x.InstrumentId), InstrumentName = x.InstrumentName, InstrumentNameSearchKey = OpenTelemetrySearchKeys.RequiredKey(x.InstrumentName, nameof(x.InstrumentName)), ResourceId = x.ResourceId, ResourceIdSearchKey = OpenTelemetrySearchKeys.ResourceId(x.ResourceId), ServiceName = service, ServiceNameKey = service is null ? null : OpenTelemetrySearchKeys.Hash(OpenTelemetrySearchKeys.ServiceName(service)), TimestampTicks = x.Timestamp.UtcTicks, TimestampOffsetMinutes = Offset(x.Timestamp), PayloadJson = Serialize(x) };
    private OpenTelemetryLogEntity ToLogEntity(OtlpLogRecord x, long sequence, string? service) => new() { ScopeKey = binding.ScopeKey, Sequence = sequence, Id = x.Id, IdSearchKey = OpenTelemetrySearchKeys.SignalId(x.Id), IdOrderKey = OpenTelemetrySearchKeys.OrderKey(x.Id), ResourceId = x.ResourceId, ResourceIdSearchKey = OpenTelemetrySearchKeys.ResourceId(x.ResourceId), ServiceName = service, ServiceNameKey = service is null ? null : OpenTelemetrySearchKeys.Hash(OpenTelemetrySearchKeys.ServiceName(service)), TraceId = x.TraceId, TraceIdSearchKey = x.TraceId is null ? null : OpenTelemetrySearchKeys.TraceId(x.TraceId), SpanId = x.SpanId, SpanIdSearchKey = x.SpanId is null ? null : OpenTelemetrySearchKeys.LogSpanId(x.SpanId), SeverityText = x.SeverityText, SeveritySearchKey = OpenTelemetrySearchKeys.RequiredKey(x.SeverityText, nameof(x.SeverityText)), SeverityNumber = x.SeverityNumber, Body = x.Body, BodySearchKey = OpenTelemetrySearchKeys.RequiredKey(x.Body, nameof(x.Body)), TimestampTicks = x.Timestamp.UtcTicks, TimestampOffsetMinutes = Offset(x.Timestamp), PayloadJson = Serialize(x) };

    private static void Copy(OpenTelemetryResourceEntity from, OpenTelemetryResourceEntity to) { to.IdSearchKey = from.IdSearchKey; to.IdOrderKey = from.IdOrderKey; to.ServiceName = from.ServiceName; to.ServiceNameSearchKey = from.ServiceNameSearchKey; to.ServiceNameKey = from.ServiceNameKey; to.Status = from.Status; to.LastSeenTicks = from.LastSeenTicks; to.LastSeenOffsetMinutes = from.LastSeenOffsetMinutes; to.PayloadJson = from.PayloadJson; }
    private static void Copy(OpenTelemetryMetricInstrumentEntity from, OpenTelemetryMetricInstrumentEntity to) { to.IdSearchKey = from.IdSearchKey; to.IdOrderKey = from.IdOrderKey; to.ResourceId = from.ResourceId; to.ResourceIdSearchKey = from.ResourceIdSearchKey; to.Name = from.Name; to.NameSearchKey = from.NameSearchKey; to.LastSeenTicks = from.LastSeenTicks; to.LastSeenOffsetMinutes = from.LastSeenOffsetMinutes; to.PayloadJson = from.PayloadJson; }
    private static TelemetryResource ToResource(OpenTelemetryResourceEntity x) => Deserialize<TelemetryResource>(x.PayloadJson) with { LastSeen = DateTimeOffsetFrom(x.LastSeenTicks, x.LastSeenOffsetMinutes) };
    private static TelemetryTrace ToTrace(OpenTelemetryTraceSummaryEntity x) => Deserialize<TelemetryTrace>(x.PayloadJson) with { StartTime = DateTimeOffsetFrom(x.StartTimeTicks, x.StartTimeOffsetMinutes), EndTime = DateTimeOffsetFrom(x.EndTimeTicks, x.EndTimeOffsetMinutes), Duration = DateTimeOffsetFrom(x.EndTimeTicks, x.EndTimeOffsetMinutes) - DateTimeOffsetFrom(x.StartTimeTicks, x.StartTimeOffsetMinutes) };
    private static TelemetrySpan ToSpan(OpenTelemetrySpanEntity x) => Deserialize<TelemetrySpan>(x.PayloadJson) with { StartTime = DateTimeOffsetFrom(x.StartTimeTicks, x.StartTimeOffsetMinutes), EndTime = DateTimeOffsetFrom(x.EndTimeTicks, x.EndTimeOffsetMinutes) };
    private static MetricInstrument ToInstrument(OpenTelemetryMetricInstrumentEntity x) => Deserialize<MetricInstrument>(x.PayloadJson);
    private static MetricPoint ToMetricPoint(OpenTelemetryMetricPointEntity x) => Deserialize<MetricPoint>(x.PayloadJson) with { Timestamp = DateTimeOffsetFrom(x.TimestampTicks, x.TimestampOffsetMinutes) };
    private static OtlpLogRecord ToLog(OpenTelemetryLogEntity x) => Deserialize<OtlpLogRecord>(x.PayloadJson) with { Timestamp = DateTimeOffsetFrom(x.TimestampTicks, x.TimestampOffsetMinutes) };
    private static DateTimeOffset DateTimeOffsetFrom(long utcTicks, short offsetMinutes) => new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

    private sealed class DrainTarget(EfOpenTelemetryStore owner) : IDiagnosticsDrainTarget<OpenTelemetryBatch, bool>
    {
        public async ValueTask<DiagnosticsDrainCommit<bool>> CommitAsync(DiagnosticsDrainBatch<OpenTelemetryBatch> batch, CancellationToken cancellationToken = default) => await owner.CommitBatchAsync(batch, cancellationToken);
        public ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default) => new(owner.ApplyRetentionCoreAsync(cancellationToken));
    }
}
