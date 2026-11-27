using System.Diagnostics;
using System.Net;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;
using Activity = System.Diagnostics.Activity;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// A long Durable sequence completes under the default drain cycle limits, whatever started the run. The run drains in a
/// session that serves the next activity itself instead of delivering it through the outbox, so the drain takes few
/// cycles. Since #2661 the limit counts only cycles without progress, so a drain that spends a cycle per activity
/// completes too; <see cref="DrainCyclesWithoutProgressTests"/> covers those drains in every mode.
/// </summary>
/// <remarks>
/// The HTTP case used to wait for all 200 WriteLines inside one Sync request bounded by the host SyncRequestTimeout
/// and the TestServer client's 100-second default. On CI that wall-clock bound is what failed (408 after ~1m43s), not
/// the drain-cycle limit: isolated, the same drain finishes in about 14 seconds. This class runs after the assembly's
/// parallel collections so sibling hosts cannot starve it, the HTTP trigger is Async so Sync 30s/90s/100s defaults are
/// not the gate, and the assertion is the default <see cref="WorkflowDrainOrchestratorOptions.MaxCyclesWithoutProgress"/>
/// bound plus completion of every WriteLine.
/// </remarks>
[Collection(Name)]
public sealed class DurableDrainCycleTests(ITestOutputHelper output)
{
    public const string Name = nameof(DurableDrainCycleTests);

    private const int Lines = 200;
    private const string TriggerPath = "drain-cycle/writelines";

    [Fact]
    public async Task A_directly_started_Durable_sequence_of_200_WriteLines_completes_under_the_default_drain_cycle_bound()
    {
        var probe = new DrainCycleProbe();
        await using var fixture = await StartDurableHostAsync(probe);
        var executable = ReferenceWorkflows.WriteLineSequence("writeline-sequence-200", Lines);
        await fixture.PublishAsync(executable);

        var executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId);

        AssertCompletedDurable(await fixture.WorkflowExecutionAsync(executionId));
        await AssertWriteLinesCompletedAsync(fixture, executionId);
        probe.AssertCompletedWithinDefaultDrainCycleBound(output, Lines);
    }

    [Fact]
    public async Task An_HTTP_triggered_Durable_sequence_of_200_WriteLines_completes_under_the_default_drain_cycle_bound()
    {
        var probe = new DrainCycleProbe();
        await using var fixture = await StartDurableHostAsync(probe);
        await fixture.PublishAsync(ReferenceWorkflows.HttpTriggeredWriteLineSequence(
            "http-writeline-sequence-200",
            TriggerPath,
            Lines,
            ResponseMode.Async));

        using var client = fixture.Client;
        // The TestServer client defaults to 100 seconds; that default is not the drain-cycle bound.
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var response = await client.PostAsync($"/workflows/http/{TriggerPath}", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var execution = await fixture.SingleWorkflowExecutionAsync();
        AssertCompletedDurable(execution);
        await AssertWriteLinesCompletedAsync(fixture, execution.WorkflowExecutionId);
        probe.AssertCompletedWithinDefaultDrainCycleBound(output, Lines);
    }

    private static async Task<HttpEndpointHostFixture> StartDurableHostAsync(DrainCycleProbe probe) =>
        await HttpEndpointHostFixture.StartDurableSqliteAsync(
            PersistenceMode.Durable,
            maxActivitiesPerCommit: 50,
            services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IWorkflowEngineTracer>(probe));
            });

    private static void AssertCompletedDurable(WorkflowExecutionState execution)
    {
        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
        Assert.Equal(nameof(PersistenceMode.Durable), execution.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
    }

    private static async Task AssertWriteLinesCompletedAsync(HttpEndpointHostFixture fixture, string executionId)
    {
        var completed = (await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId))
            .Where(state => state.Status == ActivityExecutionStatus.Completed)
            .Select(state => state.Execution.ExecutableNodeId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(
            Enumerable.Range(1, Lines).Select(line => $"write-{line}"),
            nodeId => Assert.Contains(nodeId, completed));
    }

    /// <summary>
    /// Records each drain cycle, dispatch and activity execution so the test can assert the default no-progress bound
    /// and report per-step timings instead of a Sync HTTP wall-clock deadline.
    /// </summary>
    private sealed class DrainCycleProbe : IWorkflowEngineTracer
    {
        private readonly object _gate = new();
        private readonly List<CycleRecord> _cycles = [];
        private readonly List<StepRecord> _steps = [];
        private readonly HashSet<string> _seenWorkItems = new(StringComparer.Ordinal);
        private CycleRecord? _open;
        private int _consecutiveWithoutProgress;
        private int _maxConsecutiveWithoutProgress;

        public Activity? StartDrainCycle(RuntimeSchedulerDrainRequest request)
        {
            lock (_gate)
            {
                CloseOpenCycle();
                _open = new CycleRecord(_cycles.Count + 1, Stopwatch.GetTimestamp());
                _cycles.Add(_open);
            }

            return null;
        }

        public Activity? StartDispatch(RuntimeSchedulerWorkItem workItem)
        {
            lock (_gate)
            {
                _open?.Dispatches.Add(workItem.WorkItemId);
            }

            return null;
        }

        public Activity? StartActivityExecution(RuntimeSchedulerWorkItem workItem)
        {
            lock (_gate)
            {
                _steps.Add(new StepRecord(NodeId(workItem), workItem.WorkItemId, Stopwatch.GetTimestamp()));
            }

            return null;
        }

        public Activity? StartCheckpointCommit(RuntimeCheckpointCommit commit) => null;

        public void AssertCompletedWithinDefaultDrainCycleBound(ITestOutputHelper output, int expectedWriteLines)
        {
            lock (_gate)
                CloseOpenCycle();

            var bound = WorkflowDrainOrchestratorOptions.DefaultMaxCyclesWithoutProgress;
            var cycleGaps = Gaps(_cycles.Select(cycle => cycle.StartedTimestamp).ToArray());
            var stepGaps = Gaps(_steps.Select(step => step.StartedTimestamp).ToArray());

            output.WriteLine(
                $"Drain cycles: {_cycles.Count}; max consecutive cycles without progress: {_maxConsecutiveWithoutProgress} (bound {bound}); traced activity executions: {_steps.Count} (fused WriteLines do not each start a span); expected completed WriteLines: {expectedWriteLines}.");
            output.WriteLine($"Cycle dispatch counts: {string.Join(", ", _cycles.Select(cycle => $"{cycle.Number}:{cycle.Dispatches.Count}"))}.");
            if (cycleGaps.Count > 0)
            {
                output.WriteLine(
                    $"Drain-cycle gaps: n={cycleGaps.Count}, min={cycleGaps[0].TotalMilliseconds:0.0}ms, p50={Percentile(cycleGaps, 0.50).TotalMilliseconds:0.0}ms, p95={Percentile(cycleGaps, 0.95).TotalMilliseconds:0.0}ms, max={cycleGaps[^1].TotalMilliseconds:0.0}ms.");
            }

            if (stepGaps.Count > 0)
            {
                output.WriteLine(
                    $"Traced activity/resumption gaps: n={stepGaps.Count}, min={stepGaps[0].TotalMilliseconds:0.0}ms, p50={Percentile(stepGaps, 0.50).TotalMilliseconds:0.0}ms, p95={Percentile(stepGaps, 0.95).TotalMilliseconds:0.0}ms, max={stepGaps[^1].TotalMilliseconds:0.0}ms.");
            }

            Assert.True(_cycles.Count > 0, "The run produced no drain cycles.");
            Assert.True(
                _maxConsecutiveWithoutProgress < bound,
                $"The drain spent {_maxConsecutiveWithoutProgress} consecutive cycles without progress, which is not under the default bound of {bound}.");
        }

        private void CloseOpenCycle()
        {
            if (_open is null)
                return;

            var progressed = false;
            foreach (var workItemId in _open.Dispatches)
                progressed |= _seenWorkItems.Add(workItemId);

            _consecutiveWithoutProgress = progressed ? 0 : _consecutiveWithoutProgress + 1;
            if (_consecutiveWithoutProgress > _maxConsecutiveWithoutProgress)
                _maxConsecutiveWithoutProgress = _consecutiveWithoutProgress;
            _open = null;
        }

        private static List<TimeSpan> Gaps(IReadOnlyList<long> timestamps)
        {
            if (timestamps.Count < 2)
                return [];

            var gaps = new List<TimeSpan>(timestamps.Count - 1);
            for (var i = 1; i < timestamps.Count; i++)
                gaps.Add(Stopwatch.GetElapsedTime(timestamps[i - 1], timestamps[i]));
            gaps.Sort();
            return gaps;
        }

        private static TimeSpan Percentile(IReadOnlyList<TimeSpan> sorted, double percentile)
        {
            var index = (int)Math.Clamp(Math.Ceiling(percentile * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }

        private static string NodeId(RuntimeSchedulerWorkItem workItem)
        {
            if (workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var nodeId) && nodeId.Length > 0)
                return nodeId;
            if (workItem.EnvelopeMetadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out nodeId) && nodeId.Length > 0)
                return nodeId;
            return workItem.WorkItemId;
        }

        private sealed class CycleRecord(int number, long startedTimestamp)
        {
            public int Number { get; } = number;
            public long StartedTimestamp { get; } = startedTimestamp;
            public List<string> Dispatches { get; } = [];
        }

        private sealed record StepRecord(string NodeId, string WorkItemId, long StartedTimestamp);
    }
}

[CollectionDefinition(DurableDrainCycleTests.Name, DisableParallelization = true)]
public sealed class DurableDrainCycleTestCollection;
