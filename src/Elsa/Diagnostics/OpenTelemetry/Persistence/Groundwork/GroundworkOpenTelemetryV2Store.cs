using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Clean-break OpenTelemetry adapter over Groundwork v2 ordinary storage units.  Every signal,
/// catalog, and capture ledger row is written through <see cref="IStorageSession"/>; no v1
/// diagnostic-record or document store is involved.
/// </summary>
public sealed class GroundworkOpenTelemetryStore :
    IOpenTelemetryStore,
    IDiagnosticsPersistenceDrain,
    IDiagnosticsPersistenceStartupResource,
    IAsyncDisposable
{
    private const int DrainBatchSize = 64;
    private const int MaxCaptureAttempts = 3;
    private const int TraceAggregationMaxGroups = 1_000;
    private readonly IOpenTelemetrySourceRegistry? sourceRegistry;
    private readonly V2OpenTelemetryBinding binding;
    private readonly IStorageProviderConnection connection;
    private readonly V2Sessions sessions;
    private readonly V2OpenTelemetryStorageSchemaSet schema;
    private readonly DiagnosticsDrain<OpenTelemetryBatch, bool> drain;
    private readonly StartupResource? startupResource;
    private readonly int traceCapacity;
    private readonly int spanCapacity;
    private readonly int metricPointCapacity;
    private readonly int logCapacity;
    private readonly int resourceCapacity;
    private readonly int instrumentCapacity;
    private readonly int maxQuerySize;
    private readonly TimeProvider timeProvider;
    private long droppedTraces;
    private long droppedSpans;
    private long droppedMetricPoints;
    private long droppedLogs;

    public GroundworkOpenTelemetryStore(
        IStorageProviderConnection connection,
        IOptions<OpenTelemetryDiagnosticsOptions> options,
        V2OpenTelemetryBinding binding,
        TimeProvider? timeProvider = null,
        IOpenTelemetrySourceRegistry? sourceRegistry = null,
        IDiagnosticsPersistenceObserver? observer = null)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(options);
        this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
        binding.Validate();
        this.sourceRegistry = sourceRegistry;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        schema = new();
        sessions = new();
        (traceCapacity, spanCapacity, metricPointCapacity, logCapacity, resourceCapacity, instrumentCapacity, maxQuerySize) =
            ReadOptions(options.Value);
        startupResource = new(this, connection);
        drain = CreateDrain(options.Value, observer);
    }

    public void Start() => drain.Start();

    public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        if (drain.State == DiagnosticsDrainState.Created)
            throw new InvalidOperationException("The Groundwork OpenTelemetry v2 capture drain must be started before use.");
        var accepted = drain.TryEnqueue(batch, out var acknowledgement);
        if (!accepted)
            CountDropped(batch);
        else if (sourceRegistry is not null)
            foreach (var resource in batch.Resources)
                sourceRegistry.MarkSeen(resource);
        _ = ObserveFailureAsync(batch, acknowledgement);
        return ValueTask.CompletedTask;
    }

    public Task CompleteDrainingAsync(CancellationToken cancellationToken = default) =>
        drain.StopIfStartedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        drain.StopIfStartedAsync(cancellationToken);

    ValueTask<IDiagnosticsPersistenceResourceLease> IDiagnosticsPersistenceStartupResource.AcquireAsync(
        CancellationToken cancellationToken) =>
        startupResource?.AcquireAsync(cancellationToken) ??
        ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(DirectLease.Instance);

    public ValueTask WriteAsync(
        DiagnosticsDrainBatchId batchId,
        OpenTelemetryBatch batch,
        CancellationToken cancellationToken = default) =>
        WriteDurablyAsync(batchId, batch, cancellationToken);

    public async ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(
        OpenTelemetryResourceFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var take = ClampTake(filter.Take, maxQuerySize);
        if (take == 0 || resourceCapacity == 0)
            return new([], sourceRegistry?.DroppedCount ?? 0);

        var predicates = new List<Predicate>();
        AddEqual(predicates, ResourceColumns.ServiceName, filter.ServiceName);
        if (filter.Status is { } status)
            predicates.Add(new Predicate.Equal(ResourceColumns.Status, QueryConstant.Of(ResourceColumns.Status, (long)status)));
        if (!string.IsNullOrWhiteSpace(filter.Search))
            predicates.Add(new Predicate.Or([
                new Predicate.Substring(ResourceColumns.Id, filter.Search, Anchor.Contains),
                new Predicate.Substring(ResourceColumns.ServiceName, filter.Search, Anchor.Contains)]));
        var rows = Query(
            sessions.Resources,
            predicates,
            ResourceColumns.LastSeen,
            take,
            descending: true,
            ResourceColumns.Id).Rows;
        return new(rows.Select(V2OpenTelemetryCodec.Deserialize<TelemetryResource>).ToArray(), sourceRegistry?.DroppedCount ?? 0);
    }

    public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(
        OpenTelemetryTraceFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.From, filter.To, nameof(filter));
        var take = Math.Min(Math.Min(ClampTake(filter.Take, maxQuerySize), traceCapacity), TraceAggregationMaxGroups);
        if (take == 0)
            return ValueTask.FromResult(new OpenTelemetryTraceResult([], Interlocked.Read(ref droppedTraces)));

        var source = new List<Predicate>();
        AddSubstring(source, TraceColumns.TraceId, filter.TraceId);
        AddEqual(source, TraceColumns.ResourceId, filter.ResourceId);
        AddEqual(source, TraceColumns.ServiceName, filter.ServiceName);
        AddSubstring(source, TraceColumns.WorkflowInstanceId, filter.WorkflowInstanceId);
        AddEqual(source, TraceColumns.Status, filter.Status is { } status ? (long)status : null);
        AddRange(source, TraceColumns.StartTime, filter.From, filter.To);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            source.Add(new Predicate.Or([
                new Predicate.Substring(TraceColumns.TraceId, filter.Search, Anchor.Contains),
                new Predicate.Substring(TraceColumns.Name, filter.Search, Anchor.Contains)]));

        var aggregation = sessions.Traces.Aggregate(new AggregationQuery(V2OpenTelemetryStorageSchema.TraceProfile)
        {
            SourcePredicate = All(source),
            OrderBy = V2OpenTelemetryStorageSchema.StartTime,
            OrderDirection = global::Groundwork.Kernel.SortDirection.Descending,
            Take = take
        });
        var traces = aggregation.Rows.Select(ToTrace).Reverse().ToArray();
        return ValueTask.FromResult(new OpenTelemetryTraceResult(traces, Interlocked.Read(ref droppedTraces)));
    }

    public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);
        var aggregate = sessions.Traces.Aggregate(new AggregationQuery(V2OpenTelemetryStorageSchema.TraceProfile)
        {
            SourcePredicate = new Predicate.Equal(TraceColumns.TraceId, QueryConstant.Of(TraceColumns.TraceId, traceId)),
            Take = 1
        });
        var row = aggregate.Rows.SingleOrDefault();
        if (row is null)
            return ValueTask.FromResult<OpenTelemetryTraceDetail?>(null);
        var trace = ToTrace(row);
        var tracePredicate = new[] { new Predicate.Equal(SpanColumns.TraceId, QueryConstant.Of(SpanColumns.TraceId, trace.TraceId)) };
        var spans = QueryAll(sessions.Spans, tracePredicate, SpanColumns.StartTime, maxQuerySize, cancellationToken, SpanColumns.SpanId)
            .Select(V2OpenTelemetryCodec.Deserialize<TelemetrySpan>).ToArray();
        var logs = QueryAll(sessions.Logs, tracePredicate.Select(predicate => Rebind(predicate, LogColumns.TraceId)), LogColumns.Timestamp, maxQuerySize, cancellationToken, LogColumns.Id)
            .Select(V2OpenTelemetryCodec.Deserialize<OtlpLogRecord>).ToArray();
        var resources = trace.ResourceIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => sessions.Resources.Read(new StorageKey(new Dictionary<string, object?> { [V2OpenTelemetryStorageSchema.Id] = id })))
            .Where(entry => entry is not null)
            .Select(entry => V2OpenTelemetryCodec.Deserialize<TelemetryResource>(entry!.Values.Values))
            .OrderBy(resource => resource.ServiceName, StringComparer.Ordinal)
            .ThenBy(resource => resource.Id, StringComparer.Ordinal)
            .ToArray();
        return ValueTask.FromResult<OpenTelemetryTraceDetail?>(new(trace, spans, resources, logs));
    }

    public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(
        OpenTelemetryMetricFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.From, filter.To, nameof(filter));
        var take = ClampTake(filter.Take, maxQuerySize);
        if (take == 0 || metricPointCapacity == 0)
            return ValueTask.FromResult(new OpenTelemetryMetricResult([], [], Interlocked.Read(ref droppedMetricPoints)));
        var predicates = new List<Predicate>();
        AddEqual(predicates, MetricColumns.ResourceId, filter.ResourceId);
        AddEqual(predicates, MetricColumns.ServiceName, filter.ServiceName);
        AddSubstring(predicates, MetricColumns.InstrumentName, filter.InstrumentName);
        AddRange(predicates, MetricColumns.Timestamp, filter.From, filter.To);
        var points = Query(sessions.MetricPoints, predicates, MetricColumns.Timestamp, take, descending: false, MetricColumns.Id)
            .Rows.Select(V2OpenTelemetryCodec.Deserialize<MetricPoint>).ToArray();
        var instruments = points.Select(point => point.InstrumentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => sessions.Instruments.Read(new StorageKey(new Dictionary<string, object?> { [V2OpenTelemetryStorageSchema.Id] = id })))
            .Where(entry => entry is not null)
            .Select(entry => V2OpenTelemetryCodec.Deserialize<MetricInstrument>(entry!.Values.Values))
            .OrderBy(instrument => instrument.Id, StringComparer.Ordinal)
            .ToArray();
        return ValueTask.FromResult(new OpenTelemetryMetricResult(instruments, points, Interlocked.Read(ref droppedMetricPoints)));
    }

    public ValueTask<OpenTelemetryLogResult> QueryLogsAsync(
        OpenTelemetryLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.From, filter.To, nameof(filter));
        var take = ClampTake(filter.Take, maxQuerySize);
        if (take == 0 || logCapacity == 0)
            return ValueTask.FromResult(new OpenTelemetryLogResult([], Interlocked.Read(ref droppedLogs)));
        var predicates = new List<Predicate>();
        AddEqual(predicates, LogColumns.ResourceId, filter.ResourceId);
        AddEqual(predicates, LogColumns.ServiceName, filter.ServiceName);
        AddSubstring(predicates, LogColumns.TraceId, filter.TraceId);
        AddSubstring(predicates, LogColumns.SpanId, filter.SpanId);
        AddSubstring(predicates, LogColumns.SeverityText, filter.Severity);
        AddSubstring(predicates, LogColumns.Body, filter.Search);
        AddRange(predicates, LogColumns.Timestamp, filter.From, filter.To);
        var logs = Query(sessions.Logs, predicates, LogColumns.Timestamp, take, descending: false, LogColumns.Id)
            .Rows.Select(V2OpenTelemetryCodec.Deserialize<OtlpLogRecord>).ToArray();
        return ValueTask.FromResult(new OpenTelemetryLogResult(logs, Interlocked.Read(ref droppedLogs)));
    }

    public ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new OpenTelemetryStorageDiagnostics(
            traceCapacity,
            spanCapacity,
            metricPointCapacity,
            logCapacity,
            Count(sessions.Resources, ResourceColumns.Id),
            Count(sessions.Traces, TraceColumns.Sequence),
            Count(sessions.Spans, SpanColumns.Sequence),
            Count(sessions.Instruments, InstrumentColumns.Id),
            Count(sessions.MetricPoints, MetricColumns.Sequence),
            Count(sessions.Logs, LogColumns.Sequence),
            Interlocked.Read(ref droppedTraces),
            Interlocked.Read(ref droppedSpans),
            Interlocked.Read(ref droppedMetricPoints),
            Interlocked.Read(ref droppedLogs)));
    }

    public ValueTask DisposeAsync() => drain.DisposeAsync();

    private async ValueTask WriteDurablyAsync(
        DiagnosticsDrainBatchId batchId,
        OpenTelemetryBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batchId.Value == Guid.Empty)
            throw new ArgumentException("The diagnostics drain batch identity cannot be empty.", nameof(batchId));
        cancellationToken.ThrowIfCancellationRequested();

        var services = batch.Resources.ToDictionary(resource => resource.Id, resource => resource.ServiceName, StringComparer.OrdinalIgnoreCase);
        var traces = batch.Traces.Select(trace => V2OpenTelemetryCodec.Trace(trace, ServiceFor(trace.ResourceIds, services))).ToArray();
        var spans = batch.Spans.Select(V2OpenTelemetryCodec.Span).ToArray();
        var points = batch.MetricPoints.Select(point => V2OpenTelemetryCodec.MetricPoint(point, ServiceFor([point.ResourceId], services))).ToArray();
        var logs = batch.Logs.Select(log => V2OpenTelemetryCodec.Log(log, ServiceFor([log.ResourceId], services))).ToArray();
        var resources = batch.Resources.Select(V2OpenTelemetryCodec.Resource).ToArray();
        var instruments = batch.Instruments.Select(instrument => V2OpenTelemetryCodec.Instrument(instrument, batchId.IssuedAt)).ToArray();
        var fingerprint = Fingerprint(resources, instruments, traces, spans, points, logs);
        var resourceUnit = schema.Unit(V2OpenTelemetryStorageSchema.ResourceUnitId);
        var instrumentUnit = schema.Unit(V2OpenTelemetryStorageSchema.InstrumentUnitId);
        var ledgerUnit = schema.Unit(V2OpenTelemetryStorageSchema.CaptureLedgerUnitId);
        var summaryUnit = schema.Unit(V2OpenTelemetryStorageSchema.TraceSummaryUnitId);

        if (!connection.Capabilities.Any(capability => capability.Id == WellKnownCapabilities.AtomicCommit))
        {
            throw new NotSupportedException(
                "The OpenTelemetry v3 capture contract requires Groundwork atomic multi-unit commit support.");
        }

        Exception? failure = null;
        for (var attempt = 1; attempt <= MaxCaptureAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var work = connection.BeginUnitOfWork(
                    StorageAccess.Scoped(binding.StorageScope),
                    BatchWriteOptions.Exact,
                    schema.Units.ToArray());
                var transaction = V2Sessions.Open(work, schema.Units);
                var ledgerKey = new StorageKey(new Dictionary<string, object?>
                {
                    [V2OpenTelemetryStorageSchema.BatchId] = batchId.ToString()
                });
                var existingLedger = transaction.Ledger.Read(ledgerKey);
                if (existingLedger is not null)
                {
                    EnsureLedgerMatches(existingLedger, fingerprint);
                    work.Rollback();
                    return;
                }

                foreach (var resource in resources)
                    work.Stage(RowWrite.Upsert(resourceUnit, resource));
                foreach (var instrument in instruments)
                    work.Stage(RowWrite.Upsert(instrumentUnit, instrument));

                await AppendExactAsync(transaction.Traces, traces, batchId, "traces", cancellationToken);
                await AppendExactAsync(transaction.Spans, spans, batchId, "spans", cancellationToken);
                await AppendExactAsync(transaction.MetricPoints, points, batchId, "metric-points", cancellationToken);
                await AppendExactAsync(transaction.Logs, logs, batchId, "logs", cancellationToken);

                foreach (var group in batch.Traces.GroupBy(trace => V2OpenTelemetryCodec.TraceKey(trace.TraceId), StringComparer.Ordinal))
                {
                    var key = new StorageKey(new Dictionary<string, object?>
                    {
                        [V2OpenTelemetryStorageSchema.TraceKey] = group.Key
                    });
                    var existing = transaction.TraceSummaries.Read(key);
                    var records = existing is null
                        ? group.ToArray()
                        : new[] { V2OpenTelemetryCodec.DeserializeTraceSummary(existing.Values.Values) }.Concat(group).ToArray();
                    var summary = V2OpenTelemetryCodec.TraceSummary(TelemetryTraceMerger.Merge(records));
                    var options = existing is null
                        ? WriteOptions.CreateOnly
                        : WriteOptions.IfVersion(existing.Version ?? throw new InvalidDataException(
                            "The OpenTelemetry trace summary omitted its optimistic-concurrency version."));
                    work.Stage(RowWrite.ConditionalUpsert(summaryUnit, summary, options));
                }

                work.Stage(RowWrite.Insert(ledgerUnit, V2OpenTelemetryCodec.Ledger(batchId, fingerprint)));
                var report = await work.CommitWithOutcomesAsync(cancellationToken);
                if (!report.IsSuccessful)
                    throw new IOException("The OpenTelemetry v3 atomic capture commit returned failed row outcomes.");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NotSupportedException)
            {
                throw;
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains("batch identity was reused", StringComparison.Ordinal))
            {
                throw;
            }
            catch (Exception exception) when (attempt < MaxCaptureAttempts)
            {
                failure = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        throw new IOException("The OpenTelemetry v3 atomic capture failed after retries.", failure);
    }

    private static async ValueTask AppendExactAsync(
        IStorageSession session,
        IReadOnlyList<StorageValues> values,
        DiagnosticsDrainBatchId batchId,
        string kind,
        CancellationToken cancellationToken)
    {
        if (values.Count == 0)
            return;
        var exact = session as IExactAppendStorageSession ??
            throw new NotSupportedException("The selected Groundwork provider does not advertise exact append outcomes.");
        var operation = new OperationId(batchId.IssuedAt, $"otel-v3:{batchId}:{kind}");
        var report = await exact.AppendWithOutcomesAsync(operation, values, cancellationToken);
        if (report.Outcomes.Count != values.Count)
            throw new InvalidDataException("Groundwork returned an incomplete exact append outcome report.");
    }

    private DiagnosticsDrain<OpenTelemetryBatch, bool> CreateDrain(
        OpenTelemetryDiagnosticsOptions options,
        IDiagnosticsPersistenceObserver? observer) =>
        new(
            new DrainTarget(this),
            new DiagnosticsDrainOptions
            {
                BatchSize = DrainBatchSize,
                QueueCapacity = Math.Max(DrainBatchSize, options.SubscriberChannelCapacity) * 4,
                RetentionInterval = 500,
                MaxAttempts = 3,
                BaseRetryDelay = TimeSpan.FromMilliseconds(50),
                MaxRetryDelay = TimeSpan.FromSeconds(5),
                ShutdownTimeout = options.ShutdownDrainTimeout <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : options.ShutdownDrainTimeout
            },
            observer);

    private async ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        var deleted = 0;
        deleted += sessions.Traces.ApplyRetention(new RetentionExecutionOptions { KeepNewestOverride = traceCapacity, CancellationToken = cancellationToken }).DeletedRows;
        deleted += sessions.Spans.ApplyRetention(new RetentionExecutionOptions { KeepNewestOverride = spanCapacity, CancellationToken = cancellationToken }).DeletedRows;
        deleted += sessions.MetricPoints.ApplyRetention(new RetentionExecutionOptions { KeepNewestOverride = metricPointCapacity, CancellationToken = cancellationToken }).DeletedRows;
        deleted += sessions.Logs.ApplyRetention(new RetentionExecutionOptions { KeepNewestOverride = logCapacity, CancellationToken = cancellationToken }).DeletedRows;
        deleted += sessions.Resources.ApplyRetention(new RetentionExecutionOptions { KeepNewestOverride = resourceCapacity, CancellationToken = cancellationToken }).DeletedRows;
        deleted += sessions.Instruments.ApplyRetention(new RetentionExecutionOptions { KeepNewestOverride = instrumentCapacity, CancellationToken = cancellationToken }).DeletedRows;
        return deleted;
    }

    private static void EnsureLedgerMatches(StoredEntry existing, string fingerprint)
    {
        if (!StringComparer.Ordinal.Equals(
                existing.Values.Values.GetValueOrDefault(V2OpenTelemetryStorageSchema.Fingerprint)?.ToString(),
                fingerprint))
            throw new InvalidOperationException("The OpenTelemetry v2 batch identity was reused with different content.");
    }

    private string? ServiceFor(IEnumerable<string> resourceIds, IReadOnlyDictionary<string, string> batchServices)
    {
        foreach (var resourceId in resourceIds)
        {
            if (batchServices.TryGetValue(resourceId, out var serviceName))
                return serviceName;
            var retained = sessions.Resources.Read(new StorageKey(new Dictionary<string, object?>
            {
                [V2OpenTelemetryStorageSchema.Id] = resourceId
            }));
            if (retained is not null)
                return V2OpenTelemetryCodec.Deserialize<TelemetryResource>(retained.Values.Values).ServiceName;
        }
        return null;
    }

    private static QueryMaterializedResult Query(
        IStorageSession session,
        IEnumerable<Predicate> predicates,
        ColumnRef orderColumn,
        int take,
        bool descending,
        params ColumnRef[] tieBreakers) =>
        QueryPage(session, predicates, orderColumn, take, descending, null, tieBreakers);

    private static QueryMaterializedResult QueryPage(
        IStorageSession session,
        IEnumerable<Predicate> predicates,
        ColumnRef orderColumn,
        int take,
        bool descending,
        int? offset,
        IReadOnlyList<ColumnRef> tieBreakers)
    {
        var order = ImmutableArray.CreateBuilder<OrderTerm>();
        order.Add(new(orderColumn, descending ? OrderDirection.Descending : OrderDirection.Ascending, descending ? NullOrder.First : NullOrder.Last));
        foreach (var tieBreaker in tieBreakers)
            order.Add(new(tieBreaker, OrderDirection.Ascending, NullOrder.Last));
        return session.Query(new QueryRequest(
            new TableId(session.Unit.Name),
            All(predicates.ToArray()) ?? Predicate.AlwaysTrue.Instance,
            order.ToImmutable(),
            Projection.All,
            offset is null ? Paging.Keyset(take) : Paging.OffsetLimit(offset.Value, take)));
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> QueryAll(
        IStorageSession session,
        IEnumerable<Predicate> predicates,
        ColumnRef orderColumn,
        int pageSize,
        CancellationToken cancellationToken,
        params ColumnRef[] tieBreakers)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var offset = 0;
        QueryMaterializedResult page;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            page = QueryPage(session, predicates, orderColumn, pageSize, false, offset, tieBreakers);
            rows.AddRange(page.Rows);
            offset += page.Rows.Count;
        } while (page.Rows.Count == pageSize);
        return rows;
    }

    private static int Count(IStorageSession session, ColumnRef orderColumn) =>
        checked((int)Math.Min(int.MaxValue, session.Query(new QueryRequest(
            new TableId(session.Unit.Name),
            Predicate.AlwaysTrue.Instance,
            [new OrderTerm(orderColumn, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(1),
            result: ResultShape.TotalCount.Instance)).TotalCount ?? 0));

    private static TelemetryTrace ToTrace(AggregationRow row)
    {
        var start = Value<DateTimeOffset>(row, V2OpenTelemetryStorageSchema.StartTime);
        var end = Value<DateTimeOffset>(row, V2OpenTelemetryStorageSchema.EndTime);
        return new(
            Value<string>(row, V2OpenTelemetryStorageSchema.TraceId),
            Optional<string>(row, V2OpenTelemetryStorageSchema.RootSpanId),
            Optional<string>(row, V2OpenTelemetryStorageSchema.Name),
            start,
            end,
            end - start,
            (SpanStatus)Value<long>(row, V2OpenTelemetryStorageSchema.Status),
            Values<string>(row, V2OpenTelemetryStorageSchema.ResourceId),
            Values<string>(row, V2OpenTelemetryStorageSchema.WorkflowInstanceId),
            checked((int)Value<long>(row, V2OpenTelemetryStorageSchema.SpanCount)));
    }

    private static T Value<T>(AggregationRow row, string field) =>
        row[field] switch
        {
            T value => value,
            int value when typeof(T) == typeof(long) => (T)(object)(long)value,
            long value when typeof(T) == typeof(int) => (T)(object)(int)value,
            _ => throw new InvalidDataException($"The OpenTelemetry aggregation result omitted '{field}'.")
        };

    private static T? Optional<T>(AggregationRow row, string field) where T : class => row[field] is T value ? value : null;

    private static IReadOnlyList<T> Values<T>(AggregationRow row, string field) => row[field] switch
    {
        IEnumerable<T> values => values.ToArray(),
        T value => [value],
        _ => []
    };

    private static Predicate? All(IReadOnlyList<Predicate> predicates) => predicates.Count switch
    {
        0 => null,
        1 => predicates[0],
        _ => new Predicate.And(predicates)
    };

    private static void AddEqual(ICollection<Predicate> predicates, ColumnRef column, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            predicates.Add(new Predicate.Equal(column, QueryConstant.Of(column, value)));
    }

    private static void AddEqual(ICollection<Predicate> predicates, ColumnRef column, long? value)
    {
        if (value is not null)
            predicates.Add(new Predicate.Equal(column, QueryConstant.Of(column, value.Value)));
    }

    private static void AddSubstring(ICollection<Predicate> predicates, ColumnRef column, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            predicates.Add(new Predicate.Substring(column, value, Anchor.Contains));
    }

    private static void AddRange(ICollection<Predicate> predicates, ColumnRef column, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is null && to is null)
            return;
        predicates.Add(new Predicate.Range(
            column,
            from is { } lower ? Bound.Inclusive(QueryConstant.Of(column, lower)) : null,
            to is { } upper ? Bound.Inclusive(QueryConstant.Of(column, upper)) : null));
    }

    private static Predicate Rebind(Predicate predicate, ColumnRef column) => predicate switch
    {
        Predicate.Equal equal => new Predicate.Equal(column, QueryConstant.Of(column, equal.Value.Value)),
        _ => throw new InvalidOperationException("The OpenTelemetry query predicate cannot be rebound to another unit.")
    };

    private static void ValidateRange(DateTimeOffset? from, DateTimeOffset? to, string parameterName)
    {
        if (from is { } lower && to is { } upper && lower > upper)
            throw new ArgumentException("The inclusive OpenTelemetry range start cannot be later than its end.", parameterName);
    }

    private static (int, int, int, int, int, int, int) ReadOptions(OpenTelemetryDiagnosticsOptions value) =>
        (Math.Max(0, value.TraceCapacity), Math.Max(0, value.SpanCapacity), Math.Max(0, value.MetricPointCapacity),
            Math.Max(0, value.LogRecordCapacity), Math.Max(0, value.ResourceCapacity), Math.Max(0, value.MetricInstrumentCapacity),
            Math.Max(1, value.MaxQuerySize));

    private static int ClampTake(int? requested, int max = int.MaxValue) => Math.Clamp(requested ?? max, 0, max);

    private static string Fingerprint(params IReadOnlyList<StorageValues>[] batches)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var batch in batches)
            foreach (var values in batch)
            {
                foreach (var pair in values.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    var text = $"{pair.Key}\u001f{pair.Value}";
                    var bytes = Encoding.UTF8.GetBytes(text);
                    hash.AppendData(bytes);
                    hash.AppendData([0]);
                }
                hash.AppendData([1]);
            }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private async Task ObserveFailureAsync(OpenTelemetryBatch batch, Task<bool> acknowledgement)
    {
        try { await acknowledgement.ConfigureAwait(false); }
        catch { CountDropped(batch); }
    }

    private void CountDropped(OpenTelemetryBatch batch)
    {
        Interlocked.Add(ref droppedTraces, batch.Traces.Count);
        Interlocked.Add(ref droppedSpans, batch.Spans.Count);
        Interlocked.Add(ref droppedMetricPoints, batch.MetricPoints.Count);
        Interlocked.Add(ref droppedLogs, batch.Logs.Count);
    }

    private sealed class DrainTarget(GroundworkOpenTelemetryStore owner)
        : IDiagnosticsDrainTarget<OpenTelemetryBatch, bool>
    {
        public async ValueTask<DiagnosticsDrainCommit<bool>> CommitAsync(
            DiagnosticsDrainBatch<OpenTelemetryBatch> batch,
            CancellationToken cancellationToken = default)
        {
            var results = new bool[batch.Items.Count];
            var count = 0;
            for (var index = 0; index < batch.Items.Count; index++)
            {
                var child = new DiagnosticsDrainBatchId(ChildId(batch.Id.Value, index), batch.Id.IssuedAt);
                await owner.WriteDurablyAsync(child, batch.Items[index], cancellationToken);
                results[index] = true;
                count = checked(count + batch.Items[index].Traces.Count + batch.Items[index].Spans.Count + batch.Items[index].MetricPoints.Count + batch.Items[index].Logs.Count);
            }
            return new(results, count);
        }

        public async ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
        {
            return await owner.ApplyRetentionAsync(cancellationToken);
        }

        private static Guid ChildId(Guid parent, int index)
        {
            Span<byte> input = stackalloc byte[20];
            parent.TryWriteBytes(input[..16]);
            BitConverter.TryWriteBytes(input[16..], index);
            return new(SHA256.HashData(input)[..16]);
        }
    }

    private sealed class StartupResource(GroundworkOpenTelemetryStore owner, IStorageProviderConnection connection)
    {
        private readonly Lock gate = new();
        private Exception? failure;
        private IDiagnosticsPersistenceResourceLease? lease;
        private bool attempted;

        public ValueTask<IDiagnosticsPersistenceResourceLease> AcquireAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (lease is not null)
                    return ValueTask.FromResult(lease);
                if (failure is not null)
                    throw failure;
                if (attempted)
                    throw new InvalidOperationException("OpenTelemetry v2 startup did not produce a resource lease.");
                attempted = true;
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var unit in owner.schema.Units)
                    connection.Schema.Apply(unit);
                owner.sessions.Publish(connection, owner.binding);
                var created = new Lease(owner);
                lock (gate)
                    lease = created;
                return ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(created);
            }
            catch (Exception exception)
            {
                owner.sessions.Release();
                lock (gate)
                    failure = exception;
                throw;
            }
        }
    }

    private sealed class Lease(GroundworkOpenTelemetryStore owner) : IDiagnosticsPersistenceResourceLease
    {
        private int disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.sessions.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DirectLease : IDiagnosticsPersistenceResourceLease
    {
        public static DirectLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class V2Sessions
    {
        internal IStorageSession Traces { get; private set; } = null!;
        internal IStorageSession Spans { get; private set; } = null!;
        internal IStorageSession MetricPoints { get; private set; } = null!;
        internal IStorageSession Logs { get; private set; } = null!;
        internal IStorageSession Resources { get; private set; } = null!;
        internal IStorageSession Instruments { get; private set; } = null!;
        internal IStorageSession Ledger { get; private set; } = null!;
        internal IStorageSession TraceSummaries { get; private set; } = null!;

        internal V2Sessions() { }

        internal static V2Sessions Open(IUnitOfWork work, IReadOnlyList<StorageUnit> units)
        {
            ArgumentNullException.ThrowIfNull(work);
            ArgumentNullException.ThrowIfNull(units);
            var sessions = new V2Sessions();
            sessions.Bind(units.Select(work.OpenSession).ToArray());
            return sessions;
        }

        internal void Publish(IStorageProviderConnection connection, V2OpenTelemetryBinding binding)
        {
            var access = StorageAccess.Scoped(binding.StorageScope);
            var units = new V2OpenTelemetryStorageSchemaSet().Units;
            Bind(units.Select(unit => connection.OpenSession(unit, access)).ToArray());
        }

        private void Bind(IReadOnlyList<IStorageSession> opened)
        {
            if (opened.Count != 8)
                throw new InvalidOperationException("OpenTelemetry v2 did not open all storage units.");
            Traces = opened[0];
            Spans = opened[1];
            MetricPoints = opened[2];
            Logs = opened[3];
            Resources = opened[4];
            Instruments = opened[5];
            Ledger = opened[6];
            TraceSummaries = opened[7];
            Validate();
        }

        internal void Release()
        {
            Traces = Spans = MetricPoints = Logs = Resources = Instruments = Ledger = TraceSummaries = null!;
        }

        private void Validate()
        {
            if (new[] { Traces.Unit.Id.Value, Spans.Unit.Id.Value, MetricPoints.Unit.Id.Value, Logs.Unit.Id.Value, Resources.Unit.Id.Value, Instruments.Unit.Id.Value, Ledger.Unit.Id.Value, TraceSummaries.Unit.Id.Value }.Distinct(StringComparer.Ordinal).Count() != 8)
                throw new ArgumentException("OpenTelemetry v2 sessions must each be bound to a distinct storage unit.");
        }
    }

    private sealed class V2OpenTelemetryStorageSchemaSet
    {
        internal IReadOnlyList<StorageUnit> Units { get; } = V2OpenTelemetryStorageSchema.CreateUnits();

        internal StorageUnit Unit(string id) =>
            Units.Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, id));
    }

    private static class TraceColumns
    {
        internal static ColumnRef Sequence => Column(V2OpenTelemetryStorageSchema.Sequence, QueryType.Int64, false);
        internal static ColumnRef TraceId => Column(V2OpenTelemetryStorageSchema.TraceId, QueryType.String, false, 256);
        internal static ColumnRef ResourceId => Column(V2OpenTelemetryStorageSchema.ResourceId, QueryType.String, false, 512);
        internal static ColumnRef ServiceName => Column(V2OpenTelemetryStorageSchema.ServiceName, QueryType.String, true, 512);
        internal static ColumnRef WorkflowInstanceId => Column(V2OpenTelemetryStorageSchema.WorkflowInstanceId, QueryType.String, true, 512);
        internal static ColumnRef Status => Column(V2OpenTelemetryStorageSchema.Status, QueryType.Int64, false);
        internal static ColumnRef StartTime => Column(V2OpenTelemetryStorageSchema.StartTime, QueryType.DateTimeOffset, false);
        internal static ColumnRef Name => Column(V2OpenTelemetryStorageSchema.Name, QueryType.String, true);
        private static ColumnRef Column(string name, QueryType type, bool nullable, int? max = null) => new(new TableId("elsa_otel_traces_v2"), name, type, nullable, max);
    }

    private static class SpanColumns
    {
        internal static ColumnRef Sequence => Column(V2OpenTelemetryStorageSchema.Sequence, QueryType.Int64, false);
        internal static ColumnRef TraceId => Column(V2OpenTelemetryStorageSchema.TraceId, QueryType.String, false, 256);
        internal static ColumnRef SpanId => Column(V2OpenTelemetryStorageSchema.SpanId, QueryType.String, false, 256);
        internal static ColumnRef StartTime => Column(V2OpenTelemetryStorageSchema.StartTime, QueryType.DateTimeOffset, false);
        private static ColumnRef Column(string name, QueryType type, bool nullable, int? max = null) => new(new TableId("elsa_otel_spans_v2"), name, type, nullable, max);
    }

    private static class MetricColumns
    {
        internal static ColumnRef Sequence => Column(V2OpenTelemetryStorageSchema.Sequence, QueryType.Int64, false);
        internal static ColumnRef Id => Column(V2OpenTelemetryStorageSchema.Id, QueryType.String, false, 512);
        internal static ColumnRef ResourceId => Column(V2OpenTelemetryStorageSchema.ResourceId, QueryType.String, false, 512);
        internal static ColumnRef ServiceName => Column(V2OpenTelemetryStorageSchema.ServiceName, QueryType.String, true, 512);
        internal static ColumnRef InstrumentName => Column(V2OpenTelemetryStorageSchema.InstrumentName, QueryType.String, false);
        internal static ColumnRef Timestamp => Column(V2OpenTelemetryStorageSchema.Timestamp, QueryType.DateTimeOffset, false);
        private static ColumnRef Column(string name, QueryType type, bool nullable, int? max = null) => new(new TableId("elsa_otel_metric_points_v2"), name, type, nullable, max);
    }

    private static class LogColumns
    {
        internal static ColumnRef Sequence => Column(V2OpenTelemetryStorageSchema.Sequence, QueryType.Int64, false);
        internal static ColumnRef Id => Column(V2OpenTelemetryStorageSchema.Id, QueryType.String, false, 512);
        internal static ColumnRef ResourceId => Column(V2OpenTelemetryStorageSchema.ResourceId, QueryType.String, false, 512);
        internal static ColumnRef ServiceName => Column(V2OpenTelemetryStorageSchema.ServiceName, QueryType.String, true, 512);
        internal static ColumnRef TraceId => Column(V2OpenTelemetryStorageSchema.TraceId, QueryType.String, true, 256);
        internal static ColumnRef SpanId => Column(V2OpenTelemetryStorageSchema.SpanId, QueryType.String, true, 256);
        internal static ColumnRef SeverityText => Column(V2OpenTelemetryStorageSchema.SeverityText, QueryType.String, false);
        internal static ColumnRef Body => Column(V2OpenTelemetryStorageSchema.Body, QueryType.String, false);
        internal static ColumnRef Timestamp => Column(V2OpenTelemetryStorageSchema.Timestamp, QueryType.DateTimeOffset, false);
        private static ColumnRef Column(string name, QueryType type, bool nullable, int? max = null) => new(new TableId("elsa_otel_logs_v2"), name, type, nullable, max);
    }

    private static class ResourceColumns
    {
        internal static ColumnRef Id => Column(V2OpenTelemetryStorageSchema.Id, QueryType.String, false, 512);
        internal static ColumnRef ServiceName => Column(V2OpenTelemetryStorageSchema.ServiceName, QueryType.String, false, 512);
        internal static ColumnRef Status => Column(V2OpenTelemetryStorageSchema.Status, QueryType.Int64, false);
        internal static ColumnRef LastSeen => Column(V2OpenTelemetryStorageSchema.LastSeen, QueryType.DateTimeOffset, false);
        private static ColumnRef Column(string name, QueryType type, bool nullable, int? max = null) => new(new TableId("elsa_otel_resources_v2"), name, type, nullable, max);
    }

    private static class InstrumentColumns
    {
        internal static ColumnRef Id => Column(V2OpenTelemetryStorageSchema.Id, QueryType.String, false, 512);
        private static ColumnRef Column(string name, QueryType type, bool nullable, int? max = null) => new(new TableId("elsa_otel_instruments_v2"), name, type, nullable, max);
    }
}
