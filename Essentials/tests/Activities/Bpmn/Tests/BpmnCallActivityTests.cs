using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 133 call activities. The genuinely new engine piece is the failure-outcome
/// translation (D3): a call activity's bound <c>DispatchWorkflow</c> child COMPLETES with an outcome
/// (Faulted/DispatchFailed/Cancelled) rather than faulting, and the engine routes that terminal outcome through
/// BPMN error handling — a host error boundary, else the scope's error event subprocess, else a deterministic
/// composite fault — with no seam-B incident. These tests drive the ladder with a probe leaf that emits the
/// DispatchWorkflow outcome names directly (a faithful test double: the engine reads only the completion's
/// outcome names, never the child's activity type). The dispatch wait/resume path itself is proven by the
/// DispatchWorkflow test project; wiring the whole dispatch actor/delivery harness into the BPMN execution
/// harness is disproportionate (the two harnesses are structurally different — stop-and-report tripwire).
/// </summary>
public sealed class BpmnCallActivityTests
{
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(params string[] activityExecutionIds) =>
        BpmnRuntimeFixture.CreateAsync(activityExecutionIds, services =>
            new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    private static string[] Ids(int count) =>
        new[] { "actexec-bpmn" }.Concat(Enumerable.Range(0, count).Select(index => $"aei-{index}")).ToArray();

    /// <summary>The live process state when it faulted (private state still inline), else the last committed state (pruned on completion).</summary>
    private static async Task<BpmnExecutionState> PersistedStateAsync(BpmnRuntimeFixture fixture, WorkflowExecutionRun run)
    {
        var inline = run.State(BpmnRuntimeFixture.ProcessNodeId).PrivateState?.Value.InlineValue;
        return inline is { } value
            ? JsonSerializer.Deserialize<BpmnExecutionState>(value.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            : await fixture.GetBpmnStateAsync();
    }

    // ---- FR-2 / FR-4: non-failure outcomes route normal outbound -----------------------------------------------

    [Theory]
    [InlineData("Completed")] // FR-2: a waited call activity that completed routes normal outbound.
    [InlineData("Dispatched")] // FR-4: fire-and-forget routes normal outbound immediately.
    public async Task NonFailureOutcome_RoutesNormalOutbound(string outcome)
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-call", "actexec-after");
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", [outcome]), fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CallActivity("call", childNodeId: "node-call"),
                BpmnRuntimeFixture.Task("after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "after"),
                BpmnRuntimeFixture.Flow("flow-3", "after", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-after").Status);
        Assert.DoesNotContain((await PersistedStateAsync(fixture, run)).Diagnostics, diagnostic => diagnostic.Kind == BpmnDiagnosticKind.CallActivityFailureRouted);
    }

    [Fact]
    public async Task CompletedOutcome_DiscriminatedByConditionOutcomeFlow()
    {
        // FR-2: a conditionOutcome flow can still discriminate the (untouched) Completed outcome.
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-call", "actexec-ok", "actexec-other");
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", ["Completed"]), fixture.NewProbeNode("node-ok"), fixture.NewProbeNode("node-other")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CallActivity("call", childNodeId: "node-call"),
                BpmnRuntimeFixture.Task("ok", childNodeId: "node-ok"),
                BpmnRuntimeFixture.Task("other", childNodeId: "node-other"),
                BpmnRuntimeFixture.EndEvent("end-ok"),
                BpmnRuntimeFixture.EndEvent("end-other")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "ok", conditionOutcome: "Completed"),
                BpmnRuntimeFixture.Flow("flow-3", "call", "other", conditionOutcome: "SomethingElse"),
                BpmnRuntimeFixture.Flow("flow-4", "ok", "end-ok"),
                BpmnRuntimeFixture.Flow("flow-5", "other", "end-other")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-ok").Status);
        Assert.Empty(run.States("node-other"));
    }

    [Fact]
    public async Task TwoCallActivities_InOneProcess_BothRoute()
    {
        // FR-2: two call activities coexist in one process and both route their normal outbound.
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-a", "actexec-b");
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a", ["Completed"]), fixture.NewProbeNode("node-b", ["Completed"])],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CallActivity("call-a", childNodeId: "node-a"),
                BpmnRuntimeFixture.CallActivity("call-b", childNodeId: "node-b"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call-a"),
                BpmnRuntimeFixture.Flow("flow-2", "call-a", "call-b"),
                BpmnRuntimeFixture.Flow("flow-3", "call-b", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-a").Status);
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-b").Status);
    }

    // ---- FR-3 tier 1: host error boundary ----------------------------------------------------------------------

    [Theory]
    [InlineData("Faulted")]
    [InlineData("DispatchFailed")]
    [InlineData("Cancelled")]
    public async Task FailureOutcome_WithErrorBoundary_FiresBoundary_NoIncident(string outcome)
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-call", "actexec-recover");
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", [outcome]), fixture.NewProbeNode("node-recover")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CallActivity("call", childNodeId: "node-call"),
                BpmnRuntimeFixture.BoundaryEvent("b-error", "call", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.Task("recover", childNodeId: "node-recover"),
                BpmnRuntimeFixture.EndEvent("end-ok"),
                BpmnRuntimeFixture.EndEvent("end-error")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "end-ok"),
                BpmnRuntimeFixture.Flow("flow-3", "b-error", "recover"),
                BpmnRuntimeFixture.Flow("flow-4", "recover", "end-error")
            ]);

        var run = await fixture.RunAsync(executable);

        // The call activity's failure outcome routed through the error boundary to completion — no fault, no incident.
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-call").Status);
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-recover").Status);
        Assert.Empty(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Contains((await PersistedStateAsync(fixture, run)).Diagnostics, diagnostic =>
            diagnostic.Kind == BpmnDiagnosticKind.CallActivityFailureRouted && diagnostic.Message.Contains(outcome, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failure_WithErrorBoundary_TearsDownSiblingCatchListener()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-call", "actexec-timer");
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", ["Faulted"]), NewDelayNode("node-timer", TimeSpan.FromMinutes(5))],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CallActivity("call", childNodeId: "node-call"),
                BpmnRuntimeFixture.BoundaryEvent("b-error", "call", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "call", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.EndEvent("end-ok"),
                BpmnRuntimeFixture.EndEvent("end-error")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "end-ok"),
                BpmnRuntimeFixture.Flow("flow-3", "b-error", "end-error"),
                BpmnRuntimeFixture.Flow("flow-4", "b-timer", "end-ok")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        var listener = run.State("node-timer");
        Assert.Equal(ActivityExecutionStatus.Cancelled, listener.Status);
        Assert.Equal(BpmnExecutionEngine.CallActivityFailureRoutedReason, listener.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    // ---- FR-3 tier 2: scope error event subprocess -------------------------------------------------------------

    [Fact]
    public async Task Failure_NoBoundary_ButErrorEventSubprocess_ActivatesInterrupting()
    {
        await using var fixture = await CreateFixtureAsync(Ids(30));
        var esBody = BpmnRuntimeFixture.NestedProcessNode("node-esbody",
            elements: [BpmnRuntimeFixture.EventSubprocessErrorStart("es-start"), BpmnRuntimeFixture.Task("es-task", childNodeId: "node-es-probe"), BpmnRuntimeFixture.EndEvent("es-end")],
            flows: [BpmnRuntimeFixture.Flow("es-f1", "es-start", "es-task"), BpmnRuntimeFixture.Flow("es-f2", "es-task", "es-end")],
            innerChildren: [fixture.NewProbeNode("node-es-probe")]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", ["Faulted"]), esBody],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CallActivity("call", childNodeId: "node-call"),
                BpmnRuntimeFixture.EndEvent(),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        // No host boundary, but the scope error event subprocess absorbed the failure, ran its body, and completed.
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-es-probe"));
        // No incident — the child completed (with a failure outcome), it did not fault.
        Assert.Empty(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
    }

    // ---- FR-3 tier 3: no catcher → deterministic composite fault -----------------------------------------------

    [Theory]
    [InlineData("Faulted", BpmnExecutionEngine.CallActivityFaultedFaultCode)]
    [InlineData("DispatchFailed", BpmnExecutionEngine.CallActivityDispatchFailedFaultCode)]
    [InlineData("Cancelled", BpmnExecutionEngine.CallActivityCancelledFaultCode)]
    public async Task FailureOutcome_NoCatcher_FaultsCompositeWithPinnedCode(string outcome, string faultCode)
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-call");
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", [outcome])],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CallActivity("call", childNodeId: "node-call"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        var process = run.State(BpmnRuntimeFixture.ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, process.Status);
        Assert.Equal(faultCode, process.Fault?.Code);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.Contains((await PersistedStateAsync(fixture, run)).Diagnostics, diagnostic =>
            diagnostic.Kind == BpmnDiagnosticKind.Faulted && diagnostic.Message.Contains("call", StringComparison.Ordinal) && diagnostic.Message.Contains(outcome, StringComparison.Ordinal));
    }

    // ---- FR-5: multi-instance composition ----------------------------------------------------------------------

    [Fact]
    public async Task MultiInstanceCallActivity_InstanceFailure_FiresBoundary_InterruptsCoordinator()
    {
        // FR-5: a failing MI call activity instance rides the D3 ladder composed with the spec-121 coordinator
        // cascade — the host error boundary fires and interrupts the loop, so remaining instances never run.
        await using var fixture = await CreateFixtureAsync(Ids(30));
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", ["Faulted"]), fixture.NewProbeNode("node-recover")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceCallActivity("call", "node-call", cardinality: 3, isSequential: true),
                BpmnRuntimeFixture.BoundaryEvent("b-error", "call", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.Task("recover", childNodeId: "node-recover"),
                BpmnRuntimeFixture.EndEvent("end-ok"),
                BpmnRuntimeFixture.EndEvent("end-error")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "end-ok"),
                BpmnRuntimeFixture.Flow("flow-3", "b-error", "recover"),
                BpmnRuntimeFixture.Flow("flow-4", "recover", "end-error")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-recover").Status);
        // Sequential loop: only the first instance ran before the boundary interrupted the coordinator.
        Assert.Single(run.States("node-call"));
        Assert.Empty(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
    }

    [Fact]
    public async Task MultiInstanceCallActivity_AllInstancesComplete_RoutesCoordinatorOutbound()
    {
        // FR-2/FR-5: a non-failing MI call activity runs every instance and routes the coordinator's outbound.
        await using var fixture = await CreateFixtureAsync(Ids(30));
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", ["Completed"]), fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceCallActivity("call", "node-call", cardinality: 3, isSequential: true),
                BpmnRuntimeFixture.Task("after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "after"),
                BpmnRuntimeFixture.Flow("flow-3", "after", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(3, run.States("node-call").Count);
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-after").Status);
    }

    // ---- FR-7: determinism -------------------------------------------------------------------------------------

    [Fact]
    public async Task IdenticalRuns_ProduceIdenticalTokenIds()
    {
        var first = await RunFailureLadderAsync();
        var second = await RunFailureLadderAsync();

        Assert.Equal(
            first.Tokens.Select(token => token.TokenId).OrderBy(id => id, StringComparer.Ordinal),
            second.Tokens.Select(token => token.TokenId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(
            first.Diagnostics.Select(diagnostic => $"{diagnostic.Kind}:{diagnostic.ElementId}"),
            second.Diagnostics.Select(diagnostic => $"{diagnostic.Kind}:{diagnostic.ElementId}"));
    }

    private static async Task<BpmnExecutionState> RunFailureLadderAsync()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-call", "actexec-recover");
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-call", ["Faulted"]), fixture.NewProbeNode("node-recover")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CallActivity("call", childNodeId: "node-call"),
                BpmnRuntimeFixture.BoundaryEvent("b-error", "call", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.Task("recover", childNodeId: "node-recover"),
                BpmnRuntimeFixture.EndEvent("end-ok"),
                BpmnRuntimeFixture.EndEvent("end-error")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "call"),
                BpmnRuntimeFixture.Flow("flow-2", "call", "end-ok"),
                BpmnRuntimeFixture.Flow("flow-3", "b-error", "recover"),
                BpmnRuntimeFixture.Flow("flow-4", "recover", "end-error")
            ]);

        await fixture.RunAsync(executable);
        return await fixture.GetBpmnStateAsync();
    }

    private static ExecutableNode NewDelayNode(string nodeId, TimeSpan duration) =>
        NewClrNode(nodeId, typeof(Delay), new Dictionary<string, object?> { [nameof(Delay.Duration)] = duration });

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
