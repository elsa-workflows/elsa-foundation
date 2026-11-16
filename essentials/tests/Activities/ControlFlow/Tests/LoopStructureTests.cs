using System.Text.Json;
using Elsa.Activities.ControlFlow.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>
/// The four loop activities share one structure handler and one navigator, parameterized by loop. These tests
/// pin what each loop must keep after that merge: its own structure kind and message label, the compiled payload
/// bytes that feed executable content hashes, and one rule for a structure-less node.
/// </summary>
public sealed class LoopStructureTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public LoopStructureTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
    }

    public static TheoryData<string> Loops => ["Do", "For", "ForEach", "While"];

    public void Dispose() => _provider.Dispose();

    [Theory]
    [MemberData(nameof(Loops))]
    public void Compiled_and_remapped_structure_payloads_keep_their_bytes(string loop)
    {
        var (structureKind, _, bodySlotName) = Identity(loop);
        var handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), handler => handler.Kind == structureKind);
        var node = new ActivityNode("node-loop", "activity-version-loop", [], []);
        var withBody = handler.ReplaceChildren(node, [new ActivityChildProjection(bodySlotName, [new ActivityNode("branch-body", "activity-version-branch-body", [], [])])]);
        var withoutBody = handler.ReplaceChildren(node, []);

        var compiled = handler.CompileExecutableStructure(withBody);
        var remapped = handler.RemapExecutableStructure(compiled, new Dictionary<string, string> { ["branch-body"] = "exec-body" });

        Assert.Equal(structureKind, compiled.Kind);
        Assert.Equal("""{"body":"branch-body"}""", compiled.Payload.GetRawText());
        Assert.Equal("""{"body":null}""", handler.CompileExecutableStructure(withoutBody).Payload.GetRawText());
        Assert.Equal("""{"body":null}""", withoutBody.Structure!.Payload.GetRawText());
        Assert.Equal(structureKind, remapped.Kind);
        Assert.Equal("""{"body":"exec-body"}""", remapped.Payload.GetRawText());
    }

    [Theory]
    [MemberData(nameof(Loops))]
    public async Task Body_slot_without_structure_fails_instead_of_running_no_passes(string loop)
    {
        // An empty body slot with no structure is malformed. Reading it as an empty body would complete the loop
        // exactly like a legitimately empty one, so the fault would never surface.
        var (structureKind, _, bodySlotName) = Identity(loop);
        var context = NewContext(loop, NewLoopNode(childSlots: [new ExecutableChildSlot(bodySlotName, [])]));

        var exception = await Assert.ThrowsAsync<ControlFlowExecutionException>(() => ExecuteAsync(context).AsTask());

        Assert.Equal($"{loop} executable node 'node-loop' requires structure '{structureKind}'.", exception.Message);
    }

    [Theory]
    [MemberData(nameof(Loops))]
    public async Task Node_without_slots_or_structure_runs_as_an_empty_loop(string loop)
    {
        // Every loop's inputs here would run a pass if the loop had a body, so completing without scheduling one
        // shows the navigator resolved an empty body rather than an unmet condition or range ending the loop.
        var context = NewContext(loop, NewLoopNode(childSlots: []));

        var continuation = await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, continuation.OutcomeName);
    }

    private static (string StructureKind, string StructureSchemaVersion, string BodySlotName) Identity(string loop) => loop switch
    {
        "Do" => (DoActivity.StructureKind, DoActivity.StructureSchemaVersion, DoActivity.BodySlotName),
        "For" => (ForActivity.StructureKind, ForActivity.StructureSchemaVersion, ForActivity.BodySlotName),
        "ForEach" => (ForEachActivity.StructureKind, ForEachActivity.StructureSchemaVersion, ForEachActivity.BodySlotName),
        "While" => (WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion, WhileActivity.BodySlotName),
        _ => throw new ArgumentOutOfRangeException(nameof(loop), loop, null)
    };

    private static IActivity NewRunnableLoop(string loop) => loop switch
    {
        "Do" => new DoActivity { Condition = true },
        "For" => new ForActivity { Start = 0, End = 3, Step = 1 },
        "ForEach" => new ForEachActivity { Collection = new[] { 1, 2 } },
        "While" => new WhileActivity { Condition = true },
        _ => throw new ArgumentOutOfRangeException(nameof(loop), loop, null)
    };

    private static ValueTask<RuntimeStructuralContinuation> ExecuteAsync(SimpleActivityExecutionContext context) =>
        ((IRuntimeStructuralActivity)context.Activity).ExecuteStructureAsync(context);

    private static SimpleActivityExecutionContext NewContext(string loop, ExecutableNode executableNode) =>
        new(
            NewRunnableLoop(loop),
            CancellationToken.None,
            "wfexec-1",
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            new RuntimeSchedulerWorkItem(
                workItemId: "work-loop",
                workflowExecutionId: "wfexec-1",
                commandId: "command-loop",
                commandKind: WorkflowExecutionCommandKind.InvokeActivity,
                envelopeId: "envelope-1",
                idempotencyKey: "wfexec-1:loop",
                enqueuedAt: DateTimeOffset.UnixEpoch,
                recordedAt: DateTimeOffset.UnixEpoch,
                sequence: 1,
                payload: null,
                commandMetadata: new Dictionary<string, string>(),
                envelopeMetadata: new Dictionary<string, string>()),
            executableNode,
            new ActivityExecutionState(
                Execution: new ActivityExecution("actexec-loop", "wfexec-1", "node-loop", "authored-loop", loop, "1.0.0"),
                Status: ActivityExecutionStatus.Running,
                SubStatus: null,
                ScheduledAt: DateTimeOffset.UnixEpoch,
                StartedAt: DateTimeOffset.UnixEpoch,
                CompletedAt: null,
                SchedulingActivityExecutionId: null,
                ParentActivityExecutionId: null,
                BranchId: null,
                IterationId: null,
                CallStackDepth: 0,
                BookmarkIds: [],
                IncidentIds: [],
                FaultCount: 0,
                AggregateFaultCount: 0,
                Metadata: new Dictionary<string, string>()));

    private static ExecutableNode NewLoopNode(IReadOnlyCollection<ExecutableChildSlot> childSlots) =>
        new(
            executableNodeId: "node-loop",
            authoredActivityId: "authored-node-loop",
            activityType: "loop",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: null);
}
