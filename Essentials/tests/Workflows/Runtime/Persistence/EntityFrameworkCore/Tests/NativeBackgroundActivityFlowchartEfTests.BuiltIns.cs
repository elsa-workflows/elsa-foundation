using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 1230 US5 (T019): a marked built-in placed in the background is admitted, runs on a worker and its result is
/// applied on the coordinator, under both persistence modes.
/// </summary>
public sealed partial class NativeBackgroundActivityFlowchartEfTests
{
    private const string BuiltInNodeId = "background-built-in";

    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task A_marked_built_in_runs_on_a_worker_and_its_completion_is_applied(PersistenceMode persistenceMode)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var finalCounter = new FinalStepCounter();
        await using var harness = await StartHarnessAsync(executionId, new BackgroundBodyBarrier(), finalCounter, persistenceMode);
        var reference = await PublishAsync(harness, NewBuiltInExecutable(BuiltInNode<WriteLine>(("text", "from a worker"))));
        await CopyPublicationIntoPartitionAsync(harness, reference);

        await harness.StartPublishedAsync(reference, executionId);
        Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(harness, executionId, BuiltInNodeId));

        var worker = Assert.Single(await ClaimWorkersAsync(harness, executionId, 1));
        Assert.Equal(BuiltInNodeId, worker.Request.ExecutableNodeId);
        await harness.Services.GetRequiredService<BackgroundActivityWorker>()
            .ExecuteClaimAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value), worker.Claim)
            .WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);

        Assert.Equal(ActivityExecutionStatus.Completed, await ActivityStatusAsync(harness, executionId, BuiltInNodeId));
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(1, finalCounter.Count);
    }

    public static TheoryData<string> OtherMarkedBuiltIns => new() { nameof(Break), nameof(WriteLines), nameof(BpmnDecision) };

    [Theory]
    [MemberData(nameof(OtherMarkedBuiltIns))]
    public async Task Every_other_marked_Essentials_built_in_runs_on_a_worker(string activity)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        await using var harness = await StartHarnessAsync(executionId, new BackgroundBodyBarrier(), new FinalStepCounter(),
            includeBpmnFeature: activity == nameof(BpmnDecision));
        var node = activity switch
        {
            nameof(Break) => BuiltInNode<Break>(),
            nameof(WriteLines) => BuiltInNode<WriteLines>(("Lines", new[] { "one", "two" })),
            // Outside a BPMN process the pinned outcome set is the default one; the worker refuses any other outcome.
            _ => BuiltInNode<BpmnDecision>(("Outcome", "Done"))
        };
        var reference = await PublishAsync(harness, NewBuiltInExecutable(node));
        await CopyPublicationIntoPartitionAsync(harness, reference);

        await harness.StartPublishedAsync(reference, executionId);
        var worker = Assert.Single(await ClaimWorkersAsync(harness, executionId, 1));
        Assert.Equal(BuiltInNodeId, worker.Request.ExecutableNodeId);
        await harness.Services.GetRequiredService<BackgroundActivityWorker>()
            .ExecuteClaimAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value), worker.Claim)
            .WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);

        Assert.Equal(ActivityExecutionStatus.Completed, await ActivityStatusAsync(harness, executionId, BuiltInNodeId));
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(harness, executionId));
    }

    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task A_marked_built_in_fault_from_a_worker_faults_its_step(PersistenceMode persistenceMode)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var finalCounter = new FinalStepCounter();
        await using var harness = await StartHarnessAsync(executionId, new BackgroundBodyBarrier(), finalCounter, persistenceMode);
        var reference = await PublishAsync(harness, NewBuiltInExecutable(BuiltInNode<Fault>(("code", "worker-fault"), ("message", "raised on a worker"), ("category", "test"), ("faultType", "worker"))));
        await CopyPublicationIntoPartitionAsync(harness, reference);

        await harness.StartPublishedAsync(reference, executionId);
        var worker = Assert.Single(await ClaimWorkersAsync(harness, executionId, 1));
        await harness.Services.GetRequiredService<BackgroundActivityWorker>()
            .ExecuteClaimAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value), worker.Claim)
            .WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);

        var state = Assert.Single(await ActivityStatesAsync(harness, executionId), item => item.Execution.ExecutableNodeId == BuiltInNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("worker-fault", state.Fault!.Code);
        Assert.Equal(0, finalCounter.Count);
    }

    private static WorkflowExecutable NewBuiltInExecutable(ExecutableNode background)
    {
        var final = InlineNode<FinalStepActivity>(FinalNodeId, new Dictionary<string, string>());
        var sequenceContract = ClrActivityContractTestBuilder.BuildContract(typeof(SequenceActivity));
        var sequence = new ExecutableNode(
            executableNodeId: SequenceNodeId,
            authoredActivityId: $"authored-{SequenceNodeId}",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: sequenceContract.DescriptorKind,
            descriptorPayload: sequenceContract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [background, final])],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceExecutableStructure([BuiltInNodeId, FinalNodeId]))),
            activityContract: sequenceContract);
        return WorkflowExecutionHarness.NewExecutable(sequence, WorkflowExecutionHarness.Identity);
    }

    private static ExecutableNode BuiltInNode<TActivity>(params (string Key, object Value)[] literals)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(TActivity));
        Assert.True(contract.SupportsBackgroundExecution);
        var bindings = literals.ToDictionary(
            literal => literal.Key,
            literal =>
            {
                var type = contract.Inputs[literal.Key].Type;
                return new RuntimeInputBinding(literal.Key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(literal.Value), ValueProtectionPolicy.InstanceInline));
            },
            StringComparer.Ordinal);
        return new ExecutableNode(
            executableNodeId: BuiltInNodeId,
            authoredActivityId: $"authored-{BuiltInNodeId}",
            activityType: typeof(TActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: contract.DescriptorKind,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: bindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract,
            executionPlacement: ActivityExecutionPlacement.Background);
    }
}
