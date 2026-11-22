using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Reuses the durable commit gate and SQLite backup machinery rather than simulating redelivery in memory.
public sealed partial class EfReplaySafeFusionCapRecoveryTests
{
    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Durable)]
    public Task MatchAny_recovers_after_child_commit_before_parent_handling_without_repeating_a_body(PersistenceMode mode) =>
        RunCutAndRecoverAsync(_output,
            new CutPoint("MatchAny first child committed before parent callback", commit => RecordsCompleted(commit, ChildA)),
            mode, maxActivitiesPerCommit: 1, NewMatchAnySequence,
            new CutExpectation(
                StatusesAtCut: Statuses((SequenceNodeId, ActivityExecutionStatus.Running), ("switch", ActivityExecutionStatus.Running),
                    (ChildA, ActivityExecutionStatus.Completed), (ChildB, ActivityExecutionStatus.Scheduled)),
                DurableAnchorNodeIds: null, PendingSchedulerOutboxCount: null,
                RecoveredNodeIds: new[] { SequenceNodeId, "switch", ChildA, ChildB, ChildC }.Order(StringComparer.Ordinal).ToArray()),
            targetOccurrence: 1, CheckpointCommitGatePlacement.InnerStoreReturn,
            assertCut: commit => Assert.DoesNotContain(commit.StateChanges.ActivityExecutions,
                change => change.State.Execution.ExecutableNodeId == "switch" && change.State.Status == ActivityExecutionStatus.Completed),
            assertRecovered: states =>
            {
                var owner = Assert.Single(states, state => state.Execution.ExecutableNodeId == "switch");
                Assert.Equal(new[] { "First", "Second" }, owner.Completion!.OutcomeNames);
                Assert.NotNull(owner.InputSnapshot);
                // RunCutAndRecover also checks exactly one durable execution per expected node, fresh identities
                // for genuinely new work, all terminal states, settled queue/outbox and no injected scheduler work.
                Assert.Single(states, state => state.Execution.ExecutableNodeId == ChildA);
                Assert.Single(states, state => state.Execution.ExecutableNodeId == ChildB);
            });

    private static WorkflowExecutable NewMatchAnySequence()
    {
        var boolean = new ValueTypeDescriptor("Boolean");
        var bindings = new[] { "a", "b" }.ToDictionary(SwitchActivity.ConditionInputKey,
            id => new RuntimeInputBinding(SwitchActivity.ConditionInputKey(id), boolean, ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(boolean, JsonSerializer.SerializeToElement(true), ValueProtectionPolicy.InstanceInline)));
        var prototype = ClrActivityContractTestBuilder.BuildContract(typeof(SwitchActivity));
        var contract = new ActivityContract(prototype.ActivityTypeKey, "4.1.0", prototype.DescriptorKind, prototype.DescriptorPayload,
            bindings.Values.Select(binding => new ActivityInputContract(binding.InputKey, binding.InputKey, boolean,
                false, false, false, null, ActivityValuePolicy.Default) { IsStructureInput = true }),
            prototype.Result, ["First", "Second", "Default", "Break"], prototype.Activation, prototype.SideEffectProfile);
        var node = new ExecutableNode("switch", "switch", typeof(SwitchActivity).FullName!, "4.1.0",
            prototype.DescriptorKind, prototype.DescriptorPayload, bindings, new Dictionary<string, string>(),
            childSlots: [
                new(SwitchActivity.CaseSlotName("First"), [WorkflowExecutionHarness.NewReplaySafeProbeNode(ChildA)]),
                new(SwitchActivity.CaseSlotName("Second"), [WorkflowExecutionHarness.NewReplaySafeProbeNode(ChildB)])],
            structure: new(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(new
            {
                mode = "MatchAny",
                cases = new[] {
                    new { match = "First", activity = ChildA, conditionInputKey = SwitchActivity.ConditionInputKey("a") },
                    new { match = "Second", activity = ChildB, conditionInputKey = SwitchActivity.ConditionInputKey("b") }
                }
            })), activityContract: contract);
        return NewSequenceExecutable(node, WorkflowExecutionHarness.NewReplaySafeProbeNode(ChildC));
    }
}
