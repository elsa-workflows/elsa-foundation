using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 121 multi-instance activities (cardinality mode): a host runs its bound
/// child N times sequentially (one at a time) or in parallel (N concurrent same-node instances), each with
/// its own <c>loopIndex</c> frame; the loop routes the host's outbound flows once the last instance
/// completes. Composition with spec 120 boundary/error teardown: cancelling a multi-instance host cancels
/// all live instances, and an error boundary absorbs an instance fault.
/// </summary>
public sealed class BpmnMultiInstanceTests
{
    private const string BodyNodeId = "node-body";
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(params string[] activityExecutionIds) =>
        BpmnRuntimeFixture.CreateAsync(activityExecutionIds, services =>
            new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    [Fact]
    public async Task Sequential_EachInstanceSeesItsOwnLoopIndex_AndRoutesOnce()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-0", "actexec-1", "actexec-2"]);
        var run = await fixture.RunAsync(NewCardinalityExecutable(fixture, cardinality: 3, isSequential: true, NewLoopIndexCaptureNode(BodyNodeId)));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();

        IReadOnlyList<string?> captured = run.States(BodyNodeId)
            .Select(state => state.Completion!.Result.InlineValue!.Value.GetProperty("captured").GetString())
            .ToArray();
        Assert.Equal(["0", "1", "2"], captured);
    }

    [Fact]
    public async Task Sequential_RunsOneInstanceAtATime_NeverTwoLive()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-0", "actexec-1", "actexec-2");
        var run = await fixture.RunAsync(NewCardinalityExecutable(
            fixture, cardinality: 3, isSequential: true, NewDelayNode(BodyNodeId),
            [BpmnRuntimeFixture.NodeResumeTarget(BodyNodeId, DelayResumeTargetId)]));

        // Instance 0 alone is live; sequential mode never schedules the next until the current one completes.
        for (var completed = 0; completed < 3; completed++)
        {
            var bookmark = Assert.Single(await fixture.BookmarksAsync());
            Assert.Equal(BodyNodeId, bookmark.ExecutableNodeId);
            Assert.Equal(completed + 1, run.States(BodyNodeId).Count(state => state.Status == ActivityExecutionStatus.Waiting || state.Status == ActivityExecutionStatus.Suspended || state.Status == ActivityExecutionStatus.Completed));
            run = await fixture.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(bookmark.StimulusHash)));
        }

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(3, run.States(BodyNodeId).Count);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task Parallel_RunsConcurrentSameNodeInstances_WithDistinctIdentity_AndRoutesOnLastCompletion()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-0", "actexec-1", "actexec-2");
        var initial = await fixture.RunAsync(NewCardinalityExecutable(
            fixture, cardinality: 3, isSequential: false, NewDelayNode(BodyNodeId),
            [BpmnRuntimeFixture.NodeResumeTarget(BodyNodeId, DelayResumeTargetId)]));

        // First-ever proof of same-node concurrency: 3 live instances of ONE node, distinct aeis + iteration
        // ids, and 3 distinct aei-scoped bookmarks.
        var instances = initial.States(BodyNodeId);
        Assert.Equal(3, instances.Count);
        Assert.Equal(3, instances.Select(state => state.Execution.ActivityExecutionId).Distinct().Count());
        Assert.Equal(3, instances.Select(state => state.IterationId).Distinct().Count());
        Assert.All(instances, state => Assert.NotNull(state.IterationId));

        var bookmarks = await fixture.BookmarksAsync();
        Assert.Equal(3, bookmarks.Count);
        Assert.Equal(3, bookmarks.Select(bookmark => bookmark.ActivityExecutionId).Distinct().Count());

        // Resume each independently; the last completion routes the host's outbound flow.
        WorkflowExecutionRun run = initial;
        foreach (var bookmark in bookmarks)
            run = await fixture.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(bookmark.StimulusHash)));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task InterruptingBoundary_MidLoop_TearsDownAllLiveInstances_AndRoutesBoundaryPath()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-0", "actexec-1", "actexec-timer");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode(BodyNodeId, "go"), NewDelayNode("node-timer")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceTask("mi", childNodeId: BodyNodeId, cardinality: 2, isSequential: false),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "mi", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-boundary")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "mi"),
                BpmnRuntimeFixture.Flow("flow-2", "mi", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-timer", "end-boundary")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget(BodyNodeId, EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId)
            ]);

        var initial = await fixture.RunAsync(executable);

        // Two instance waits + one boundary timer listener (armed once on the coordinator).
        Assert.Equal(3, (await fixture.BookmarksAsync()).Count);
        var timerBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-timer");

        var resumed = await fixture.ResumeAsync(timerBookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(timerBookmark.StimulusHash)));

        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
        var instances = resumed.States(BodyNodeId);
        Assert.Equal(2, instances.Count);
        Assert.All(instances, state =>
        {
            Assert.Equal(ActivityExecutionStatus.Cancelled, state.Status);
            Assert.Equal(BpmnExecutionEngine.BoundaryHostInterruptedReason, state.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);
        });
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task InstanceFault_WithoutErrorBoundary_FaultsComposite()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-0"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewFaultingNode(BodyNodeId)],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceTask("mi", childNodeId: BodyNodeId, cardinality: 1, isSequential: true),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows: [BpmnRuntimeFixture.Flow("flow-1", "start", "mi"), BpmnRuntimeFixture.Flow("flow-2", "mi", "end")]);

        var run = await fixture.RunAsync(executable);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(BpmnRuntimeFixture.ProcessNodeId).Status);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task InstanceFault_WithErrorBoundary_AbsorbsAndRoutesErrorPath()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-0", "actexec-1"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewFaultingNode(BodyNodeId)],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceTask("mi", childNodeId: BodyNodeId, cardinality: 2, isSequential: true),
                BpmnRuntimeFixture.BoundaryEvent("b-error", "mi", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-error")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "mi"),
                BpmnRuntimeFixture.Flow("flow-2", "mi", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-error", "end-error")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Faulted, run.States(BodyNodeId).First().Status);

        var incident = Assert.Single(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(BpmnExecutionEngine.ErrorBoundaryAbsorptionReason, incident.Metadata[RuntimeMetadataKeys.FaultAbsorptionReason]);
    }

    [Fact]
    public async Task IdenticalRuns_ProduceIdenticalTokenAndLoopIds()
    {
        var first = await RunParallelLoopAsync();
        var second = await RunParallelLoopAsync();

        Assert.Equal(
            first.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal),
            second.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal));
    }

    private static async Task<BpmnExecutionState> RunParallelLoopAsync()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-0", "actexec-1");
        await fixture.RunAsync(NewCardinalityExecutable(
            fixture, cardinality: 2, isSequential: false, NewDelayNode(BodyNodeId),
            [BpmnRuntimeFixture.NodeResumeTarget(BodyNodeId, DelayResumeTargetId)]));
        return await fixture.GetBpmnStateAsync();
    }

    // ---- spec 123: collection-mode multi-instance ----

    private const string CollectionVariable = "items";

    [Fact]
    public async Task Collection_Sequential_EachInstanceSeesItsItemInOrder_AndRoutesOnce()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-0", "actexec-1", "actexec-2"]);
        var run = await fixture.RunAsync(NewCollectionExecutable(
            fixture, isSequential: true, NewItemCaptureNode(BodyNodeId, "item"), ["a", "b", "c"]));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();

        IReadOnlyList<string?> captured = run.States(BodyNodeId)
            .Select(state => state.Completion!.Result.InlineValue!.Value.GetProperty("captured").GetString())
            .ToArray();
        Assert.Equal(["a", "b", "c"], captured);
    }

    [Fact]
    public async Task Collection_Parallel_RunsConcurrentSameNodeInstances_EachObservingItsItem()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-0", "actexec-1", "actexec-2"]);
        var run = await fixture.RunAsync(NewCollectionExecutable(
            fixture, isSequential: false, NewItemCaptureNode(BodyNodeId, "item"), ["a", "b", "c"]));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();

        var instances = run.States(BodyNodeId);
        Assert.Equal(3, instances.Count);
        Assert.Equal(3, instances.Select(state => state.Execution.ActivityExecutionId).Distinct().Count());
        Assert.Equal(3, instances.Select(state => state.IterationId).Distinct().Count());
        Assert.Equal(
            new[] { "a", "b", "c" },
            instances.Select(state => state.Completion!.Result.InlineValue!.Value.GetProperty("captured").GetString()).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Collection_EmptyArray_RoutesImmediately_WithoutScheduling()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn"]);
        var run = await fixture.RunAsync(NewCollectionExecutable(
            fixture, isSequential: true, NewItemCaptureNode(BodyNodeId, "item"), []));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(run.States(BodyNodeId));
    }

    [Fact]
    public async Task Collection_NullVariable_RoutesImmediately_WithoutScheduling()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn"]);
        var run = await fixture.RunAsync(fixture.NewExecutable(
            children: [NewItemCaptureNode(BodyNodeId, "item")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceCollectionTask("mi", childNodeId: BodyNodeId, collectionVariable: CollectionVariable, isSequential: true),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows: [BpmnRuntimeFixture.Flow("flow-1", "start", "mi"), BpmnRuntimeFixture.Flow("flow-2", "mi", "end")],
            variables: [BpmnRuntimeFixture.NullVariable(CollectionVariable)]));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(run.States(BodyNodeId));
    }

    [Fact]
    public async Task Collection_NonArrayValue_FaultsDeterministically()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn"]);
        var run = await fixture.RunAsync(fixture.NewExecutable(
            children: [NewItemCaptureNode(BodyNodeId, "item")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceCollectionTask("mi", childNodeId: BodyNodeId, collectionVariable: CollectionVariable, isSequential: true),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows: [BpmnRuntimeFixture.Flow("flow-1", "start", "mi"), BpmnRuntimeFixture.Flow("flow-2", "mi", "end")],
            variables: [BpmnRuntimeFixture.InlineVariable(CollectionVariable, JsonSerializer.SerializeToElement("not-a-collection"))]));

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(BpmnRuntimeFixture.ProcessNodeId).Status);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.Empty(run.States(BodyNodeId));
    }

    [Fact]
    public async Task Collection_SnapshotPersistsOnLoopRecord_AndDrivesSequentialProgression()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-0", "actexec-1", "actexec-2");
        var run = await fixture.RunAsync(NewCollectionExecutable(
            fixture, isSequential: true, NewDelayNode(BodyNodeId), ["a", "b", "c"],
            [BpmnRuntimeFixture.NodeResumeTarget(BodyNodeId, DelayResumeTargetId)]));

        // Instance 0 alone is suspended, yet the loop record already carries the whole 3-item snapshot: later
        // instances are seeded from Items in a later evaluation, never re-reading the variable (snapshot discipline).
        var loop = Assert.Single((await fixture.GetBpmnStateAsync()).Loops);
        Assert.NotNull(loop.Items);
        Assert.Equal(["a", "b", "c"], loop.Items!.Select(item => item.GetString()));
        Assert.Equal(3, loop.TotalCount);

        var current = run;
        for (var completed = 0; completed < 3; completed++)
        {
            var bookmark = Assert.Single(await fixture.BookmarksAsync());
            current = await fixture.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(bookmark.StimulusHash)));
        }

        current.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        current.AssertWorkflowCompleted();
        Assert.Equal(3, current.States(BodyNodeId).Count);
    }

    [Fact]
    public async Task Collection_IdenticalRuns_ProduceIdenticalTokenAndLoopIds()
    {
        var first = await RunCollectionLoopAsync();
        var second = await RunCollectionLoopAsync();

        Assert.Equal(
            first.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal),
            second.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            first.Loops.Select(loop => loop.LoopId).OrderBy(value => value, StringComparer.Ordinal),
            second.Loops.Select(loop => loop.LoopId).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Collection_InterruptingBoundary_MidLoop_TearsDownAllInstances()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-0", "actexec-1", "actexec-timer");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode(BodyNodeId, "go"), NewDelayNode("node-timer")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceCollectionTask("mi", childNodeId: BodyNodeId, collectionVariable: CollectionVariable, isSequential: false),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "mi", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-boundary")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "mi"),
                BpmnRuntimeFixture.Flow("flow-2", "mi", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-timer", "end-boundary")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget(BodyNodeId, EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId)
            ],
            variables: [BpmnRuntimeFixture.StringArrayVariable(CollectionVariable, "a", "b")]);

        await fixture.RunAsync(executable);

        // Two instance waits (one per item) + one boundary timer listener armed once on the coordinator.
        Assert.Equal(3, (await fixture.BookmarksAsync()).Count);
        var timerBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-timer");

        var resumed = await fixture.ResumeAsync(timerBookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(timerBookmark.StimulusHash)));

        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
        var instances = resumed.States(BodyNodeId);
        Assert.Equal(2, instances.Count);
        Assert.All(instances, state => Assert.Equal(ActivityExecutionStatus.Cancelled, state.Status));
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task Collection_LoopStartReachedViaChildCompletion_ReadsCollectionThroughCompletionHandlerSeam()
    {
        // The multi-instance host is reached AFTER a preceding task completes, so the collection read happens in
        // the child-completion handler's populated seam (not the process-start invoke path).
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-t0", "actexec-0", "actexec-1"]);
        var run = await fixture.RunAsync(fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-t0"), NewItemCaptureNode(BodyNodeId, "item")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("t0", childNodeId: "node-t0"),
                BpmnRuntimeFixture.MultiInstanceCollectionTask("mi", childNodeId: BodyNodeId, collectionVariable: CollectionVariable, isSequential: true),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "t0"),
                BpmnRuntimeFixture.Flow("flow-2", "t0", "mi"),
                BpmnRuntimeFixture.Flow("flow-3", "mi", "end")
            ],
            variables: [BpmnRuntimeFixture.StringArrayVariable(CollectionVariable, "a", "b")]));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(
            ["a", "b"],
            run.States(BodyNodeId).Select(state => state.Completion!.Result.InlineValue!.Value.GetProperty("captured").GetString()));
    }

    private static async Task<BpmnExecutionState> RunCollectionLoopAsync()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-0", "actexec-1");
        await fixture.RunAsync(NewCollectionExecutable(
            fixture, isSequential: false, NewDelayNode(BodyNodeId), ["a", "b"],
            [BpmnRuntimeFixture.NodeResumeTarget(BodyNodeId, DelayResumeTargetId)]));
        return await fixture.GetBpmnStateAsync();
    }

    private static WorkflowExecutable NewCollectionExecutable(
        BpmnRuntimeFixture fixture,
        bool isSequential,
        ExecutableNode body,
        string[] items,
        IReadOnlyCollection<WorkflowExecutableResumeTarget>? resumeTargets = null) =>
        fixture.NewExecutable(
            children: [body],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceCollectionTask("mi", childNodeId: body.ExecutableNodeId, collectionVariable: CollectionVariable, isSequential: isSequential),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows: [BpmnRuntimeFixture.Flow("flow-1", "start", "mi"), BpmnRuntimeFixture.Flow("flow-2", "mi", "end")],
            resumeTargets: resumeTargets,
            variables: [BpmnRuntimeFixture.StringArrayVariable(CollectionVariable, items)]);

    private static ExecutableNode NewItemCaptureNode(string nodeId, string itemVariable) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(BpmnItemCaptureActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "clr" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Value"] = new RuntimeInputBinding(
                    inputKey: "Value",
                    targetType: new ValueTypeDescriptor("String"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference(itemVariable, BpmnRuntimeFixture.ProcessNodeId))
            },
            metadata: new Dictionary<string, string>());

    private static WorkflowExecutable NewCardinalityExecutable(
        BpmnRuntimeFixture fixture,
        int cardinality,
        bool isSequential,
        ExecutableNode body,
        IReadOnlyCollection<WorkflowExecutableResumeTarget>? resumeTargets = null) =>
        fixture.NewExecutable(
            children: [body],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceTask("mi", childNodeId: body.ExecutableNodeId, cardinality: cardinality, isSequential: isSequential),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows: [BpmnRuntimeFixture.Flow("flow-1", "start", "mi"), BpmnRuntimeFixture.Flow("flow-2", "mi", "end")],
            resumeTargets: resumeTargets);

    private static ExecutableNode NewLoopIndexCaptureNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(BpmnLoopIndexCaptureActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "clr" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Value"] = new RuntimeInputBinding(
                    inputKey: "Value",
                    targetType: new ValueTypeDescriptor("Int32"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference(BpmnLoopCharacteristics.LoopIndexVariable, BpmnRuntimeFixture.ProcessNodeId))
            },
            metadata: new Dictionary<string, string>());

    private static ExecutableNode NewDelayNode(string nodeId) =>
        NewClrNode(nodeId, typeof(Delay), new Dictionary<string, object?> { [nameof(Delay.Duration)] = TimeSpan.FromMinutes(5) });

    private static ExecutableNode NewEventWaitNode(string nodeId, string eventName) =>
        NewClrNode(nodeId, typeof(EventActivity), new Dictionary<string, object?>
        {
            [nameof(EventActivity.EventName)] = eventName,
            [nameof(EventActivity.CanStartWorkflow)] = false
        });

    private static ExecutableNode NewClrNode(string nodeId, Type activityType, IReadOnlyDictionary<string, object?> literals) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "clr" }),
            inputBindings: literals.ToDictionary(
                literal => literal.Key,
                literal => new RuntimeInputBinding(
                    inputKey: literal.Key,
                    targetType: new ValueTypeDescriptor("String"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        new ValueTypeDescriptor("String"),
                        JsonSerializer.SerializeToElement(literal.Value),
                        ValueProtectionPolicy.InstanceInline)),
                StringComparer.Ordinal),
            metadata: new Dictionary<string, string>());
}

/// <summary>A leaf body activity that reads its multi-instance <c>loopIndex</c> frame value and completes capturing it (spec 121 per-iteration frame proof).</summary>
public sealed class BpmnLoopIndexCaptureActivity : Activity<BpmnLoopCaptureResult>
{
    [ActivityInput]
    public int Value { get; set; }

    protected override ValueTask<ActivityTransition<BpmnLoopCaptureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new BpmnLoopCaptureResult(
            Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
}

/// <summary>A leaf body activity that reads its multi-instance collection <c>item</c> frame value and completes capturing it (spec 123 per-instance item proof).</summary>
public sealed class BpmnItemCaptureActivity : Activity<BpmnLoopCaptureResult>
{
    [ActivityInput]
    public string Value { get; set; } = string.Empty;

    protected override ValueTask<ActivityTransition<BpmnLoopCaptureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new BpmnLoopCaptureResult(Value)));
}

public sealed record BpmnLoopCaptureResult([property: Output] string Captured);
