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

/// <summary>
/// Subscribes the engine <see cref="ActivitySource"/> and forwards completed span trees to the OpenTelemetry ingestor.
/// </summary>
internal sealed class OpenTelemetryEngineTracingBridge(
    IOpenTelemetryIngestor ingestor,
    ILogger<OpenTelemetryEngineTracingBridge> logger)
{
    // Mirrors the OTLP parser's resource shape (resource id == service name when no instance id is present).
    private const string ServiceName = "elsa-workflow-engine";

    // Bounded-buffer policy: a pending trace that never sees its outermost drain span stop (aborted drain, engine
    // spans emitted outside a drain) must not leak forever on a long-lived server. Sweeps run opportunistically from
    // OnActivityStopped, are rate-limited by SweepIntervalMs, drop entries older than MaxPendingAgeMs, and cap the
    // table at MaxPendingTraces with oldest-first eviction.
    private const long SweepIntervalMs = 30_000;
    private const long MaxPendingAgeMs = 5 * 60_000;
    private const int MaxPendingTraces = 512;

    // Spans complete child-first and the OUTERMOST drain span last, so a trace's spans are buffered under its trace id
    // until that drain stops; at that point the whole tree is folded into one batch — matching the OTLP receiver, which
    // also derives the TelemetryTrace from the batch's spans. The outermost drain is detected by counting open engine
    // spans per trace (started minus stopped), NOT by requiring a parentless drain: on the synchronous execute path the
    // drain runs inside the HTTP request, so the drain span inherits ASP.NET Core's request activity as its parent and
    // a parent-id check would never fire. Nested drains (ChildStartExecutor runs a child workflow's drain inside the
    // parent's) stop while the parent's dispatch span is still open, so they buffer like any child span.
    private readonly ConcurrentDictionary<string, PendingTrace> _pending = new(StringComparer.OrdinalIgnoreCase);
    private long _lastSweepTicks;
    private ActivityListener? _listener;
    private int _started;

    private sealed class PendingTrace(long createdAtTicks)
    {
        public readonly List<TelemetrySpan> Spans = [];
        public readonly long CreatedAtTicks = createdAtTicks;
        public int OpenSpanCount;
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
        logger.LogInformation(
            "OpenTelemetry engine tracing bridge attached to ActivitySource '{Source}'.",
            WorkflowEngineTelemetry.ActivitySourceName);
    }

    public void Stop()
    {
        _listener?.Dispose();
        _listener = null;
        _pending.Clear();
    }

    private void OnActivityStarted(Activity activity)
    {
        if (activity.Source.Name != WorkflowEngineTelemetry.ActivitySourceName)
            return;

        var entry = _pending.GetOrAdd(activity.TraceId.ToHexString(), static _ => new PendingTrace(Environment.TickCount64));
        lock (entry.Spans)
        {
            entry.OpenSpanCount++;
        }
    }

    private void OnActivityStopped(Activity activity)
    {
        if (activity.Source.Name != WorkflowEngineTelemetry.ActivitySourceName)
            return;

        try
        {
            var span = MapSpan(activity);
            var entry = _pending.GetOrAdd(span.TraceId, static _ => new PendingTrace(Environment.TickCount64));

            // Only the OUTERMOST drain span flushes the trace: a drain stop with no other engine span still open in
            // this trace. Nested drains (a child workflow drained inside the parent's drain via ChildStartExecutor)
            // stop while the parent's dispatch span is still open, so publishing there would emit a partial tree plus
            // a second batch for the same trace id. A parent-id check cannot detect the outermost drain because the
            // synchronous execute path drains inside the HTTP request, giving the drain span an ASP.NET Core parent.
            var isDrain = activity.OperationName == WorkflowEngineTelemetry.DrainSpanName;

            List<TelemetrySpan>? completed = null;
            lock (entry.Spans)
            {
                // Clamp at zero: a listener attached mid-drain sees stops without matching starts.
                if (entry.OpenSpanCount > 0)
                    entry.OpenSpanCount--;
                entry.Spans.Add(span);
                if (isDrain && entry.OpenSpanCount == 0)
                    completed = [.. entry.Spans];
            }

            if (completed is not null)
            {
                _pending.TryRemove(span.TraceId, out _);
                Publish(span.TraceId, completed);
            }

            SweepIfDue();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to bridge engine telemetry activity '{OperationName}' into the OpenTelemetry ingestion store.",
                activity.OperationName);
        }
    }

    // Opportunistic bounded eviction, rate-limited to one sweep per SweepIntervalMs (a single Interlocked exchange on
    // the hot path otherwise). Drops pending traces whose outermost drain span never stopped once they exceed
    // MaxPendingAgeMs, then enforces MaxPendingTraces with oldest-first eviction.
    private void SweepIfDue()
    {
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastSweepTicks);
        if (now - last < SweepIntervalMs)
            return;

        // Single sweeper wins; losers return immediately without scanning.
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now, last) != last)
            return;

        var dropped = 0;
        foreach (var pair in _pending)
        {
            if (now - pair.Value.CreatedAtTicks > MaxPendingAgeMs && _pending.TryRemove(pair.Key, out _))
                dropped++;
        }

        var excess = _pending.Count - MaxPendingTraces;
        if (excess > 0)
        {
            foreach (var pair in _pending.ToArray().OrderBy(x => x.Value.CreatedAtTicks))
            {
                if (excess <= 0)
                    break;

                if (_pending.TryRemove(pair.Key, out _))
                {
                    dropped++;
                    excess--;
                }
            }
        }

        if (dropped > 0)
        {
            logger.LogWarning(
                "Dropped {Count} pending engine telemetry trace(s) that never completed an outermost drain span.",
                dropped);
        }
    }

    private void Publish(string traceId, IReadOnlyList<TelemetrySpan> spans)
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
            [CreateTrace(traceId, spans)],
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
            await ingestor.IngestAsync(batch, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ingest engine telemetry batch into the OpenTelemetry store.");
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
