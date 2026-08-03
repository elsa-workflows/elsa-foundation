using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests.Differential;

/// <summary>
/// The six behavioural dimensions the #646 differential drives against <see cref="IOpenTelemetryStore"/>.
/// Names match the structured-log seam so the divergence ledger has one vocabulary.
/// </summary>
internal enum OpenTelemetryDimension
{
    ConcurrencyConflictShape,
    ProducerOrdering,
    NullAndDefaultMaterialization,
    RollbackVisibility,
    RestartObservation,
    IdempotentReplay
}

/// <summary>
/// A normalized, provider-neutral record of what one comparand did. Values must never carry a database
/// path, connection value, or wall-clock time.
/// </summary>
internal sealed record OpenTelemetryObservation(OpenTelemetryDimension Dimension, IReadOnlyDictionary<string, string> Facts)
{
    public string Canonical => string.Join("; ", Facts.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));

    public override string ToString() => Canonical;
}

/// <summary>
/// Drives one identical operation sequence per dimension and reduces it to an observation. Every probe
/// leaves the target closed so the two stacks can run in any order without cross-contamination.
/// </summary>
internal static class OpenTelemetryDifferentialProbes
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-25T00:00:00Z");

    public static Func<OpenTelemetryDifferentialTarget, Task<OpenTelemetryObservation>> For(OpenTelemetryDimension dimension) => dimension switch
    {
        OpenTelemetryDimension.ConcurrencyConflictShape => ConcurrencyConflictShapeAsync,
        OpenTelemetryDimension.ProducerOrdering => ProducerOrderingAsync,
        OpenTelemetryDimension.NullAndDefaultMaterialization => NullAndDefaultMaterializationAsync,
        OpenTelemetryDimension.RollbackVisibility => RollbackVisibilityAsync,
        OpenTelemetryDimension.RestartObservation => RestartObservationAsync,
        OpenTelemetryDimension.IdempotentReplay => IdempotentReplayAsync,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    /// <summary>
    /// Unlike the append-only log stream, the resource and instrument catalogs are mutable and upserted,
    /// so this seam has a real last-writer-wins contest. Two batches claim the same resource id with
    /// different service names and last-seen values; the probe records which one survives and whether
    /// either write surfaced a conflict.
    /// </summary>
    private static async Task<OpenTelemetryObservation> ConcurrencyConflictShapeAsync(OpenTelemetryDifferentialTarget target)
    {
        var store = await target.OpenAsync();

        var older = Resource("resource-contested", "service-older", Now);
        var newer = Resource("resource-contested", "service-newer", Now.AddSeconds(5));

        var firstWrite = await FailureShapeAsync(() => store.WriteAsync(Batch(resources: [older])).AsTask());
        var secondWrite = await FailureShapeAsync(() => store.WriteAsync(Batch(resources: [newer])).AsTask());
        // A stale claim arriving after the newer one is the actual conflict case.
        var staleWrite = await FailureShapeAsync(() => store.WriteAsync(Batch(resources: [Resource("resource-contested", "service-stale", Now.AddSeconds(-5))])).AsTask());
        await target.FlushAsync();

        var resources = await store.QueryResourcesAsync(new() { Take = 50 });
        var survivor = resources.Items.SingleOrDefault(r => r.Id == "resource-contested");

        var facts = new Dictionary<string, string>
        {
            ["first-write"] = firstWrite,
            ["second-write"] = secondWrite,
            ["stale-write"] = staleWrite,
            ["catalog-rows-for-contested-id"] = resources.Items.Count(r => r.Id == "resource-contested").ToString(),
            ["surviving-service-name"] = survivor?.ServiceName ?? "<absent>",
            ["surviving-last-seen"] = survivor is null ? "<absent>"
                : survivor.LastSeen == Now.AddSeconds(5) ? "newest"
                : survivor.LastSeen == Now ? "first"
                : "stale"
        };

        await target.CloseAsync();
        return new(OpenTelemetryDimension.ConcurrencyConflictShape, facts);
    }

    /// <summary>Concurrent producers writing distinct traces: records loss, duplication and ordering.</summary>
    private static async Task<OpenTelemetryObservation> ProducerOrderingAsync(OpenTelemetryDifferentialTarget target)
    {
        const int perProducer = 10;
        var store = await target.OpenAsync();
        await store.WriteAsync(Batch(resources: [Resource("resource-a", "service-a", Now)]));

        await Task.WhenAll(
            Enumerable.Range(0, 2).Select(producer => Task.Run(async () =>
            {
                for (var i = 0; i < perProducer; i++)
                    await store.WriteAsync(Batch(traces: [Trace($"trace-p{producer}-{i:D2}", Now.AddSeconds(i))]));
            })).ToArray());

        await target.FlushAsync();
        var traces = await store.QueryTracesAsync(new() { Take = 200 });
        var ids = traces.Items.Select(t => t.TraceId).ToArray();

        var facts = new Dictionary<string, string>
        {
            ["written"] = (perProducer * 2).ToString(),
            ["readable"] = ids.Length.ToString(),
            ["distinct"] = ids.Distinct(StringComparer.Ordinal).Count().ToString(),
            ["loss"] = ids.Length == perProducer * 2 ? "none" : "present",
            ["dropped-count"] = traces.DroppedCount.ToString(),
            ["ordered-newest-first"] = IsDescendingByStart(traces.Items) ? "true" : "false"
        };

        await target.CloseAsync();
        return new(OpenTelemetryDimension.ProducerOrdering, facts);
    }

    /// <summary>Round-trips fully populated and fully sparse records, recording which fields survive.</summary>
    private static async Task<OpenTelemetryObservation> NullAndDefaultMaterializationAsync(OpenTelemetryDifferentialTarget target)
    {
        var store = await target.OpenAsync();

        var rich = new TelemetryResource(
            "resource-rich", "service-rich", "instance-1", "dotnet",
            new Dictionary<string, string?> { ["key"] = "value" }, Now, TelemetryResourceStatus.Active);

        var sparse = new TelemetryResource(
            "resource-sparse", "service-sparse", null, null,
            new Dictionary<string, string?>(), Now, TelemetryResourceStatus.Unknown);

        var log = new OtlpLogRecord(
            "log-1", "resource-rich", Now, "Warning", 13, "log body", "trace-detail", null,
            new Dictionary<string, string?> { ["log-key"] = "log-value" });

        await store.WriteAsync(Batch(
            resources: [rich, sparse],
            traces: [Trace("trace-detail", Now)],
            logs: [log]));
        await target.FlushAsync();

        var resources = await store.QueryResourcesAsync(new() { Take = 50 });
        var readRich = resources.Items.SingleOrDefault(r => r.Id == "resource-rich");
        var readSparse = resources.Items.SingleOrDefault(r => r.Id == "resource-sparse");
        var readLog = (await store.QueryLogsAsync(new() { Take = 50 })).Items.SingleOrDefault();

        var facts = new Dictionary<string, string>
        {
            ["rich-readable"] = readRich is null ? "false" : "true",
            ["sparse-readable"] = readSparse is null ? "false" : "true",
            ["service-instance-id"] = RoundTrip(readRich?.ServiceInstanceId, "instance-1"),
            ["sdk-language"] = RoundTrip(readRich?.TelemetrySdkLanguage, "dotnet"),
            ["resource-attributes"] = RoundTrip(readRich?.Attributes.TryGetValue("key", out var v) == true ? v : null, "value"),
            ["resource-status"] = RoundTrip(readRich?.Status.ToString(), nameof(TelemetryResourceStatus.Active)),
            ["sparse-null-instance-id"] = readSparse is null ? "unreadable" : readSparse.ServiceInstanceId is null ? "null-preserved" : "defaulted",
            ["sparse-null-sdk-language"] = readSparse is null ? "unreadable" : readSparse.TelemetrySdkLanguage is null ? "null-preserved" : "defaulted",
            ["sparse-empty-attributes"] = readSparse is null ? "unreadable" : readSparse.Attributes.Count == 0 ? "empty-preserved" : "mutated",
            ["log-severity-number"] = RoundTrip(readLog?.SeverityNumber?.ToString(), "13"),
            ["log-null-span-id"] = readLog is null ? "unreadable" : readLog.SpanId is null ? "null-preserved" : "defaulted",
            ["log-attributes"] = RoundTrip(readLog?.Attributes.TryGetValue("log-key", out var lv) == true ? lv : null, "log-value")
        };

        await target.CloseAsync();
        return new(OpenTelemetryDimension.NullAndDefaultMaterialization, facts);
    }

    /// <summary>Queued-but-unflushed writes lost to process death must not leave a partial batch visible.</summary>
    private static async Task<OpenTelemetryObservation> RollbackVisibilityAsync(OpenTelemetryDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        await store.WriteAsync(Batch(resources: [Resource("resource-a", "service-a", Now)], traces: [Trace("trace-flushed", Now)]));
        await target.FlushAsync();

        var reopened = await target.OpenAsync();
        await reopened.WriteAsync(Batch(traces: [Trace("trace-unflushed", Now.AddSeconds(1))]));
        await target.AbandonAsync();

        var afterAbandon = await target.OpenAsync();
        var traces = (await afterAbandon.QueryTracesAsync(new() { Take = 50 })).Items.Select(t => t.TraceId).ToArray();

        var facts = new Dictionary<string, string>
        {
            ["flushed-write-durable"] = traces.Contains("trace-flushed") ? "true" : "false",
            ["readable-trace-count"] = traces.Length.ToString(),
            ["partial-batch-visible"] = traces.Length > 2 ? "true" : "false"
        };

        await target.CloseAsync();
        return new(OpenTelemetryDimension.RollbackVisibility, facts);
    }

    /// <summary>A graceful stop and reopen must preserve every durable signal and its diagnostics counts.</summary>
    private static async Task<OpenTelemetryObservation> RestartObservationAsync(OpenTelemetryDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        await store.WriteAsync(Batch(
            resources: [Resource("resource-a", "service-a", Now)],
            traces: [Trace("trace-1", Now), Trace("trace-2", Now.AddSeconds(1))],
            instruments: [Instrument("instrument-a", "resource-a")],
            points: [Point("point-a", "instrument-a", "resource-a", Now)],
            logs: [Log("log-a", "resource-a", "trace-1")]));
        await target.FlushAsync();

        var before = await store.GetDiagnosticsAsync();
        await target.CloseAsync();

        var reopened = await target.OpenAsync();
        var after = await reopened.GetDiagnosticsAsync();
        var traces = await reopened.QueryTracesAsync(new() { Take = 50 });
        var detail = await reopened.GetTraceAsync("trace-1");

        var facts = new Dictionary<string, string>
        {
            ["traces-preserved"] = after.TraceCount == before.TraceCount ? "true" : "false",
            ["resources-preserved"] = after.ResourceCount == before.ResourceCount ? "true" : "false",
            ["metric-points-preserved"] = after.MetricPointCount == before.MetricPointCount ? "true" : "false",
            ["logs-preserved"] = after.LogRecordCount == before.LogRecordCount ? "true" : "false",
            ["readable-traces-after-restart"] = traces.Items.Count.ToString(),
            ["trace-detail-after-restart"] = detail is null ? "absent" : "present"
        };

        await target.CloseAsync();
        return new(OpenTelemetryDimension.RestartObservation, facts);
    }

    /// <summary>
    /// Replays the same trace id across two batches. This is the case spec 139 records as a known
    /// divergence — its merged-trace-summary objective is marked blocked because Groundwork's
    /// latest-per-key reducer returns only the newer record where the EF store merged them. If this
    /// probe reports agreement, the harness is wrong before the stores are.
    /// </summary>
    private static async Task<OpenTelemetryObservation> IdempotentReplayAsync(OpenTelemetryDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        await store.WriteAsync(Batch(resources: [Resource("resource-a", "service-a", Now)]));

        // First sighting: one span, error, no workflow correlation.
        await store.WriteAsync(Batch(traces:
        [
            new("trace-shared", "span-1", "first-name", Now, Now.AddSeconds(1), TimeSpan.FromSeconds(1),
                SpanStatus.Error, ["resource-a"], [], 1)
        ]));

        // Second sighting of the same trace: later, ok, one more span, and a workflow correlation.
        await store.WriteAsync(Batch(traces:
        [
            new("trace-shared", "span-1", "second-name", Now, Now.AddSeconds(3), TimeSpan.FromSeconds(3),
                SpanStatus.Ok, ["resource-a"], ["workflow-1"], 2)
        ]));
        await target.FlushAsync();

        var traces = await store.QueryTracesAsync(new() { Take = 50 });
        var shared = traces.Items.SingleOrDefault(t => t.TraceId == "trace-shared");

        var facts = new Dictionary<string, string>
        {
            ["rows-for-replayed-trace-id"] = traces.Items.Count(t => t.TraceId == "trace-shared").ToString(),
            ["summary-name"] = shared?.Name ?? "<absent>",
            ["summary-status"] = shared?.Status.ToString() ?? "<absent>",
            ["summary-span-count"] = shared?.SpanCount.ToString() ?? "<absent>",
            ["summary-workflow-ids"] = shared is null ? "<absent>" : shared.WorkflowInstanceIds.Count == 0 ? "none" : string.Join(",", shared.WorkflowInstanceIds.OrderBy(x => x, StringComparer.Ordinal))
        };

        await target.CloseAsync();
        return new(OpenTelemetryDimension.IdempotentReplay, facts);
    }

    private static async Task<string> FailureShapeAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return "accepted";
        }
        catch (Exception exception)
        {
            return $"threw:{exception.GetType().Name}";
        }
    }

    private static bool IsDescendingByStart(IReadOnlyCollection<TelemetryTrace> traces)
    {
        var starts = traces.Select(t => t.StartTime).ToArray();
        return starts.SequenceEqual(starts.OrderByDescending(s => s));
    }

    private static string RoundTrip(string? actual, string expected) =>
        string.IsNullOrEmpty(actual) ? "dropped" : string.Equals(actual, expected, StringComparison.Ordinal) ? "preserved" : "mutated";

    private static OpenTelemetryBatch Batch(
        IReadOnlyCollection<TelemetryResource>? resources = null,
        IReadOnlyCollection<TelemetryTrace>? traces = null,
        IReadOnlyCollection<TelemetrySpan>? spans = null,
        IReadOnlyCollection<MetricInstrument>? instruments = null,
        IReadOnlyCollection<MetricPoint>? points = null,
        IReadOnlyCollection<OtlpLogRecord>? logs = null) =>
        new(resources ?? [], traces ?? [], spans ?? [], instruments ?? [], points ?? [], logs ?? []);

    private static TelemetryResource Resource(string id, string serviceName, DateTimeOffset lastSeen) =>
        new(id, serviceName, "instance", "dotnet", new Dictionary<string, string?>(), lastSeen, TelemetryResourceStatus.Active);

    private static TelemetryTrace Trace(string traceId, DateTimeOffset start) =>
        new(traceId, "span-root", traceId, start, start.AddSeconds(1), TimeSpan.FromSeconds(1),
            SpanStatus.Ok, ["resource-a"], [], 1);

    private static MetricInstrument Instrument(string id, string resourceId) =>
        new(id, resourceId, id, "ms", "description", MetricKind.Gauge, new Dictionary<string, string?>());

    private static MetricPoint Point(string id, string instrumentId, string resourceId, DateTimeOffset timestamp) =>
        new(id, instrumentId, instrumentId, resourceId, timestamp, 1.0, null, null, new Dictionary<string, string?>(), null, null);

    private static OtlpLogRecord Log(string id, string resourceId, string traceId) =>
        new(id, resourceId, Now, "Information", 9, "body", traceId, null, new Dictionary<string, string?>());
}
