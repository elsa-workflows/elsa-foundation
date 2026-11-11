using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;
using EventStimulus = Elsa.Activities.Primitives.Activities.EventStimulus;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// #4078 slice A adds the member-read model but no runtime that interprets it. A hand-built executable with a member
/// read runs through the real engine up to the reader, and the reader's input read is refused with a reason naming
/// the missing support, in a fresh start, after a bookmark resume and as queued start work. The reader is never
/// constructed, records no input snapshot and never receives the whole source value. Slice B replaces these rows with
/// real selection.
/// </summary>
public sealed class MemberReadRefusalTests
{
    private const string Key = nameof(InputReadFailureCharacterizationTests.Echo.Value);

    [Fact]
    public async Task A_member_read_is_refused_at_its_reader_on_a_fresh_start()
    {
        await using var harness = NewHarness();
        var root = GraphBoundaryCompleteTests.Sequence("node-root",
            Leaf("node-p", typeof(InputReadFailureCharacterizationTests.Stamp)),
            Leaf("node-r", typeof(InputReadFailureCharacterizationTests.EchoActivity), MemberRead("node-p")));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root),
            allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        run.AssertCompleted("node-p");
        await AssertRefusedAsync(harness, run, "node-r");
    }

    [Fact]
    public async Task A_member_read_is_refused_at_its_reader_after_a_bookmark_resume()
    {
        await using var harness = NewHarness();
        var waitTarget = WorkflowExecutableResumeTarget.ComposeScopedId("node-wait", EventActivity.ResumeTargetId);
        var root = GraphBoundaryCompleteTests.Sequence("node-root",
            Leaf("node-p", typeof(InputReadFailureCharacterizationTests.Stamp)),
            Nodes.Leaf("node-wait", typeof(EventActivity), new Dictionary<string, object?> { ["EventName"] = "go", ["CanStartWorkflow"] = false }),
            Leaf("node-r", typeof(InputReadFailureCharacterizationTests.EchoActivity), MemberRead("node-p")));
        var executable = new WorkflowExecutable(WorkflowExecutionHarness.Identity, root,
            new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [waitTarget] = new(waitTarget, "node-wait", "ResumeAsync", new Dictionary<string, string>(), EventActivity.ResumeTargetId)
            },
            WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);

        var suspended = await harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true);
        Assert.False(suspended.DidRun("node-r"));
        var wait = suspended.State("node-wait");
        await Record.ExceptionAsync(() => harness.ResumeAsync(WorkflowExecutionHarness.Identity, Assert.Single(wait.BookmarkIds),
            wait.InvocationId, "node-wait", waitTarget, EventStimulus.StimulusType, EventStimulus.Hash("go"),
            JsonSerializer.SerializeToElement(new { eventName = "go" })));

        await AssertRefusedAsync(harness, await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId), "node-r");
    }

    [Fact]
    public async Task A_member_read_is_refused_when_its_queued_start_work_is_drained()
    {
        await using var harness = NewHarness();
        var root = GraphBoundaryCompleteTests.Sequence("node-root",
            Leaf("node-r", typeof(InputReadFailureCharacterizationTests.EchoActivity), MemberRead("node-p", isOptional: true)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root),
            allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        // Even an optional read of a producer that never ran is refused, not read as no value.
        var incident = await AssertRefusedAsync(harness, run, "node-r");
        Assert.Equal("StartActivity", incident.Metadata["runtime.commandKind"]);
    }

    private static async Task<IncidentState> AssertRefusedAsync(WorkflowExecutionHarness harness, WorkflowExecutionRun run, string reader)
    {
        var blocked = run.States(reader).OrderBy(state => state.ExecutionSequence).Last();
        Assert.Equal(ActivityExecutionStatus.Scheduled, blocked.Status);
        Assert.Null(blocked.InputSnapshot);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.True(incident.IsBlocking);
        Assert.Equal(reader, incident.ExecutableNodeId);
        Assert.Equal(typeof(NotSupportedException).FullName, incident.Metadata["runtime.faultType"]);
        Assert.Contains("does not support member selection", incident.Message, StringComparison.Ordinal);
        Assert.Contains("elsa.runtime.input-member-selection@1", incident.Message, StringComparison.Ordinal);
        return incident;
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new Primitives.ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));

    private static RuntimeInputBinding MemberRead(string producerNodeId, bool isOptional = false) => new(
        Key, new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.ActivityResultMember,
        new RuntimeReadSelection(1, ["value"], RuntimeReadSelectionMode.DeferredFit),
        activityResult: new RuntimeActivityResultReference(producerNodeId, "$result", producerScopeId: null, isOptional));

    private static ExecutableNode Leaf(string nodeId, Type activityType, RuntimeInputBinding? read = null) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: activityType.FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: read is null
            ? new Dictionary<string, RuntimeInputBinding>()
            : new Dictionary<string, RuntimeInputBinding> { [read.InputName] = read },
        metadata: new Dictionary<string, string>());
}
