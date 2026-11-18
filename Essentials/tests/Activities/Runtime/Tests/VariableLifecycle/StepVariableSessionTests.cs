using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1231 (S-7) Phase 2 and User Stories 2 and 4 at run time: a step's variable session reads committed values, writes
/// intents committed with the step under the commit rule with source "session change", and saves nothing when the step
/// faults.
/// </summary>
public sealed class StepVariableSessionTests
{
    private static readonly string[] Ids = ["actexec-root"];
    private static readonly string[] ParallelIds = ["actexec-parallel", "actexec-left", "actexec-right"];

    [Fact]
    public async Task A_bound_reference_reads_and_writes_the_variable_committed_with_the_step()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(StepVariableTestGraph.CountLeaf("node-root", typeof(SessionIncrementActivity))));

        run.AssertWorkflowCompleted();
        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(run));
        var state = run.State("node-root");
        var record = Assert.Single(state.VariableWrites!);
        Assert.Equal(VariableWriteSource.SessionChange, record.Writer.Source);
        Assert.Equal("node-root", record.Writer.ExecutableNodeId);
        Assert.Equal(VariableCommitOutcome.Current, record.Outcome);
        Assert.Equal((0L, 1L), (record.BaseVersion!.Value, record.NewVersion));
        // The session read is recorded with its version and the variable's protection policy (FR-007).
        var read = Assert.Single(state.SessionReads!);
        Assert.Equal(VariableLifecycleTestGraph.CountKey, read.Entry.VariableKey);
        Assert.Equal(0, read.Entry.Version);
        Assert.Null(read.Entry.Digest);
        Assert.Equal(ValueProtectionPolicy.InstanceInline, read.Policy);
        // The own applied write advanced the invocation's base for count (FR-010).
        Assert.Equal(1, Assert.Single(state.OwnVariableBases!).Version);
    }

    [Fact]
    public async Task A_step_that_throws_after_writing_saves_nothing()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(StepVariableTestGraph.CountLeaf("node-root", typeof(WriteThenThrowActivity))));

        var state = run.State("node-root");
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Equal(0, StepVariableTestGraph.Version(run, VariableLifecycleTestGraph.CountKey).Version);
        Assert.Null(state.VariableWrites);
    }

    [Fact]
    public async Task A_step_that_returns_a_fault_after_writing_saves_nothing()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(StepVariableTestGraph.CountLeaf("node-root", typeof(WriteThenFaultActivity))));

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-root").Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Null(run.State("node-root").VariableWrites);
    }

    [Fact]
    public async Task A_step_that_cancels_after_writing_saves_nothing()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(StepVariableTestGraph.CountLeaf("node-root", typeof(WriteThenCancelActivity))));

        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-root").Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Equal(0, StepVariableTestGraph.Version(run, VariableLifecycleTestGraph.CountKey).Version);
        Assert.Null(run.State("node-root").VariableWrites);
    }

    [Fact]
    public async Task A_read_after_a_write_in_the_same_step_returns_the_written_value()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(
            StepVariableTestGraph.CountLeaf("node-root", typeof(ReadYourWritesActivity), captureInto: StepVariableTestGraph.OtherKey)));

        run.AssertWorkflowCompleted();
        Assert.Equal(7, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Equal(7, StepVariableTestGraph.ReadInt(run, StepVariableTestGraph.OtherKey));
        // Reading its own pending write is not a read of a committed value.
        Assert.Null(run.State("node-root").SessionReads);
    }

    [Fact]
    public async Task Writing_the_value_a_variable_already_holds_writes_nothing_and_keeps_the_version()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(StepVariableTestGraph.CountLeaf("node-root", typeof(WriteSameValueActivity))));

        run.AssertWorkflowCompleted();
        Assert.Equal(0, StepVariableTestGraph.Version(run, VariableLifecycleTestGraph.CountKey).Version);
        Assert.Null(run.State("node-root").VariableWrites);
        Assert.Null(run.State("node-root").OwnVariableBases);
    }

    [Fact]
    public async Task Setting_a_read_only_reference_throws_and_faults_the_step()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(StepVariableTestGraph.CountLeaf("node-root", typeof(SetReadOnlyActivity))));

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-root").Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
    }

    [Fact]
    public async Task Changing_a_copy_without_Set_writes_nothing()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);
        var order = new { total = 10, status = "open" };

        var run = await harness.RunAsync(StepVariableTestGraph.Single(OrderLeaf(typeof(ChangeCopyWithoutSetActivity)), StepVariableTestGraph.OrderDeclaration(order)));

        run.AssertWorkflowCompleted();
        var stored = run.WorkflowState!.RootVariableFrame!.Values[StepVariableTestGraph.OrderKey].InlineValue!.Value;
        Assert.Equal(10, stored.GetProperty("total").GetInt32());
        Assert.Equal(0, StepVariableTestGraph.Version(run, StepVariableTestGraph.OrderKey).Version);
        Assert.Null(run.State("node-root").VariableWrites);
    }

    [Fact]
    public async Task An_Any_variable_read_as_a_JSON_node_and_set_back_keeps_the_members_the_step_did_not_touch()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);
        var order = new { total = 10, status = "open", lines = new[] { 1, 2 } };

        var run = await harness.RunAsync(StepVariableTestGraph.Single(OrderLeaf(typeof(ChangeJsonMemberActivity)), StepVariableTestGraph.OrderDeclaration(order)));

        run.AssertWorkflowCompleted();
        var stored = run.WorkflowState!.RootVariableFrame!.Values[StepVariableTestGraph.OrderKey].InlineValue!.Value;
        Assert.Equal(1, stored.GetProperty("total").GetInt32());
        Assert.Equal("open", stored.GetProperty("status").GetString());
        Assert.Equal(2, stored.GetProperty("lines").GetArrayLength());
        Assert.Null(Assert.Single(run.State("node-root").VariableWrites!).DroppedMembers);
    }

    [Fact]
    public async Task A_write_of_a_value_read_as_a_narrower_type_warns_about_the_members_it_dropped()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);
        var order = new { total = 10, status = "open" };

        var run = await harness.RunAsync(StepVariableTestGraph.Single(OrderLeaf(typeof(NarrowOrderWriteActivity)), StepVariableTestGraph.OrderDeclaration(order)));

        run.AssertWorkflowCompleted();
        var record = Assert.Single(run.State("node-root").VariableWrites!);
        Assert.Equal(["$.status"], record.DroppedMembers);
    }

    [Fact]
    public async Task A_marked_activity_reads_and_writes_by_free_name()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(
            StepVariableTestGraph.Leaf("node-root", typeof(FreeNameSetActivity), new Dictionary<string, RuntimeInputBinding>())));

        run.AssertWorkflowCompleted();
        Assert.Equal(5, StepVariableTestGraph.ReadInt(run, StepVariableTestGraph.OtherKey));
        var record = Assert.Single(run.State("node-root").VariableWrites!);
        Assert.Equal(StepVariableTestGraph.OtherKey, record.VariableKey);
        Assert.Equal(VariableCommitOutcome.Blind, record.Outcome);
    }

    [Fact]
    public async Task An_unmarked_activity_that_uses_a_free_name_faults()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(
            StepVariableTestGraph.Leaf("node-root", typeof(UnmarkedFreeNameActivity), new Dictionary<string, RuntimeInputBinding>())));

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-root").Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
    }

    [Fact]
    public async Task A_marked_activity_naming_an_undeclared_variable_faults_and_saves_nothing()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(
            StepVariableTestGraph.Leaf("node-root", typeof(FreeNameUndeclaredActivity), new Dictionary<string, RuntimeInputBinding>())));

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-root").Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
        Assert.False(run.WorkflowState!.RootVariableFrame!.Values.ContainsKey("total"));
    }

    [Fact]
    public async Task A_value_of_the_wrong_type_faults_the_step_even_when_the_body_catches_the_exception()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(
            StepVariableTestGraph.Leaf("node-root", typeof(WrongTypeCaughtActivity), new Dictionary<string, RuntimeInputBinding>())));

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-root").Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
    }

    [Fact]
    public async Task A_write_to_a_container_variable_commits_on_its_owner_frame()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(ParallelIds);
        static ExecutableNode InnerLeaf(string nodeId) =>
            StepVariableTestGraph.Leaf(nodeId, typeof(ContainerWriteActivity), new Dictionary<string, RuntimeInputBinding>
            {
                ["Inner"] = StepVariableTestGraph.Reference("Inner", "inner", StepVariableTestGraph.InnerKey, scopeId: "node-parallel")
            });

        var run = await harness.RunAsync(StepVariableTestGraph.ParallelWithContainerVariable(InnerLeaf("node-left"), InnerLeaf("node-right")));

        run.AssertWorkflowCompleted();
        // Both branches incremented the container's variable in their own step, inline: 2, on the Parallel's own frame.
        var frame = run.State("node-parallel").VariableFrame!;
        Assert.Equal(2, frame.Values[StepVariableTestGraph.InnerKey].InlineValue!.Value.GetInt32());
        Assert.Equal(2, frame.VersionOf(StepVariableTestGraph.InnerKey).Version);
        Assert.Equal("node-parallel", Assert.Single(run.State("node-right").VariableWrites!).ScopeId);
    }

    // FR-023, FR-024, FR-025: a step reaches only the variables its inputs bind, in their direction, unless its type is
    // marked; the engine surface is for engine script activities.
    [Theory]
    [InlineData(typeof(ForgedReferenceActivity))]
    [InlineData(typeof(RewrappedReadOnlyActivity))]
    [InlineData(typeof(EngineSurfaceCastActivity))]
    public async Task A_step_cannot_write_a_variable_its_inputs_do_not_bind_writable(Type activityType)
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(StepVariableTestGraph.Single(StepVariableTestGraph.CountLeaf("node-root", activityType)));

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-root").Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Equal(0, StepVariableTestGraph.ReadInt(run, StepVariableTestGraph.OtherKey));
        Assert.Null(run.State("node-root").VariableWrites);
    }

    // FR-025, User Story 4 scenario 1: a free name resolves to the nearest visible declaration. The Parallel declares its
    // own "other", which shadows the workflow's "other": the write lands on the container frame, the root keeps its value.
    [Fact]
    public async Task A_free_name_resolves_to_the_nearest_visible_declaration()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(ParallelIds);
        static ExecutableNode FreeNameLeaf(string nodeId) =>
            StepVariableTestGraph.Leaf(nodeId, typeof(FreeNameSetActivity), new Dictionary<string, RuntimeInputBinding>());

        var run = await harness.RunAsync(StepVariableTestGraph.ParallelWithContainerVariable(FreeNameLeaf("node-left"), FreeNameLeaf("node-right"), name: "other"));

        run.AssertWorkflowCompleted();
        Assert.Equal(5, run.State("node-parallel").VariableFrame!.Values[StepVariableTestGraph.InnerKey].InlineValue!.Value.GetInt32());
        Assert.Equal(0, StepVariableTestGraph.ReadInt(run, StepVariableTestGraph.OtherKey));
        Assert.Equal("node-parallel", Assert.Single(run.State("node-left").VariableWrites!).ScopeId);
    }

    private static ExecutableNode OrderLeaf(Type activityType) =>
        StepVariableTestGraph.Leaf("node-root", activityType, new Dictionary<string, RuntimeInputBinding>
        {
            ["Order"] = StepVariableTestGraph.Reference("Order", "order", StepVariableTestGraph.OrderKey)
        });
}
