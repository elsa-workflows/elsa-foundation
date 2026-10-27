using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Bpmn.Tests;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class BpmnUpgradeRecoveryEfTests
{
    [Theory]
    [InlineData("parallel-mi", "node-work")]
    [InlineData("listener-rearm", "node-listener")]
    public async Task Continued_legacy_work_callback_replay_after_pruning_preserves_exact_current_and_sibling_facts(string scenario, string nodeId)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-bpmn-continued-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "upgraded.db");
            await CopyCheckpointAsync(scenario, database);
            var observations = new CallbackObservations();
            var capture = new ContinuedCallbackCapture();
            RuntimeSchedulerWorkItem callback;
            string parentId;
            string childId;
            string tokenId;
            JsonElement stableActivities;
            JsonElement stableBookmarks;
            await using (var first = await OpenHostAsync(database,
                Enumerable.Range(0, 100).Select(index => $"continued-first-{index}"), observations, capture))
            {
                await first.Provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new());
                var store = first.Provider.GetRequiredService<IActivityExecutionStateStore>();
                var before = await store.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                var parent = Assert.Single(before, state => state.Execution.ExecutableNodeId == BpmnRuntimeFixture.ProcessNodeId);
                Assert.Equal(1, parent.PrivateState!.StateVersion);
                var waits = (await first.BookmarksAsync()).OrderBy(wait => wait.ActivityExecutionId, StringComparer.Ordinal).ToArray();
                var selected = waits.First(wait => wait.ExecutableNodeId == nodeId);
                var current = Assert.Single(before, state => state.InvocationId == selected.ActivityExecutionId);
                parentId = parent.InvocationId; childId = current.InvocationId; tokenId = current.Provenance.ExecutionPathId!;
                Assert.Equal(ActivityExecutionStatus.Suspended, current.Status);
                Assert.Equal(parentId, current.ParentActivityExecutionId);
                Assert.False(string.IsNullOrWhiteSpace(tokenId));
                var priorState = parent.PrivateState.Value.InlineValue!.Value;
                Assert.Contains(priorState.GetProperty("tokens").EnumerateArray(), token => token.GetProperty("tokenId").GetString() == tokenId);
                Assert.Single(priorState.GetProperty("activeChildren").EnumerateArray(), work =>
                    work.GetProperty("tokenId").GetString() == tokenId && work.GetProperty("nodeId").GetString() == nodeId &&
                    (work.TryGetProperty("iterationId", out var iteration) ? iteration.GetString() : null) == current.IterationId);
                var originalWaits = await BookmarkObservationsAsync(first);
                if (scenario == "parallel-mi")
                {
                    var siblings = waits.Where(wait => wait.ExecutableNodeId == nodeId).ToArray();
                    Assert.Equal(3, siblings.Length);
                    Assert.Equal(3, siblings.Select(wait => before.Single(state => state.InvocationId == wait.ActivityExecutionId).IterationId).Distinct().Count());
                }
                else
                {
                    Assert.Null(current.IterationId);
                    Assert.Contains(before, state => state.Execution.ExecutableNodeId == nodeId && state.InvocationId != childId &&
                        state.IterationId is null && state.Status == ActivityExecutionStatus.Completed);
                }
                capture.ParentId = parentId; capture.ChildId = childId;
                await first.ResumeAsync(selected, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
                callback = Assert.Single(capture.WorkItems);
                var payload = Assert.IsType<RuntimeCompleteActivityCommandPayload>(callback.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>());
                Assert.Equal(childId, payload.CompletedChildActivityExecutionId);
                Assert.Equal(parentId, payload.ActivityExecutionId);
                var after = await store.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                var continuing = Assert.Single(after, state => state.InvocationId == parentId);
                foreach (var unrelated in before.Where(state => state.InvocationId != parentId && state.InvocationId != childId))
                    Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(unrelated),
                        JsonSerializer.SerializeToElement(Assert.Single(after, state => state.InvocationId == unrelated.InvocationId))),
                        "The callback changed another original activity's complete durable facts.");
                Assert.Equal(ActivityExecutionStatus.Running, continuing.Status);
                Assert.DoesNotContain(continuing.PrivateState!.Value.InlineValue!.Value.GetProperty("tokens").EnumerateArray(),
                    token => token.GetProperty("tokenId").GetString() == tokenId);
                var completed = Assert.Single(after, state => state.InvocationId == childId);
                Assert.Equal(ActivityExecutionStatus.Completed, completed.Status);
                Assert.Equal(callback.WorkItemId, completed.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
                Assert.Equal(1, observations.TerminalHandles.Count(handle => handle == childId));
                var continuedWaits = await BookmarkObservationsAsync(first);
                foreach (var sibling in originalWaits.Where(wait => wait.State.BookmarkId != selected.BookmarkId))
                {
                    var unchanged = Assert.Single(continuedWaits, wait => wait.State.BookmarkId == sibling.State.BookmarkId);
                    Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(sibling), JsonSerializer.SerializeToElement(unchanged)));
                }
                if (scenario == "listener-rearm")
                {
                    var rearmed = Assert.Single(await first.BookmarksAsync(), wait => wait.ExecutableNodeId == nodeId);
                    Assert.NotEqual(childId, rearmed.ActivityExecutionId);
                    Assert.Null(Assert.Single(after, state => state.InvocationId == rearmed.ActivityExecutionId).IterationId);
                }
                stableActivities = ActivityFacts(after);
                stableBookmarks = JsonSerializer.SerializeToElement(continuedWaits);
                Assert.Empty((await first.Provider.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId)));
            }
            await using (var restarted = await OpenHostAsync(database,
                Enumerable.Range(0, 100).Select(index => $"continued-restarted-{index}"), observations))
            {
                var calls = observations.TerminalHandles.Count;
                await restarted.Provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(callback);
                var queued = await restarted.Provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId));
                Assert.Equal(callback.WorkItemId, Assert.Single(queued).WorkItemId);
                await restarted.Provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new());
                Assert.Empty(await restarted.Provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId)));
                Assert.Empty(await restarted.Provider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
                var states = await restarted.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                Assert.True(JsonElement.DeepEquals(stableActivities, ActivityFacts(states)), "Duplicate callback changed complete durable activity facts.");
                Assert.True(JsonElement.DeepEquals(stableBookmarks, JsonSerializer.SerializeToElement(await BookmarkObservationsAsync(restarted))), "Duplicate callback changed an exact live wait.");
                Assert.Equal(calls, observations.TerminalHandles.Count);
                Assert.Equal(WorkflowExecutionStatus.Running, (await restarted.Provider.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId))!.Status);
                Assert.Empty(await restarted.Provider.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
                var remaining = (await restarted.BookmarksAsync()).Where(wait => wait.ExecutableNodeId == (scenario == "parallel-mi" ? "node-work" : "node-hold")).ToArray();
                Assert.Equal(scenario == "parallel-mi" ? 2 : 1, remaining.Length);
                foreach (var wait in remaining)
                    await restarted.ResumeAsync(wait, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
                Assert.Equal(WorkflowExecutionStatus.Completed, (await restarted.Provider.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId))!.Status);
                Assert.Empty(await restarted.BookmarksAsync());
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static JsonElement ActivityFacts(IEnumerable<ActivityExecutionState> states) =>
        JsonSerializer.SerializeToElement(states.OrderBy(state => state.InvocationId, StringComparer.Ordinal));
    private static async Task<IReadOnlyCollection<BookmarkStateObservation>> BookmarkObservationsAsync(BpmnRuntimeFixture host)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>();
        var result = new List<BookmarkStateObservation>();
        foreach (var wait in (await store.ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId)).OrderBy(wait => wait.BookmarkId, StringComparer.Ordinal))
            result.Add(Assert.IsType<BookmarkStateObservation>(await store.ObserveAsync(WorkflowExecutionHarness.WorkflowExecutionId, wait.BookmarkId)));
        return result;
    }

    private sealed class ContinuedCallbackCapture
    {
        public string? ParentId { get; set; }
        public string? ChildId { get; set; }
        public ConcurrentQueue<RuntimeSchedulerWorkItem> WorkItems { get; } = new();
        public void Observe(RuntimeSchedulerWorkItem work)
        {
            if (work.CommandKind == WorkflowExecutionCommandKind.CompleteActivity &&
                work.Payload is { } payload && payload.Deserialize<RuntimeCompleteActivityCommandPayload>() is { } completion &&
                completion.ActivityExecutionId == ParentId && completion.CompletedChildActivityExecutionId == ChildId)
                WorkItems.Enqueue(work);
        }
    }
    private sealed class CapturedCallbackHandler(IWorkflowSchedulerWorkHandler inner, ContinuedCallbackCapture capture) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;
        public bool CanHandle(RuntimeSchedulerWorkItem work) => inner.CanHandle(work);
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem work, CancellationToken cancellationToken = default)
        { capture.Observe(work); return inner.HandleAsync(work, cancellationToken); }
    }
}
