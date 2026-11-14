using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class QueuedSelectedStartTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Exact_selected_invocation_is_stamped_only_by_atomic_admission_and_survives_writes_and_closure(PersistenceMode mode)
    {
        var owner = LexicalOwner();
        owner.WrapSelected = selected => LexicalContainer(selected, false, Declaration("amount"));
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint,
            [new("container", VariableFrameKind.Container, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) })], []);
        await using var harness = Build(owner, mode, services =>
            new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services));
        await Setup(harness, owner, Echo(Literal("value", 7)));
        var prepared = await harness.Services.GetRequiredService<IRuntimeTestContextPreparer>().PrepareAsync(
            owner.Entry!, owner.Executable!, harness.ExecutionId, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>());
        Assert.Null(prepared.Context!.RootActivityExecutionId);
        Assert.DoesNotContain("RootActivityExecutionId", JsonSerializer.Serialize(prepared.Context));
        var checkpoint = await StageCheckpoint(harness, owner);
        var root = SelectedRootWork(checkpoint).Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        await Checkpoints(harness).HandleAsync(checkpoint);
        var stored = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        var context = JsonSerializer.Deserialize<RuntimeTestContextState>(JsonSerializer.Serialize(stored.TestContext))!;
        Assert.Equal(root.ActivityExecutionId, context.RootActivityExecutionId);
        var frame = Assert.Single(context.Frames).Frame;
        context = context.Set(frame.FrameId, "amount", Number(42), frame.Revision);
        Assert.Equal(root.ActivityExecutionId, context.RootActivityExecutionId);
        Assert.Equal(root.ActivityExecutionId, context.Close().RootActivityExecutionId);
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        await harness.SweepUntilQuietAsync();
        var final = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, final.Status);
        Assert.Equal(root.ActivityExecutionId, final.TestContext!.RootActivityExecutionId);
        Assert.Equal(root.ActivityExecutionId, Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId)).InvocationId);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("parent")]
    [InlineData("scheduler")]
    [InlineData("node")]
    [InlineData("provenance")]
    [InlineData("missing-stamp")]
    public async Task A_schedule_cannot_create_another_invocation_under_the_admitted_context(string defect)
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced);
        await Setup(harness, owner);
        var checkpoint = await StageCheckpoint(harness, owner);
        await Checkpoints(harness).HandleAsync(checkpoint);
        var rootWork = SelectedRootWork(checkpoint);
        var root = rootWork.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        var states = harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var stored = (await states.FindAsync(harness.ExecutionId))!;
        if (defect == "missing-stamp") await states.SaveAsync(stored with { TestContext = stored.TestContext! with { RootActivityExecutionId = null } });
        var forged = new RuntimeScheduleActivityCommandPayload(root.PinnedExecutable,
            defect == "node" ? "before" : root.ExecutableNodeId,
            defect == "duplicate" ? "another-invocation" : root.ActivityExecutionId, root.Reason,
            defect == "scheduler" ? "foreign" : root.SchedulingActivityExecutionId,
            defect == "parent" ? "foreign" : root.ParentActivityExecutionId,
            defect == "provenance" ? root.SchedulingProvenance with { BranchId = "foreign" } : root.SchedulingProvenance);
        var before = JsonSerializer.Serialize(await states.FindAsync(harness.ExecutionId));
        var queued = JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Assert.Single(harness.Services.GetServices<IWorkflowSchedulerWorkHandler>()
            .OfType<WorkflowScheduleActivitySchedulerWorkHandler>()).HandleAsync(Copy(rootWork, JsonSerializer.SerializeToElement(forged), "forged")));
        Assert.Equal(before, JsonSerializer.Serialize(await states.FindAsync(harness.ExecutionId)));
        Assert.Equal(queued, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId))));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("parent")]
    [InlineData("missing-stamp")]
    public async Task Another_invocation_of_the_same_node_cannot_read_frames_fixtures_or_capture(string defect)
    {
        var owner = ResultOwner("present");
        owner.WrapSelected = selected =>
        {
            var container = LexicalContainer(selected, true, Declaration("amount"));
            return new(container.ExecutableNodeId, container.AuthoredActivityId, container.ActivityType, container.ActivityTypeVersion,
                container.DescriptorType, container.DescriptorPayload, container.InputBindings, container.Metadata,
                childSlots: [new("children", [FixtureProducer("present"), selected, WorkflowExecutionHarness.NewProbeNode("after")])], structure: container.Structure);
        };
        var resultSeed = owner.Seed!;
        owner.Seed = entry => resultSeed(entry) with
        {
            Frames = [new("container", VariableFrameKind.Root, new Dictionary<string, ValueEnvelope> { ["amount"] = Number(7) }, true,
                new Dictionary<string, ValueEnvelope> { ["input"] = Number(7) })]
        };
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
        {
            services.AddSingleton<IRuntimeTestScopeContractSource, ClosedLexicalContract>();
            new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        });
        await Setup(harness, owner, ResultConsumer(false));
        await Checkpoints(harness).HandleAsync(await StageCheckpoint(harness, owner));
        var store = harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var running = (await store.FindAsync(harness.ExecutionId))!;
        await harness.SweepUntilQuietAsync();
        var activities = harness.Services.GetRequiredService<IActivityExecutionStateStore>();
        var consumer = Assert.Single(await activities.ListAllAsync(harness.ExecutionId));
        await store.SaveAsync(running);
        var frames = new RuntimeContainerScopeService(activities, store);
        Assert.NotEmpty((await frames.BuildVisibleFramesAsync(harness.ExecutionId, consumer)).Frames);
        var reference = new RuntimeActivityResultReference("before", "$result", "fixture-scope", true);
        RuntimeInputBindingResolutionContext ReadContext(ActivityExecutionState invocation, WorkflowExecutionState state) =>
            new(harness.ExecutionId, invocation.InvocationId, invocation, executable: owner.Executable) { WorkflowState = state };
        Assert.Equal("fixture-value", RuntimeActivityResultReader.Read(reference, ReadContext(consumer, running))!.Value.InlineValue!.Value.GetString());
        var capture = harness.Services.GetRequiredService<IRuntimeTestContextFrameAccess>();
        Assert.NotNull(await capture.FindCaptureTargetAsync(harness.ExecutionId, consumer.InvocationId, "container"));
        var forged = defect == "duplicate" ? consumer with { Execution = consumer.Execution with { ActivityExecutionId = "another-invocation" } } :
            defect == "parent" ? consumer with { ParentActivityExecutionId = "foreign" } : consumer;
        if (defect == "missing-stamp") running = running with { TestContext = running.TestContext! with { RootActivityExecutionId = null } };
        await store.SaveAsync(running);
        await activities.SaveAsync(forged);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await frames.BuildVisibleFramesAsync(harness.ExecutionId, forged));
        Assert.Throws<InvalidOperationException>(() => RuntimeActivityResultReader.Read(reference, ReadContext(forged, running)));
        Assert.Throws<InvalidOperationException>(() => RuntimeActivityResultReader.Read(new ActivityResultExpressionParameterBinding("before", "$result", true), ReadContext(forged, running)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await capture.FindCaptureTargetAsync(harness.ExecutionId, forged.InvocationId, "container"));
        Assert.Equal(7, Assert.Single((await store.FindAsync(harness.ExecutionId))!.TestContext!.Frames).Frame.Values["amount"].InlineValue!.Value.GetInt32());
    }

    private static RuntimeSchedulerWorkItem SelectedRootWork(RuntimeSchedulerWorkItem checkpoint) =>
        checkpoint.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!.PostCommitIntents.Single().Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
}
