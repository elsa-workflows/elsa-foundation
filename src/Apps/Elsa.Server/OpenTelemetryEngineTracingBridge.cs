using System.Collections.Concurrent;
using System.Diagnostics;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Elsa.Server;

/// <summary>
/// Host-local composition seam that bridges the workflow engine's self-instrumentation (MS-9) into the OpenTelemetry
/// ingestion domain so Studio's timing view is populated on a self-contained demo server.
/// </summary>
/// <remarks>
/// <para>
/// The two telemetry surfaces are independent by design (see <c>docs/reference/engine-telemetry.md</c>): the engine
/// <b>emits</b> <see cref="ActivitySource"/> spans on <see cref="WorkflowEngineTelemetry.ActivitySourceName"/>, while
/// <c>DiagnosticsOpenTelemetry</c> is a <b>backend</b> that only receives OTLP telemetry pushed by other processes. With
/// neither an OpenTelemetry SDK exporter nor a listener wired up, nothing subscribes the engine source and nothing lands
/// in the ingestion store, so <c>POST /diagnostics/opentelemetry/traces/search</c> stays empty even after workflow runs.
/// </para>
/// <para>
/// This feature closes that gap the way the docs prescribe — "a host wires ... a bare listener to the source name" —
/// without adding an OpenTelemetry SDK or an HTTP loopback exporter (which would fight the dev TLS cert). It attaches an
/// in-process <see cref="ActivityListener"/> that samples the engine source and, when the outermost drain span of a
/// trace completes, folds its span tree into an <see cref="OpenTelemetryBatch"/> and feeds it to <see cref="IOpenTelemetryIngestor"/> — the same
/// contract the OTLP/HTTP receiver uses — so the batch flows through redaction, the store, and the live feed unchanged.
/// </para>
/// </remarks>
[ShellFeature(
    name: "DiagnosticsOpenTelemetryEngineBridge",
    DisplayName = "Diagnostics: OpenTelemetry Engine Bridge",
    Description = "Forwards the workflow engine's ActivitySource spans into the OpenTelemetry ingestion store so the timing view is populated in a self-contained host.",
    DependsOn = new object[] { "DiagnosticsOpenTelemetry", "WorkflowsRuntimeTracing" })]
// Must be public: CShells feature discovery scans exported types only, so an internal feature class never
// enters the runtime feature catalog and is silently dropped from every shell that requests it.
public sealed class OpenTelemetryEngineTracingBridgeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<OpenTelemetryEngineTracingBridge>();
        services.AddScoped<IStartupTask, StartEngineTracingBridgeStartupTask>();
        services.AddShellTerminator<StopEngineTracingBridgeTerminator>(LifecyclePhase.Default, 0);
    }
}

/// <summary>Attaches the bridge listener once the shell has started.</summary>
internal sealed class StartEngineTracingBridgeStartupTask(OpenTelemetryEngineTracingBridge bridge) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        bridge.Start();
        return Task.CompletedTask;
    }
}

/// <summary>Detaches the bridge listener on graceful shutdown.</summary>
internal sealed class StopEngineTracingBridgeTerminator(OpenTelemetryEngineTracingBridge bridge) : IShellTerminator
{
    public Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        bridge.Stop();
        return Task.CompletedTask;
    }
}

/// <summary>Tuning knobs for the bridge's pending-trace buffer; defaults are production values, overridden in tests.</summary>
internal sealed class OpenTelemetryEngineTracingBridgeOptions
{
    /// <summary>Minimum interval between opportunistic sweeps of the pending-trace table.</summary>
    public long SweepIntervalMs { get; init; } = 30_000;

    /// <summary>Age after which a pending trace with spans still open is dropped as abandoned.</summary>
    public long MaxPendingAgeMs { get; init; } = 5 * 60_000;

    /// <summary>
    /// Quiet period after which a complete drainless entry (no open spans, no outermost drain observed — e.g. the
    /// coalescing quiescence flush commit) is published by the sweep instead of waiting for a drain that never comes.
    /// </summary>
    public long QuietPublishDelayMs { get; init; } = 30_000;

    /// <summary>Cap on concurrently buffered pending traces; oldest-first eviction beyond it.</summary>
    public int MaxPendingTraces { get; init; } = 512;

    /// <summary>Monotonic millisecond clock; tests substitute a controllable source.</summary>
    public Func<long> TickSource { get; init; } = static () => Environment.TickCount64;
}

/// <summary>
/// Subscribes the engine <see cref="ActivitySource"/> and forwards completed span trees to the OpenTelemetry ingestor.
/// </summary>
internal sealed class OpenTelemetryEngineTracingBridge
{
    // Mirrors the OTLP parser's resource shape (resource id == service name when no instance id is present).
    private const string ServiceName = "elsa-workflow-engine";

    private readonly IOpenTelemetryIngestor _ingestor;
    private readonly ILogger<OpenTelemetryEngineTracingBridge> _logger;
    private readonly OpenTelemetryEngineTracingBridgeOptions _options;
    private readonly Func<long> _ticks;

    // Spans complete child-first and the OUTERMOST drain span last, so a trace's spans are buffered under its trace id
    // until a drain the engine tagged outermost (elsa.drain.outermost — set by StartDrainCycle, which knows nesting)
    // stops with no other engine span of the trace still open; at that point the whole tree is folded into one batch —
    // matching the OTLP receiver, which also derives the TelemetryTrace from the batch's spans. Nested drains
    // (ChildStartExecutor runs a child workflow's drain inside the parent's) carry no outermost tag and stop while the
    // parent's dispatch span is still open, so they buffer like any child span. A synchronous multi-cycle drain
    // publishes one batch per cycle under the shared ASP.NET request trace id; the stores merge those records into one
    // trace summary, so per-cycle batches are correct here.
    //
    // The open-span count deliberately covers EVERY engine span, not just drains: it doubles as the completeness check
    // for DRAINLESS entries — engine spans that never sit under a drain, concretely the checkpoint-commit span the
    // coalescing quiescence flush emits after the drain loop. Those entries are published by the sweep once quiet
    // (instead of being dropped); a drain-only depth counter could never tell whether such an entry is complete.
    private readonly ConcurrentDictionary<ActivityTraceId, PendingTrace> _pending = new();
    private long _lastSweepTicks;
    private ActivityListener? _listener;
    private int _started;

    public OpenTelemetryEngineTracingBridge(
        IOpenTelemetryIngestor ingestor,
        ILogger<OpenTelemetryEngineTracingBridge> logger,
        OpenTelemetryEngineTracingBridgeOptions? options = null)
    {
        _ingestor = ingestor;
        _logger = logger;
        _options = options ?? new OpenTelemetryEngineTracingBridgeOptions();
        _ticks = _options.TickSource;
    }

    private sealed class PendingTrace(long createdAtTicks)
    {
        public readonly List<TelemetrySpan> Spans = [];
        public readonly long CreatedAtTicks = createdAtTicks;
        public long LastTouchedTicks = createdAtTicks;
        public int OpenSpanCount;

        // Set (under the Spans lock) when the entry is published or evicted; a contender still holding the reference
        // must retry against a fresh table entry instead of mutating a snapshot that already left the table.
        public bool Closed;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkflowEngineTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = OnActivityStarted,
            ActivityStopped = OnActivityStopped,
        };

        ActivitySource.AddActivityListener(_listener);
        _logger.LogInformation(
            "OpenTelemetry engine tracing bridge attached to ActivitySource '{Source}'.",
            WorkflowEngineTelemetry.ActivitySourceName);
    }

    public void Stop()
    {
        _listener?.Dispose();
        _listener = null;
        _pending.Clear();
        // Re-arm Start(): without this a same-instance Stop() → Start() would no-op forever on the _started guard.
        Interlocked.Exchange(ref _started, 0);
    }

    private void OnActivityStarted(Activity activity)
    {
        if (activity.Source.Name != WorkflowEngineTelemetry.ActivitySourceName)
            return;

        var now = _ticks();
        while (true)
        {
            var entry = GetOrAddEntry(activity.TraceId, now);
            lock (entry.Spans)
            {
                if (entry.Closed)
                    continue;

                entry.OpenSpanCount++;
                entry.LastTouchedTicks = now;
                return;
            }
        }
    }

    private void OnActivityStopped(Activity activity)
    {
        if (activity.Source.Name != WorkflowEngineTelemetry.ActivitySourceName)
            return;

        try
        {
            var span = MapSpan(activity);
            var now = _ticks();

            // Only the engine-tagged OUTERMOST drain span flushes the trace, and only once every other engine span of
            // the trace has stopped (the count guards against a second top-level engine tree sharing the trace id, e.g.
            // two workflows executed in one HTTP request). Nested drains carry no tag and stop while the parent's
            // dispatch span is still open, so publishing there would emit a partial tree.
            var isOutermostDrain = activity.OperationName == WorkflowEngineTelemetry.DrainSpanName &&
                                   activity.GetTagItem(WorkflowEngineTelemetry.DrainOutermostTag) is true;

            List<TelemetrySpan>? completed = null;
            while (true)
            {
                var entry = GetOrAddEntry(activity.TraceId, now);
                lock (entry.Spans)
                {
                    if (entry.Closed)
                        continue;

                    // Clamp at zero: a listener attached mid-drain sees stops without matching starts.
                    if (entry.OpenSpanCount > 0)
                        entry.OpenSpanCount--;
                    entry.Spans.Add(span);
                    entry.LastTouchedTicks = now;
                    if (isOutermostDrain && entry.OpenSpanCount == 0)
                    {
                        completed = [.. entry.Spans];
                        CloseEntry(activity.TraceId, entry);
                    }

                    break;
                }
            }

            if (completed is not null)
                Publish(activity.TraceId, completed);

            SweepIfDue();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to bridge engine telemetry activity '{OperationName}' into the OpenTelemetry ingestion store.",
                activity.OperationName);
        }
    }

    private PendingTrace GetOrAddEntry(ActivityTraceId traceId, long now) =>
        _pending.TryGetValue(traceId, out var entry) ? entry : _pending.GetOrAdd(traceId, new PendingTrace(now));

    // Callers must hold the entry lock.
    private void CloseEntry(ActivityTraceId traceId, PendingTrace entry)
    {
        entry.Closed = true;
        _pending.TryRemove(traceId, out _);
    }

    // Opportunistic bounded maintenance, rate-limited to one sweep per SweepIntervalMs (a single Interlocked exchange
    // on the hot path otherwise). Three passes: complete drainless entries (no open spans, quiet for
    // QuietPublishDelayMs — the coalescing quiescence flush commit is the canonical producer) are PUBLISHED; entries
    // that still have open spans after MaxPendingAgeMs are dropped as abandoned; then MaxPendingTraces is enforced
    // with oldest-first eviction.
    private void SweepIfDue()
    {
        var now = _ticks();
        var last = Volatile.Read(ref _lastSweepTicks);
        if (now - last < _options.SweepIntervalMs)
            return;

        // Single sweeper wins; losers return immediately without scanning.
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now, last) != last)
            return;

        List<(ActivityTraceId TraceId, List<TelemetrySpan> Spans)>? publish = null;
        var dropped = 0;
        foreach (var pair in _pending)
        {
            var entry = pair.Value;
            lock (entry.Spans)
            {
                if (entry.Closed)
                    continue;

                if (entry.OpenSpanCount == 0 && entry.Spans.Count > 0 && now - entry.LastTouchedTicks >= _options.QuietPublishDelayMs)
                {
                    CloseEntry(pair.Key, entry);
                    (publish ??= []).Add((pair.Key, [.. entry.Spans]));
                }
                else if (now - entry.CreatedAtTicks > _options.MaxPendingAgeMs)
                {
                    CloseEntry(pair.Key, entry);
                    dropped++;
                }
            }
        }

        var excess = _pending.Count - _options.MaxPendingTraces;
        if (excess > 0)
        {
            foreach (var pair in _pending.ToArray().OrderBy(x => x.Value.CreatedAtTicks))
            {
                if (excess <= 0)
                    break;

                var entry = pair.Value;
                lock (entry.Spans)
                {
                    if (entry.Closed)
                        continue;

                    CloseEntry(pair.Key, entry);
                    dropped++;
                    excess--;
                }
            }
        }

        if (publish is not null)
        {
            foreach (var (traceId, spans) in publish)
                Publish(traceId, spans);
        }

        if (dropped > 0)
        {
            _logger.LogWarning(
                "Dropped {Count} pending engine telemetry trace(s) whose spans never completed.",
                dropped);
        }
    }

    private void Publish(ActivityTraceId traceId, IReadOnlyList<TelemetrySpan> spans)
    {
        var resource = new TelemetryResource(
            ServiceName,
            ServiceName,
            ServiceInstanceId: null,
            TelemetrySdkLanguage: "dotnet",
            Attributes: new Dictionary<string, string?>(),
            LastSeen: DateTimeOffset.UtcNow,
            Status: TelemetryResourceStatus.Active);

        var batch = new OpenTelemetryBatch(
            [resource],
            [CreateTrace(traceId.ToHexString(), spans)],
            spans,
            [],
            [],
            []);

        PublishAsync(batch);
    }

    // Fire-and-forget so the engine's drain thread is never blocked on persistence. The in-memory store completes
    // synchronously; the EF Core store enqueues to its drain channel. Failures are contained and logged.
    private async void PublishAsync(OpenTelemetryBatch batch)
    {
        try
        {
            await _ingestor.IngestAsync(batch, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest engine telemetry batch into the OpenTelemetry store.");
        }
    }

    private static TelemetrySpan MapSpan(Activity activity)
    {
        var traceId = activity.TraceId.ToHexString();
        var spanId = activity.SpanId.ToHexString();
        var parentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString();

        var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in activity.TagObjects)
            attributes[tag.Key] = tag.Value?.ToString();

        var events = activity.Events
            .Select(e => new TelemetrySpanEvent(
                e.Name,
                e.Timestamp,
                e.Tags.ToDictionary(t => t.Key, t => t.Value?.ToString(), StringComparer.OrdinalIgnoreCase)))
            .ToList();

        var start = (DateTimeOffset)activity.StartTimeUtc;
        var end = start + activity.Duration;

        return new TelemetrySpan(
            $"{traceId}:{spanId}",
            traceId,
            spanId,
            parentSpanId,
            ServiceName,
            activity.OperationName,
            activity.Kind.ToString(),
            start,
            end,
            MapStatus(activity),
            activity.StatusDescription,
            attributes,
            events,
            []);
    }

    // Derives the trace from its spans exactly like OtlpHttpProtobufParser.CreateTrace, except workflow instance ids come
    // from the engine's execution-id tag rather than the OTLP "workflow.instance.id" convention.
    private static TelemetryTrace CreateTrace(string traceId, IReadOnlyList<TelemetrySpan> spans)
    {
        var ordered = spans.OrderBy(x => x.StartTime).ToList();
        var root = ordered.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.ParentSpanId)) ?? ordered[0];
        var start = ordered.Min(x => x.StartTime);
        var end = ordered.Max(x => x.EndTime);
        var status = ordered.Any(x => x.Status == SpanStatus.Error)
            ? SpanStatus.Error
            : ordered.Any(x => x.Status == SpanStatus.Ok)
                ? SpanStatus.Ok
                : SpanStatus.Unset;

        var workflowInstanceIds = ordered
            .Select(x => x.Attributes.TryGetValue(WorkflowEngineTelemetry.WorkflowExecutionIdTag, out var value) ? value : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TelemetryTrace(
            traceId,
            root.SpanId,
            root.Name,
            start,
            end,
            end - start,
            status,
            ordered.Select(x => x.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            workflowInstanceIds,
            ordered.Count);
    }

    private static SpanStatus MapStatus(Activity activity)
    {
        if (activity.GetTagItem(WorkflowEngineTelemetry.OutcomeTag) is string outcome &&
            string.Equals(outcome, WorkflowEngineTelemetry.OutcomeFaulted, StringComparison.OrdinalIgnoreCase))
            return SpanStatus.Error;

        return activity.Status switch
        {
            ActivityStatusCode.Ok => SpanStatus.Ok,
            ActivityStatusCode.Error => SpanStatus.Error,
            _ => SpanStatus.Unset,
        };
    }
}
