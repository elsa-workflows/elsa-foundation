using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services.StepVariables;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1231 session branches that a whole workflow cannot easily reach, driven on hand-built frames: a withheld value,
/// a loop item, the <c>Fault</c>-mode digest on a session read, transient variables, and the engine surface.
/// </summary>
public sealed class StepVariableSessionUnitTests
{
    private const string CountKey = VariableLifecycleTestGraph.CountKey;
    private static readonly ValueTypeDescriptor Int32Type = VariableLifecycleTestGraph.Int32Type;

    [Fact]
    public void A_withheld_value_is_refused_with_VF_ACT_010_and_faults_on_the_host_side()
    {
        var withheld = ValueEnvelope.Withheld(Int32Type, WithheldValue.PolicyRequiresEncryption(),
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true));
        var session = Session(Root((CountKey, withheld)));

        var error = Assert.ThrowsAny<Exception>(() => session.Get(Count()));

        Assert.Contains("VF-ACT-010", error.Message, StringComparison.Ordinal);
        Assert.Same(error, session.PendingFault);
    }

    [Fact]
    public void A_loop_item_is_read_only_and_a_write_faults()
    {
        var root = Root((CountKey, Int(0)));
        var iteration = new VariableFrameState("frame-iteration", "node-loop", "actexec-loop:iteration:1", root.FrameId, VariableFrameKind.Iteration,
            new Dictionary<string, ValueEnvelope> { ["currentItem"] = Int(7) });
        var reference = new VariableReference<int>("currentItem", "node-loop", "currentItem");
        var session = Session([root, iteration], bound: new() { [("node-loop", "currentItem")] = true });

        Assert.Equal(7, session.Get(reference));
        Assert.Throws<InvalidOperationException>(() => session.Set(reference, 8));
        Assert.NotNull(session.PendingFault);
        Assert.Empty(session.Intents);
    }

    [Fact]
    public void Under_Fault_a_session_read_records_the_stored_digest_and_by_default_it_does_not()
    {
        var strict = Session(Root((CountKey, Int(3))), mode: LostUpdateMode.Fault);
        var lenient = Session(Root((CountKey, Int(3))));

        strict.Get(Count());
        lenient.Get(Count());

        Assert.NotNull(Assert.Single(strict.Reads).Entry.Digest);
        Assert.Null(Assert.Single(lenient.Reads).Entry.Digest);
    }

    [Fact]
    public void A_transient_variable_is_read_live_and_replaced_in_memory_only()
    {
        var live = new object();
        var session = Session(Root(("var-handle", ValueEnvelope.Transient(new ValueTypeDescriptor("Object"), live))),
            bound: new() { [("workflow", "var-handle")] = true });
        var reference = new VariableReference<object>("handle", "workflow", "var-handle");

        Assert.Same(live, session.Get(reference));
        var replacement = new object();
        session.Set(reference, replacement);

        Assert.Same(replacement, session.Get(reference));
        Assert.Empty(session.Intents);
        Assert.Empty(session.Reads);
        Assert.Null(session.PendingFault);
    }

    [Fact]
    public void A_transient_variable_read_as_the_wrong_type_faults_on_the_host_side()
    {
        var session = Session(Root(("var-handle", ValueEnvelope.Transient(new ValueTypeDescriptor("Object"), new object()))),
            bound: new() { [("workflow", "var-handle")] = true });

        Assert.Throws<InvalidOperationException>(() => session.Get(new VariableReference<string>("handle", "workflow", "var-handle")));
        Assert.NotNull(session.PendingFault);
    }

    [Fact]
    public void A_step_that_can_suspend_cannot_touch_a_transient_variable()
    {
        var session = Session(Root(("var-handle", ValueEnvelope.Transient(new ValueTypeDescriptor("Object"), new object()))),
            bound: new() { [("workflow", "var-handle")] = true }, suspends: true);

        Assert.Throws<InvalidOperationException>(() => session.Get(new VariableReference<object>("handle", "workflow", "var-handle")));
        Assert.NotNull(session.PendingFault);
    }

    [Fact]
    public void The_engine_surface_resolves_reads_and_writes_by_name_as_JSON()
    {
        var session = Session(Root((CountKey, Int(4))), engineConsumer: true);

        Assert.True(session.TryResolve("count", out var target));
        Assert.Equal(("workflow", CountKey), (target.ScopeId, target.VariableKey));
        Assert.Equal(4, session.ReadJson(target)!.GetValue<int>());
        session.WriteJson(target, JsonValue.Create(5));

        Assert.Equal(5, session.ReadJson(target)!.GetValue<int>());
        var intent = Assert.Single(session.Intents);
        Assert.Equal(5, intent.Value.InlineValue!.Value.GetInt32());
        Assert.Equal(0, intent.Base!.Value.Version);
        Assert.False(session.TryResolve("missing", out _));
        Assert.Null(session.PendingFault);
    }

    [Fact]
    public void The_engine_surface_refuses_a_transient_variable_on_the_host_side()
    {
        var session = Session(Root(("var-handle", ValueEnvelope.Transient(new ValueTypeDescriptor("Object"), new object()))), engineConsumer: true);

        Assert.True(session.TryResolve("handle", out var target));
        Assert.Throws<InvalidOperationException>(() => session.ReadJson(target));
        Assert.NotNull(session.PendingFault);
    }

    [Fact]
    public void The_engine_surface_refuses_a_write_to_a_transient_variable_on_the_host_side()
    {
        // Spec 1231 FR-020a (T029a): a computed script write that resolves to a transient variable faults the step.
        var session = Session(Root(("var-handle", ValueEnvelope.Transient(new ValueTypeDescriptor("Object"), new object()))), engineConsumer: true);

        Assert.True(session.TryResolve("handle", out var target));
        Assert.Throws<InvalidOperationException>(() => session.WriteJson(target, null));
        Assert.NotNull(session.PendingFault);
    }

    // FR-025, FR-026: the marker (also inherited) and the engine-consumer interface are one pinned fact, so the engine
    // surface is never a way around publication, the concurrent-write rule or background eligibility.
    [Theory]
    [InlineData(typeof(FreeNameSetActivity), true)]
    [InlineData(typeof(InheritedMarkerActivity), true)]
    [InlineData(typeof(EngineConsumerActivity), true)]
    [InlineData(typeof(SessionIncrementActivity), false)]
    public void The_marker_and_the_engine_consumer_interface_are_pinned_as_one_fact(Type activityType, bool expected)
    {
        Assert.Equal(expected, VariableReferenceTypes.AccessesUndeclaredVariables(activityType));
        Assert.Equal(expected, ClrActivityContractTestBuilder.BuildContract(activityType).AccessesUndeclaredVariables);
    }

    // FR-028: only a declared marker counts as one; an engine script activity's implied marker never does, so its
    // analyzed body keeps deciding its may-write set (and its catalog facet carries no `declared`).
    [Theory]
    [InlineData(typeof(FreeNameSetActivity), true)]
    [InlineData(typeof(InheritedMarkerActivity), true)]
    [InlineData(typeof(EngineConsumerActivity), false)]
    [InlineData(typeof(Elsa.Activities.Scripting.Activities.RunJavaScript), false)]
    [InlineData(typeof(SessionIncrementActivity), false)]
    public void Only_a_declared_marker_counts_as_declared(Type activityType, bool expected)
    {
        Assert.Equal(expected, VariableReferenceTypes.DeclaresAccessesUndeclaredVariables(activityType));
    }

    [Fact]
    public void A_typed_read_records_dropped_members_only_when_the_value_is_written_back()
    {
        var order = ValueEnvelope.Inline(StepVariableTestGraph.AnyType,
            JsonSerializer.SerializeToElement(new { total = 1, status = "open" }), ValueProtectionPolicy.InstanceInline);
        var session = Session(Root((StepVariableTestGraph.OrderKey, order)),
            bound: new() { [("workflow", StepVariableTestGraph.OrderKey)] = true });
        var reference = new VariableReference<OrderTotal>("order", "workflow", StepVariableTestGraph.OrderKey);

        var read = session.Get(reference);
        Assert.Empty(session.Intents);
        session.Set(reference, read);

        Assert.Equal(["$.status"], Assert.Single(session.Intents).DroppedMembers);
    }

    [Fact]
    public void A_read_modify_write_loop_keeps_the_dropped_members_of_the_stored_value()
    {
        // The second read returns the step's own narrowed write; the loss is still against the stored value.
        var order = ValueEnvelope.Inline(StepVariableTestGraph.AnyType,
            JsonSerializer.SerializeToElement(new { total = 1, status = "open" }), ValueProtectionPolicy.InstanceInline);
        var session = Session(Root((StepVariableTestGraph.OrderKey, order)),
            bound: new() { [("workflow", StepVariableTestGraph.OrderKey)] = true });
        var reference = new VariableReference<OrderTotal>("order", "workflow", StepVariableTestGraph.OrderKey);

        session.Set(reference, session.Get(reference));
        session.Set(reference, session.Get(reference));

        Assert.Equal(["$.status"], Assert.Single(session.Intents).DroppedMembers);
    }

    private static VariableReference<int> Count() => new("count", "workflow", CountKey);

    private static ValueEnvelope Int(int value) =>
        ValueEnvelope.Inline(Int32Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);

    private static VariableFrameState Root(params (string Key, ValueEnvelope Value)[] values) =>
        new("frame-root", "workflow", "workflow-run", null, VariableFrameKind.Root, values.ToDictionary(item => item.Key, item => item.Value));

    private static StepVariableSession Session(
        VariableFrameState root,
        LostUpdateMode mode = LostUpdateMode.Default,
        Dictionary<(string, string), bool>? bound = null,
        bool suspends = false,
        bool engineConsumer = false) =>
        Session([root], mode, bound, suspends, engineConsumer);

    private static StepVariableSession Session(
        VariableFrameState[] frames,
        LostUpdateMode mode = LostUpdateMode.Default,
        Dictionary<(string, string), bool>? bound = null,
        bool suspends = false,
        bool engineConsumer = false)
    {
        var node = StepVariableTestGraph.CountLeaf("node-root", typeof(SessionIncrementActivity), mode: mode);
        var executable = WorkflowExecutionHarness.NewExecutable(node,
            VariableLifecycleTestGraph.CountDeclaration(),
            // A declaration is always persistable; the frame holds the live (transient) value the session sees.
            new RuntimeVariableDeclaration("var-handle", "handle", new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline));
        var invocation = new ActivityExecutionState(
            new ActivityExecution("actexec-root", "workflow-run", "node-root", "authored-node-root", node.ActivityType, "1.0.0"),
            ActivityExecutionStatus.Running, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null, null, null, null,
            [], [], 0, 0, new Dictionary<string, string>());
        return new StepVariableSession(new RuntimeVisibleVariableFrames(frames), executable, invocation, node, validator: null,
            attemptId: "attempt-1", suspends: suspends,
            boundReferences: bound ?? new Dictionary<(string, string), bool> { [("workflow", CountKey)] = true },
            engineConsumer: engineConsumer);
    }
}
