using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>The runtime persistence providers a background result is applied through.</summary>
public enum RuntimeProvider
{
    InMemory,
    EntityFrameworkCore
}

/// <summary>
/// Spec 1230 US6 (ADR 0093 decision 6): on the in-memory and the EF Core provider, a background result keeps causal
/// order, is applied atomically, and leaves an inspectable record of the actual commit order (S-10's write history and
/// commit sequence). No order is imposed between otherwise unordered branches.
/// </summary>
public sealed partial class NativeBackgroundActivityFlowchartEfTests
{
    private const string DependentNodeId = "dependent";
    private const string SharedVariableKey = "var-shared";

    [Theory]
    [InlineData(RuntimeProvider.InMemory)]
    [InlineData(RuntimeProvider.EntityFrameworkCore)]
    public async Task A_dependent_step_runs_after_the_background_result_is_applied(RuntimeProvider provider)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var barrier = new BackgroundBodyBarrier();
        var observed = new DependentStepObservations();
        await using var harness = await StartHarnessAsync(executionId, barrier, new FinalStepCounter(), provider: provider,
            configure: services => services.AddSingleton(observed));
        var reference = await PublishAsync(harness, NewBackgroundThenDependentExecutable());
        await CopyPublicationIntoPartitionAsync(harness, reference);

        await harness.StartPublishedAsync(reference, executionId);
        var worker = Assert.Single(await ClaimWorkersAsync(harness, executionId, 1));
        var run = harness.Services.GetRequiredService<BackgroundActivityWorker>()
            .ExecuteClaimAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value), worker.Claim);
        await barrier.WaitUntilEnteredAsync("left", TimeSpan.FromSeconds(10));

        // While the body runs, the coordinator stays free and the dependent step has not run.
        await DrivePartitionAsync(harness, executionId);
        Assert.Empty(observed.Values);
        Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(harness, executionId, LeftNodeId));

        barrier.Release("left");
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);

        // The dependent step read the captured result, so it ran after the result was applied.
        Assert.Equal<string?>(["left-result"], observed.Values);
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(harness, executionId));
        var states = await ActivityStatesAsync(harness, executionId);
        var background = Assert.Single(states, state => state.Execution.ExecutableNodeId == LeftNodeId);
        var dependent = Assert.Single(states, state => state.Execution.ExecutableNodeId == DependentNodeId);
        Assert.True(dependent.FirstCommitSequence > background.LastCommitSequence,
            $"The dependent step's first commit ({dependent.FirstCommitSequence}) must follow the background result's last ({background.LastCommitSequence}).");
    }

    [Theory]
    [InlineData(RuntimeProvider.InMemory, LeftNodeId)]
    [InlineData(RuntimeProvider.InMemory, RightNodeId)]
    [InlineData(RuntimeProvider.EntityFrameworkCore, LeftNodeId)]
    [InlineData(RuntimeProvider.EntityFrameworkCore, RightNodeId)]
    public async Task Unordered_background_writes_to_one_variable_keep_the_last_commit_and_record_the_order(
        RuntimeProvider provider,
        string firstNodeId)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var barrier = new BackgroundBodyBarrier();
        await using var harness = await StartHarnessAsync(executionId, barrier, new FinalStepCounter(), provider: provider);
        var reference = await PublishAsync(harness, NewForkExecutable(
            BackgroundNode(LeftNodeId, "left", SharedVariableKey, "shared"),
            BackgroundNode(RightNodeId, "right", SharedVariableKey, "shared"),
            new RuntimeVariableDeclaration(SharedVariableKey, "shared", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline)));
        await CopyPublicationIntoPartitionAsync(harness, reference);

        await harness.StartPublishedAsync(reference, executionId);
        var workers = await ClaimTwoAsync(harness, executionId);
        var secondNodeId = firstNodeId == LeftNodeId ? RightNodeId : LeftNodeId;
        await ExecuteBackgroundAsync(harness, barrier, workers.Single(item => item.Request.ExecutableNodeId == firstNodeId));
        await DrivePartitionAsync(harness, executionId);
        await ExecuteBackgroundAsync(harness, barrier, workers.Single(item => item.Request.ExecutableNodeId == secondNodeId));
        await DrivePartitionAsync(harness, executionId);

        // Last commit wins for unordered plain writes.
        var workflow = await WorkflowStateAsync(harness, executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow!.Status);
        Assert.Equal($"{Name(secondNodeId)}-result", workflow.RootVariableFrame!.Values[SharedVariableKey].InlineValue!.Value.GetString());

        // The write history records both writes, in the order they were committed.
        var states = await ActivityStatesAsync(harness, executionId);
        var writes = states
            .SelectMany(state => state.VariableWrites ?? [])
            .Where(record => record.Writer.ExecutableNodeId is LeftNodeId or RightNodeId)
            .OrderBy(record => record.CommitOrder)
            .ToArray();
        Assert.Equal([firstNodeId, secondNodeId], writes.Select(record => record.Writer.ExecutableNodeId).ToArray());
        Assert.Single(writes.Select(record => record.VariableKey).Distinct(StringComparer.Ordinal));
        Assert.True(writes[1].NewVersion > writes[0].NewVersion);
        Assert.DoesNotContain(writes, record => record.Outcome == VariableCommitOutcome.LostUpdateFault);

        // The checkpoint sequence agrees with the write history.
        var first = Assert.Single(states, state => state.Execution.ExecutableNodeId == firstNodeId);
        var second = Assert.Single(states, state => state.Execution.ExecutableNodeId == secondNodeId);
        Assert.True(second.LastCommitSequence > first.LastCommitSequence,
            $"'{secondNodeId}' committed last, so its commit sequence ({second.LastCommitSequence}) must follow '{firstNodeId}' ({first.LastCommitSequence}).");
    }

    [Theory]
    [InlineData(RuntimeProvider.InMemory)]
    [InlineData(RuntimeProvider.EntityFrameworkCore)]
    public async Task A_failed_result_commit_applies_nothing_and_keeps_the_result_for_repair(RuntimeProvider provider)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var barrier = new BackgroundBodyBarrier();
        var finalCounter = new FinalStepCounter();
        var fault = new ResultCommitFault(LeftNodeId);
        await using var harness = await StartHarnessAsync(executionId, barrier, finalCounter, provider: provider,
            configure: services =>
            {
                services.AddSingleton(fault);
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, ResultCommitFaultEnricher>());
            });
        var reference = await PublishAsync(harness, NewBackgroundThenFinalExecutable());
        await CopyPublicationIntoPartitionAsync(harness, reference);

        await harness.StartPublishedAsync(reference, executionId);
        var claimed = Assert.Single(await ClaimWorkersAsync(harness, executionId, 1));
        await ExecuteBackgroundAsync(harness, barrier, claimed);
        await SweepOnceAsync(harness);

        // The result commit failed: no captured output, no completion and no next step.
        Assert.Equal(1, fault.Hits);
        var workflow = await WorkflowStateAsync(harness, executionId);
        var captured = workflow!.RootVariableFrame?.Values.GetValueOrDefault(LeftVariableKey)?.InlineValue;
        Assert.False(captured is { ValueKind: JsonValueKind.String } && captured.Value.GetString() == "left-result");
        Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(harness, executionId, LeftNodeId));
        Assert.Equal(0, finalCounter.Count);
        Assert.Empty((await ActivityStatesAsync(harness, executionId)).SelectMany(state => state.VariableWrites ?? []));

        await using (var scope = await CreatePartitionScopeAsync(harness))
        {
            // The recorded result is not lost: the work stays result-ready, and the failed application surfaces as a
            // poisoned scheduler item with an incident, which the operator repairs (the scheduler's default policy).
            var work = await scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>().FindAsync(executionId, claimed.Request.WorkId);
            Assert.Equal(BackgroundActivityWorkStatus.ResultReady, work!.Status);
            var incident = Assert.Single(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(executionId));
            Assert.Equal("SchedulerWorkPoisoned", incident.FailureType);
            Assert.Contains(nameof(ResultCommitFaultException), incident.Message, StringComparison.Ordinal);
        }
    }

    private static async Task SweepOnceAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        try
        {
            await scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest(
                backlogBatchSize: 100,
                recoveryScanBatchSize: 100,
                maxExecutionsPerSweep: 100));
        }
        catch (ResultCommitFaultException)
        {
            // The injected fault may surface to the sweep or be absorbed by the scheduler; either way nothing applies.
        }
    }

    private static string Name(string nodeId) => nodeId == LeftNodeId ? "left" : "right";

    private static WorkflowExecutable NewBackgroundThenDependentExecutable()
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(DependentStepActivity));
        var input = contract.Inputs[nameof(DependentStepActivity.Value)];
        var dependent = new ExecutableNode(
            executableNodeId: DependentNodeId,
            authoredActivityId: $"authored-{DependentNodeId}",
            activityType: typeof(DependentStepActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: contract.DescriptorKind,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [input.Key] = new(input.Key, input.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference(LeftVariableKey, VariableReference.WorkflowScopeId))
            },
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
        return NewSequenceExecutable(BackgroundNode(LeftNodeId, "left", LeftVariableKey, "left"), dependent);
    }

    private static WorkflowExecutable NewBackgroundThenFinalExecutable(ExecutableNode? background = null) => NewSequenceExecutable(
        background ?? BackgroundNode(LeftNodeId, "left", LeftVariableKey, "left"),
        InlineNode<FinalStepActivity>(FinalNodeId, new Dictionary<string, string>()));

    private static WorkflowExecutable NewSequenceExecutable(ExecutableNode first, ExecutableNode second)
    {
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
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [first, second])],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceExecutableStructure([first.ExecutableNodeId, second.ExecutableNodeId]))),
            activityContract: sequenceContract);
        return WorkflowExecutionHarness.NewExecutable(sequence, WorkflowExecutionHarness.Identity,
            new RuntimeVariableDeclaration(LeftVariableKey, "left", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline));
    }
}

public sealed class DependentStepObservations
{
    private readonly ConcurrentQueue<string?> _values = new();
    public IReadOnlyList<string?> Values => _values.ToArray();
    public void Record(string? value) => _values.Enqueue(value);
}

public sealed class DependentStepActivity(DependentStepObservations observations) : Activity<ActivityUnit>
{
    [ActivityInput]
    public string? Value { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        observations.Record(Value);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}

/// <summary>Fails every checkpoint that would complete one node, so its result application cannot commit.</summary>
public sealed class ResultCommitFault(string executableNodeId)
{
    private int _hits;

    public string ExecutableNodeId { get; } = executableNodeId;
    public int Hits => Volatile.Read(ref _hits);
    public void Hit() => Interlocked.Increment(ref _hits);
}

public sealed class ResultCommitFaultException(string message) : InvalidOperationException(message);

public sealed class ResultCommitFaultEnricher(ResultCommitFault fault) : IRuntimeCheckpointCommitEnricher
{
    public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(commit);

    public ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (commit.StateChanges.ActivityExecutions.Any(change =>
                change.State.Execution.ExecutableNodeId == fault.ExecutableNodeId && change.State.Status == ActivityExecutionStatus.Completed))
        {
            fault.Hit();
            throw new ResultCommitFaultException($"Injected fault in the result commit of '{fault.ExecutableNodeId}'.");
        }

        return ValueTask.CompletedTask;
    }
}
