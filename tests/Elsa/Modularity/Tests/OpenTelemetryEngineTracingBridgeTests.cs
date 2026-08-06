using System.Diagnostics;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Providers.InMemory;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Elsa.Workbench;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Behavior of the host-local engine tracing bridge (lives in Elsa.Workbench, hence this project — the only test assembly
/// with internals access): per-cycle batches for multi-cycle synchronous drains that merge into one store summary, and
/// sweep-publication (not dropping) of drainless engine spans such as the coalescing quiescence flush commit.
/// </summary>
public sealed class OpenTelemetryEngineTracingBridgeTests : IDisposable
{
    private readonly List<OpenTelemetryBatch> _ingested = [];
    private readonly InMemoryOpenTelemetryStore _store;
    private readonly ActivitySource _engineSource = new(WorkflowEngineTelemetry.ActivitySourceName);
    private readonly ActivitySourceWorkflowEngineTracer _tracer;
    private readonly ActivitySource _hostSource = new("Test.Host.Request");
    private readonly ActivityListener _hostListener;
    private long _ticks;
    private OpenTelemetryEngineTracingBridge? _bridge;

    public OpenTelemetryEngineTracingBridgeTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new OpenTelemetryDiagnosticsOptions());
        _store = new InMemoryOpenTelemetryStore(options, new OpenTelemetrySourceRegistry(options));
        _tracer = new ActivitySourceWorkflowEngineTracer(_engineSource);

        // The bridge only listens to the engine source; the simulated ASP.NET request span needs its own listener.
        _hostListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Test.Host.Request",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(_hostListener);
    }

    private OpenTelemetryEngineTracingBridge StartBridge()
    {
        _bridge = new OpenTelemetryEngineTracingBridge(
            new CapturingIngestor(_ingested, _store),
            NullLogger<OpenTelemetryEngineTracingBridge>.Instance,
            new OpenTelemetryEngineTracingBridgeOptions { TickSource = () => Volatile.Read(ref _ticks) });
        _bridge.Start();
        return _bridge;
    }

    public void Dispose()
    {
        _bridge?.Stop();
        _hostListener.Dispose();
        _engineSource.Dispose();
        _hostSource.Dispose();
    }

    [Fact]
    public async Task MultiCycleSynchronousDrain_PublishesOneBatchPerCycle_AndStoreMergesThemIntoOneSummary()
    {
        StartBridge();

        // Simulates the ASP.NET Core request activity a synchronous execute drains under: every cycle shares its trace id.
        using var request = _hostSource.StartActivity("HTTP POST /execute");
        Assert.NotNull(request);

        // Cycle 1: the dispatched work item faults.
        using (var drain = _tracer.StartDrainCycle(new("wfexec-1")))
        {
            using var dispatch = _tracer.StartDispatch(NewWorkItem());
            dispatch!.SetTag(WorkflowEngineTelemetry.OutcomeTag, WorkflowEngineTelemetry.OutcomeFaulted);
        }

        // Cycle 2 (after outbox delivery re-enqueued work): clean.
        using (var drain = _tracer.StartDrainCycle(new("wfexec-1")))
        {
            using var dispatch = _tracer.StartDispatch(NewWorkItem());
            dispatch!.SetTag(WorkflowEngineTelemetry.OutcomeTag, WorkflowEngineTelemetry.OutcomeCompleted);
        }

        // One batch per drain cycle, both under the request's trace id.
        Assert.Equal(2, _ingested.Count);
        var requestTraceId = request!.TraceId.ToHexString();
        Assert.All(_ingested, batch => Assert.Equal(requestTraceId, Assert.Single(batch.Traces).TraceId));
        Assert.Equal(2, _ingested[0].Spans.Count);
        Assert.Equal(SpanStatus.Error, _ingested[0].Traces.Single().Status);
        Assert.Equal(SpanStatus.Unset, _ingested[1].Traces.Single().Status);

        // The store's list row merges the cycles: the first cycle's error status survives and spans are summed.
        var result = await _store.QueryTracesAsync(new OpenTelemetryTraceFilter { Take = 10 });
        var summary = Assert.Single(result.Items);
        Assert.Equal(requestTraceId, summary.TraceId);
        Assert.Equal(SpanStatus.Error, summary.Status);
        Assert.Equal(4, summary.SpanCount);

        // The detail view unions all spans of the trace.
        var detail = await _store.GetTraceAsync(requestTraceId);
        Assert.Equal(4, detail!.Spans.Count);
    }

    [Fact]
    public async Task DrainlessFlushCommitSpan_IsPublishedByTheSweep_InsteadOfDropped()
    {
        StartBridge();

        string requestTraceId;
        using (var request = _hostSource.StartActivity("HTTP POST /execute"))
        {
            requestTraceId = request!.TraceId.ToHexString();

            // A drained cycle publishes as usual...
            using (var drain = _tracer.StartDrainCycle(new("wfexec-1")))
            {
            }

            // ...then the coalescing quiescence flush emits a checkpoint-commit span AFTER the drain loop — no drain
            // span will ever close over it.
            _tracer.StartCheckpointCommit(NewCommit())!.Dispose();
        }

        Assert.Single(_ingested);

        // Once the entry has been quiet past the sweep thresholds, the next engine span stop (an unrelated workflow's
        // drain, on its own trace) sweeps it out as a publication under the original trace id, so the store folds it
        // into the request's trace.
        Volatile.Write(ref _ticks, 31_000);
        using (var unrelated = _tracer.StartDrainCycle(new("wfexec-other")))
        {
        }

        Assert.Equal(3, _ingested.Count);
        var flushBatch = _ingested.Single(batch => batch.Spans.Any(span => span.Name == WorkflowEngineTelemetry.CheckpointCommitSpanName));
        var flushSpan = Assert.Single(flushBatch.Spans);
        Assert.Equal(requestTraceId, flushSpan.TraceId);
        Assert.Equal("cp-1", flushSpan.Attributes[WorkflowEngineTelemetry.CheckpointIdTag]);

        // Store view: the request trace summary now covers drain + flush commit.
        var detail = await _store.GetTraceAsync(requestTraceId);
        Assert.Equal(2, detail!.Spans.Count);
        Assert.Equal(2, detail.Trace.SpanCount);
    }

    [Fact]
    public void NestedChildWorkflowDrain_DoesNotPublishAPartialTree()
    {
        StartBridge();

        using (var outer = _tracer.StartDrainCycle(new("wfexec-parent")))
        {
            using (var dispatch = _tracer.StartDispatch(NewWorkItem()))
            {
                // ChildStartExecutor drains the child workflow inside the parent's dispatch.
                using var nested = _tracer.StartDrainCycle(new("wfexec-child"));
            }

            // The nested drain has stopped, but nothing may publish while the outer tree is still open.
            Assert.Empty(_ingested);
        }

        var batch = Assert.Single(_ingested);
        Assert.Equal(3, batch.Spans.Count);
    }

    private static RuntimeSchedulerWorkItem NewWorkItem() =>
        new(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandId: "cmd-1",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "env-1",
            idempotencyKey: "idem-1",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch);

    private static RuntimeCheckpointCommit NewCommit() =>
        new(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "cp-1",
                Name: "coalescing.flush",
                WorkflowExecutionId: "wfexec-1",
                OccurredAt: DateTimeOffset.UnixEpoch,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    /// <summary>Captures every ingested batch and forwards it to the in-memory store, mimicking the ingestor pipeline.</summary>
    private sealed class CapturingIngestor(List<OpenTelemetryBatch> ingested, InMemoryOpenTelemetryStore store) : IOpenTelemetryIngestor
    {
        public async ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
        {
            lock (ingested)
            {
                ingested.Add(batch);
            }

            await store.WriteAsync(batch, cancellationToken);
        }
    }
}
