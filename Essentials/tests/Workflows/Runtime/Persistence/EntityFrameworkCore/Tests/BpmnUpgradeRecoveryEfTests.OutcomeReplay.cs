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
    [InlineData("parallel-mi-outcome", "node-work")]
    [InlineData("listener-rearm-outcome", "node-listener")]
    public async Task Actual_legacy_outcome_wait_fault_preserves_siblings_and_terminal_replay_is_purged(string scenario, string nodeId)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-bpmn-outcome-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "upgraded.db");
            await CopyCheckpointAsync(scenario, database);
            var observations = new CallbackObservations();
            var capture = new ContinuedCallbackCapture();
            RuntimeSchedulerWorkItem callback;
            JsonElement stableActivities;
            JsonElement stableBookmarks;
            JsonElement stableIncidents;
            WorkflowExecutionStatus stableWorkflowStatus;
            string parentId;
            string childId;
            await using (var first = await OpenHostAsync(database,
                Enumerable.Range(0, 100).Select(index => $"outcome-first-{index}"), observations, capture))
            {
                await first.Provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new());
                var store = first.Provider.GetRequiredService<IActivityExecutionStateStore>();
                var before = await store.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                var parent = Assert.Single(before, state => state.Execution.ExecutableNodeId == BpmnRuntimeFixture.ProcessNodeId);
                Assert.Equal(1, parent.PrivateState!.StateVersion);
                var waits = (await first.BookmarksAsync()).OrderBy(wait => wait.ActivityExecutionId, StringComparer.Ordinal).ToArray();
                var selected = waits.First(wait => wait.ExecutableNodeId == nodeId);
                var current = Assert.Single(before, state => state.InvocationId == selected.ActivityExecutionId);
                var tokenId = current.Provenance.ExecutionPathId!;
                Assert.Equal(ActivityExecutionStatus.Suspended, current.Status);
                Assert.Equal(parent.InvocationId, current.ParentActivityExecutionId);
                Assert.False(string.IsNullOrWhiteSpace(tokenId));
                var priorState = parent.PrivateState.Value.InlineValue!.Value;
                Assert.Contains(priorState.GetProperty("tokens").EnumerateArray(), token => token.GetProperty("tokenId").GetString() == tokenId);
                Assert.Single(priorState.GetProperty("activeChildren").EnumerateArray(), work =>
                    work.GetProperty("tokenId").GetString() == tokenId && work.GetProperty("nodeId").GetString() == nodeId &&
                    (work.TryGetProperty("iterationId", out var iteration) ? iteration.GetString() : null) == current.IterationId);
                var originalWaits = await BookmarkObservationsAsync(first);
                if (scenario == "parallel-mi-outcome")
                {
                    var siblings = waits.Where(wait => wait.ExecutableNodeId == nodeId).ToArray();
                    Assert.Equal(3, siblings.Length);
                    Assert.Equal(3, siblings.Select(wait => before.Single(state => state.InvocationId == wait.ActivityExecutionId).IterationId).Distinct().Count());
                }
                else
                {
                    Assert.Null(current.IterationId);
                    Assert.Contains(before, state => state.Execution.ExecutableNodeId == nodeId && state.InvocationId != current.InvocationId &&
                        state.IterationId is null && state.Status == ActivityExecutionStatus.Completed);
                }
                parentId = parent.InvocationId; childId = current.InvocationId;
                capture.ParentId = parentId; capture.ChildId = childId;
                await first.ResumeAsync(selected, JsonSerializer.SerializeToElement(new LegacyCheckpointOutcomeTrigger("fault")));
                callback = Assert.Single(capture.WorkItems);
                var payload = Assert.IsType<RuntimeCompleteActivityCommandPayload>(callback.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>());
                Assert.Equal(current.InvocationId, payload.CompletedChildActivityExecutionId);
                Assert.Equal(parent.InvocationId, payload.ActivityExecutionId);
                var after = await store.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                var continuedChild = Assert.Single(after, state => state.InvocationId == current.InvocationId);
                Assert.Equal(ActivityExecutionStatus.Faulted, continuedChild.Status);
                Assert.Equal(bool.TrueString, callback.CommandMetadata.GetValueOrDefault(RuntimeMetadataKeys.ChildFaulted));
                Assert.Contains(callback.CommandMetadata.GetValueOrDefault(RuntimeMetadataKeys.IncidentId), continuedChild.IncidentIds);
                Assert.Null(continuedChild.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
                Assert.Equal(before.Count, after.Count);
                Assert.Equal(1, observations.TerminalHandles.Count(handle => handle == current.InvocationId));
                foreach (var unrelated in before.Where(state => state.InvocationId != parent.InvocationId && state.InvocationId != current.InvocationId))
                    Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(unrelated),
                        JsonSerializer.SerializeToElement(Assert.Single(after, state => state.InvocationId == unrelated.InvocationId))),
                        "The outcome callback changed another original activity's complete durable facts.");
                var continuedWaits = await BookmarkObservationsAsync(first);
                Assert.Empty(continuedChild.BookmarkIds);
                Assert.DoesNotContain(continuedWaits, wait => wait.State.BookmarkId == selected.BookmarkId);
                Assert.Equal(originalWaits.Count - 1, continuedWaits.Count);
                foreach (var sibling in originalWaits.Where(wait => wait.State.BookmarkId != selected.BookmarkId))
                    Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(sibling),
                        JsonSerializer.SerializeToElement(Assert.Single(continuedWaits, wait => wait.State.BookmarkId == sibling.State.BookmarkId))),
                        "The outcome callback changed another original physical wait.");
                var continuing = Assert.Single(after, state => state.InvocationId == parent.InvocationId);
                var incidents = await first.Provider.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                Assert.Equal("LEGACY-OUTCOME-FAULT", continuedChild.Fault!.Code);
                Assert.Equal("Actual legacy outcome wait faulted.", continuedChild.Fault.Message);
                Assert.Equal(ActivityExecutionStatus.Faulted, continuing.Status);
                Assert.Equal("bpmn.work.faulted", continuing.Fault!.Code);
                var incident = Assert.Single(incidents, incident => continuedChild.IncidentIds.Contains(incident.IncidentId));
                Assert.True(incident.IsBlocking);
                Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, incident.ResolutionOutcome!.ActionKind);
                Assert.Equal(1, continuing.PrivateState!.StateVersion);
                AssertLegacyReaderParses(continuing);
                Assert.Equal(priorState.GetProperty("activeChildren").GetArrayLength() - 1,
                    continuing.PrivateState.Value.InlineValue!.Value.GetProperty("activeChildren").GetArrayLength());
                Assert.DoesNotContain(continuing.PrivateState!.Value.InlineValue!.Value.GetProperty("activeChildren").EnumerateArray(),
                    work => work.GetProperty("tokenId").GetString() == tokenId);
                foreach (var otherWork in priorState.GetProperty("activeChildren").EnumerateArray().Where(work => work.GetProperty("tokenId").GetString() != tokenId))
                    Assert.Contains(continuing.PrivateState.Value.InlineValue.Value.GetProperty("activeChildren").EnumerateArray(),
                        work => JsonElement.DeepEquals(otherWork, work));
                stableWorkflowStatus = (await first.Provider.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId))!.Status;
                Assert.Equal(WorkflowExecutionStatus.Faulted, stableWorkflowStatus);
                stableActivities = ActivityFacts(after);
                stableBookmarks = JsonSerializer.SerializeToElement(continuedWaits);
                stableIncidents = JsonSerializer.SerializeToElement(incidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal));
            }
            var replayCapture = new ContinuedCallbackCapture { ParentId = parentId, ChildId = childId };
            await using (var restarted = await OpenHostAsync(database,
                Enumerable.Range(0, 100).Select(index => $"outcome-restarted-{index}"), observations, replayCapture))
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
                Assert.True(JsonElement.DeepEquals(stableActivities, ActivityFacts(await restarted.Provider.GetRequiredService<IActivityExecutionStateStore>()
                    .ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId))), "Duplicate outcome callback changed durable activity facts.");
                Assert.True(JsonElement.DeepEquals(stableBookmarks, JsonSerializer.SerializeToElement(await BookmarkObservationsAsync(restarted))),
                    "Duplicate outcome callback changed an exact physical wait.");
                Assert.True(JsonElement.DeepEquals(stableIncidents, JsonSerializer.SerializeToElement((await restarted.Provider.GetRequiredService<IIncidentStateStore>()
                    .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId)).OrderBy(incident => incident.IncidentId, StringComparer.Ordinal))),
                    "Duplicate outcome callback changed original incidents.");
                Assert.Equal(calls, observations.TerminalHandles.Count);
                Assert.Empty(replayCapture.WorkItems);
                Assert.Equal(stableWorkflowStatus, (await restarted.Provider.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId))!.Status);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
