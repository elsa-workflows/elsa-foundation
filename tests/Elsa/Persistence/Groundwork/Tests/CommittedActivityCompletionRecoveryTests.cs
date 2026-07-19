using System.Text.Json;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class CommittedActivityCompletionRecoveryTests
{
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Completed_invocation_recovers_its_pinned_snapshot_attempt_and_atomic_completion(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        IActivityExecutionStateStore store = new GroundworkActivityExecutionStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer);
        var state = NewCompletedState();

        await store.SaveAsync(state);
        var recovered = await store.FindAsync("workflow-1", "invocation-1");

        Assert.NotNull(recovered);
        recovered.EnsureValueFlowCompatible();
        Assert.Equal(ActivityExecutionStatus.Completed, recovered.Status);
        Assert.Equal("hello", recovered.InputSnapshot!.Values["message"].InlineValue!.Value.GetString());
        Assert.Equal(ActivityTransitionKind.Complete, Assert.Single(recovered.Attempts!).TransitionKind);
        Assert.Equal("accepted", recovered.Completion!.Result.InlineValue!.Value.GetString());
        Assert.Equal("Done", recovered.Completion.OutcomeKey);
    }

    private static ActivityExecutionState NewCompletedState()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-16T08:00:00Z");
        var completedAt = startedAt.AddSeconds(1);
        var type = new ValueTypeDescriptor("String");
        var policy = ValueProtectionPolicy.InstanceInline;
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            "contract-sha256",
            "bindings-sha256",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("hello"), policy)
            },
            startedAt);
        var attempt = new ActivityAttempt(
            "attempt-1",
            "invocation-1",
            1,
            ActivityAttemptReason.Initial,
            startedAt,
            completedAt,
            transitionKind: ActivityTransitionKind.Complete);
        var completion = new ActivityCompletion(
            "invocation-1",
            "attempt-1",
            ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("accepted"), policy),
            "Done",
            completedAt,
            "contract-sha256");

        return new ActivityExecutionState(
            new ActivityExecution("invocation-1", "workflow-1", "node-1", "activity-1", "test/activity", "1.0.0"),
            ActivityExecutionStatus.Completed,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: startedAt,
            StartedAt: startedAt,
            CompletedAt: completedAt,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From("workflow-1", null, null, null, null, null, null, null),
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>(),
            ContractIdentity: new ActivityInvocationContractIdentity("test/activity", "1.0.0", "contract-sha256"),
            InputSnapshot: snapshot,
            Attempts: [attempt],
            Completion: completion);
    }
}
