using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Projection invariants only; emitted control has no authority or external effect before its checkpoint.</summary>
public sealed class RuntimeStructuralControlProjectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Control_and_private_state_are_projected_together_without_mutating_input()
    {
        var original = Root();
        var batch = Batch(0);
        var value = ValueEnvelope.Inline(new ValueTypeDescriptor("test.private"), JsonSerializer.SerializeToElement(new { cursor = 7 }), ValueProtectionPolicy.InstanceInline);

        var projected = RuntimeStructuralStateProjector.Apply(original,
            RuntimeStructuralContinuation.Defer.WithControl(batch).WithState(value, 2), Now);

        Assert.Null(original.ProducedControl);
        Assert.Null(original.PrivateState);
        Assert.Same(batch, projected.ProducedControl);
        Assert.Same(value, projected.PrivateState!.Value);
        Assert.Equal(2, projected.PrivateState.StateVersion);
        Assert.Equal("attempt", projected.PrivateState.ProducedByAttemptId);
    }

    [Fact]
    public void Control_only_needs_no_dummy_private_document_and_a_later_plain_projection_keeps_it()
    {
        var first = RuntimeStructuralStateProjector.Apply(Root(), RuntimeStructuralContinuation.Defer.WithControl(Batch(0)), Now);
        Assert.NotNull(first.ProducedControl);
        Assert.Null(first.PrivateState);
        Assert.Same(first, RuntimeStructuralStateProjector.Apply(first, RuntimeStructuralContinuation.Defer, Now));
        var value = ValueEnvelope.Inline(new ValueTypeDescriptor("test.private"), JsonSerializer.SerializeToElement(new { }), ValueProtectionPolicy.InstanceInline);
        var later = RuntimeStructuralStateProjector.Apply(first, RuntimeStructuralContinuation.Defer.WithState(value), Now);
        Assert.Same(first.ProducedControl, later.ProducedControl);
    }

    [Fact]
    public void Root_advances_exactly_one_ordinal_and_the_terminal_barrier_seals_it()
    {
        var first = RuntimeStructuralStateProjector.Apply(Root(), RuntimeStructuralContinuation.Defer.WithControl(Batch(0)), Now);
        var terminal = Batch(1, source: "terminal", terminal: true);
        var sealedRoot = RuntimeStructuralStateProjector.Apply(first, RuntimeStructuralContinuation.Complete().WithControl(terminal), Now);
        Assert.Same(terminal, sealedRoot.ProducedControl);
        Assert.Throws<InvalidOperationException>(() => RuntimeStructuralStateProjector.Apply(sealedRoot,
            RuntimeStructuralContinuation.Defer.WithControl(Batch(2, source: "after-terminal")), Now));
    }

    [Fact]
    public void Source_commit_anchor_survives_equivalent_replay_and_resets_only_for_a_new_batch()
    {
        var batch = Batch(0);
        var first = RuntimeStructuralStateProjector.Apply(Root(), RuntimeStructuralContinuation.Defer.WithControl(batch), Now)
            with { ProducedControlSourceCommitId = "original-commit" };
        var repeated = RuntimeStructuralStateProjector.Apply(first, RuntimeStructuralContinuation.Defer.WithControl(batch), Now);
        Assert.Equal("original-commit", repeated.ProducedControlSourceCommitId);
        var next = RuntimeStructuralStateProjector.Apply(first,
            RuntimeStructuralContinuation.Defer.WithControl(Batch(1, source: "next-work")), Now);
        Assert.Null(next.ProducedControlSourceCommitId);
        Assert.Equal(1, next.ProducedControl!.Ordinal);
    }

    [Theory]
    [InlineData("first-gap")]
    [InlineData("next-gap")]
    [InlineData("repeat-changed-body")]
    [InlineData("repeat-changed-ordinal")]
    [InlineData("consumer-change")]
    [InlineData("consumer-version-change")]
    public void Gaps_and_conflicting_stream_or_source_identity_refuse(string defect)
    {
        var first = RuntimeStructuralStateProjector.Apply(Root(), RuntimeStructuralContinuation.Defer.WithControl(Batch(0)), Now);
        var state = defect == "first-gap" ? Root() : first;
        var next = defect switch
        {
            "first-gap" => Batch(1),
            "next-gap" => Batch(2, source: "next"),
            "repeat-changed-body" => Batch(0, code: "different"),
            "repeat-changed-ordinal" => Batch(1),
            "consumer-change" => Batch(1, source: "next", consumer: "different"),
            _ => Batch(1, source: "next", consumerVersion: 2)
        };
        Assert.Throws<InvalidOperationException>(() => RuntimeStructuralStateProjector.Apply(state,
            RuntimeStructuralContinuation.Defer.WithControl(next), Now));
    }

    [Fact]
    public void Repeated_latest_source_preserves_its_batch_and_ordinal()
    {
        var batch = Batch(0);
        var first = RuntimeStructuralStateProjector.Apply(Root(), RuntimeStructuralContinuation.Defer.WithControl(batch), Now);
        var restoredBatch = JsonSerializer.Deserialize<RuntimeChildControlBatch>(JsonSerializer.Serialize(batch))!;
        var repeated = RuntimeStructuralStateProjector.Apply(first, RuntimeStructuralContinuation.Defer.WithControl(restoredBatch), Now);
        Assert.Equal(0, repeated.ProducedControl!.Ordinal);
        Assert.Equal(batch.SourceWorkItemId, repeated.ProducedControl.SourceWorkItemId);
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("foreign-root")]
    [InlineData("foreign-workflow")]
    [InlineData("no-attempt")]
    public void Only_the_exact_open_root_attempt_can_stage_control(string defect)
    {
        var state = Root();
        if (defect == "nested") state = state with { ParentActivityExecutionId = "parent" };
        if (defect == "no-attempt") state = state with { Attempts = [] };
        var batch = Batch(0, root: defect == "foreign-root" ? "other" : "root", workflow: defect == "foreign-workflow" ? "other" : "workflow");
        Assert.Throws<InvalidOperationException>(() => RuntimeStructuralStateProjector.Apply(state,
            RuntimeStructuralContinuation.Defer.WithControl(batch), Now));
        Assert.Null(state.ProducedControl);
    }

    private static ActivityExecutionState Root() => new ActivityExecutionState(
        new ActivityExecution("root", "workflow", "node", "authored", "test", "1"), ActivityExecutionStatus.Running,
        null, 0, Now, Now, null, null, null, null, null,
        ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, null, "WorkflowStart"),
        null, [], [], 0, 0, new Dictionary<string, string>())
        { Attempts = [new ActivityAttempt("attempt", "root", 1, ActivityAttemptReason.Initial, Now)] };

    private static RuntimeChildControlBatch Batch(long ordinal, string source = "work", bool terminal = false,
        string code = "notice", string consumer = "test.control", int consumerVersion = 1,
        string root = "root", string workflow = "workflow") => new(1, consumer, consumerVersion, workflow,
        root, source, ordinal, terminal, terminal ? [] : [new RuntimeChildControlNotification(code)]);
}
