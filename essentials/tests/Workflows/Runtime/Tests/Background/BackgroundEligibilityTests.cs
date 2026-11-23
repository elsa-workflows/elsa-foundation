using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Background;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Background;

/// <summary>
/// Spec 1230 (ADR 0093 decision 3): one component decides background eligibility. Each test builds a subject that
/// fails exactly one criterion and checks that both entry points report it by id.
/// </summary>
public sealed class BackgroundEligibilityTests
{
    public static TheoryData<string> EntryPoints => new() { "publication", "run time" };

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void An_eligible_leaf_passes(string entryPoint) =>
        Assert.Null(Check(entryPoint, Subject<SafeLeaf>()));

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C1_refuses_intrinsics_boundaries_unregistered_types_structure_children_and_handlers(string entryPoint)
    {
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C1, Subject<SafeLeaf>() with { IsIntrinsic = true }, "intrinsics");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C1, Subject<SafeLeaf>() with { IsReusableBoundary = true }, "reusable");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C1, Subject<SafeLeaf>() with { ActivityType = null }, "registered CLR activity");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C1, Subject<SafeLeaf>() with { HasStructure = true }, "structural");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C1, Subject<SafeLeaf>() with { HasChildren = true }, "children");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C1, Subject<OutcomeHandlerLeaf>(), "structural");
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C2_refuses_a_stateful_activity(string entryPoint) =>
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C2, Subject<StatefulLeaf>(), "stateful");

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C3_refuses_a_missing_contract_and_untyped_results(string entryPoint)
    {
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C3, Subject<SafeLeaf>() with { Contract = null }, "typed result contract");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C3, Subject<ObjectLeaf>(Contract<ObjectLeaf>(result: Result("Object"))), "untyped");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C3, Subject<SafeLeaf>(Contract<SafeLeaf>(result: Result("Any"))), "untyped");
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C4_refuses_stream_transient_and_non_persistable_results(string entryPoint)
    {
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C4, Subject<StreamLeaf>(Contract<StreamLeaf>(result: Result("Stream"))), "transient");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C4,
            Subject<SafeLeaf>(Contract<SafeLeaf>(result: Result("Int32", projection: new ActivityResultProjectionContract(
                "Handle", "$.handle", new("Int32"), true, ActivityValuePolicy.Default, ValueRepresentation.TransientResource)))), "transient");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C4,
            Subject<SafeLeaf>(Contract<SafeLeaf>(result: Result("Int32", policy: ActivityValuePolicy.Default with { IsPersistable = false }))), "transient");
    }

    [Fact]
    public void C4_refuses_a_pinned_stream_result_when_the_type_is_checked_at_run_time()
    {
        // A placement that cannot see the CLR type reads the stream from the pinned result alias alone.
        AssertRefused("publication", BackgroundEligibilityCriterion.C4,
            Subject<SafeLeaf>(Contract<SafeLeaf>(result: Result(typeof(Stream).FullName!))) with { ActivityType = null, TypeCheckedAtRunTime = true },
            "transient");
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C5_refuses_hidden_results(string entryPoint)
    {
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C5,
            Subject<SafeLeaf>(Contract<SafeLeaf>(result: Result("Int32", policy: ActivityValuePolicy.Default with { IsSensitive = true }))), "sensitive");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C5,
            Subject<SafeLeaf>(Contract<SafeLeaf>(result: Result("Int32", policy: ActivityValuePolicy.Default with { RequiresEncryption = true }))), "encryption");
    }

    [Fact]
    public void A_type_checked_at_run_time_defers_only_the_criteria_that_need_the_CLR_type()
    {
        var unknown = Subject<SafeLeaf>() with { ActivityType = null, TypeCheckedAtRunTime = true };

        Assert.Null(BackgroundEligibility.CheckPublication(unknown));
        AssertRefused("publication", BackgroundEligibilityCriterion.C1, unknown with { HasChildren = true }, "children");
        AssertRefused("publication", BackgroundEligibilityCriterion.C3, unknown with { Contract = Contract<SafeLeaf>(result: Result("Any")) }, "untyped");
        AssertRefused("publication", BackgroundEligibilityCriterion.C10, unknown with { Contract = Contract<SafeLeaf>(inputs: [Input("content", "Stream")]) }, "materialized");
        Assert.Contains("does not declare", BackgroundEligibility.CheckPublication(unknown with { Contract = Contract<SafeLeaf>(supportsBackgroundExecution: false) })!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_run_time_check_never_defers_the_CLR_type_checks()
    {
        var unknown = Subject<SafeLeaf>() with { ActivityType = null, TypeCheckedAtRunTime = true };

        AssertRefused("run time", BackgroundEligibilityCriterion.C1, unknown, "not a registered CLR activity");
    }

    [Fact]
    public void C6_is_a_run_time_check_after_the_body()
    {
        var subject = Subject<SafeLeaf>() with { StagedCoordinatorEffects = true };

        Assert.Null(BackgroundEligibility.CheckPublication(subject));
        AssertRefused("run time", BackgroundEligibilityCriterion.C6, subject, "coordinator");
        Assert.Null(BackgroundEligibility.CheckRunTime(subject with { StagedCoordinatorEffects = false }));
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C9_refuses_an_activity_that_reads_variables_by_name(string entryPoint) =>
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C9, Subject<VariableReaderLeaf>(),
            "variables reach a background step only through its inputs and output captures");

    // Spec 1231 T032 (FR-030, SC-005): each new C9 signal is refused with exactly VF-BG-001 (C9), at publication and in the
    // run-time re-check (admission and the worker both call CheckRunTime): (a) the free-name marker, (b) a writable
    // VariableReference<T> input, (c) a ReadOnlyVariableReference<T> input (Q2). An engine script activity counts as (a).
    public static TheoryData<string, string> VariableSignals => new()
    {
        { "publication", "marker" }, { "run time", "marker" },
        { "publication", "writable" }, { "run time", "writable" },
        { "publication", "read-only" }, { "run time", "read-only" },
        { "publication", "engine" }, { "run time", "engine" }
    };

    [Theory]
    [MemberData(nameof(VariableSignals))]
    public void C9_refuses_each_step_variable_signal(string entryPoint, string signal)
    {
        var (subject, reason) = VariableSignalSubject(signal);

        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C9, subject, reason);
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C9, subject, "variables reach a background step only through its inputs and output captures");
        Assert.True(subject.TouchesVariables);
        Assert.True(BackgroundEligibility.AccessesVariablesByName(subject.ActivityType!));
    }

    // T033 (FR-031): a template placement in a shell without the runtime type defers the CLR-type checks, yet the signals
    // the pinned contract shows are still refused there; a type-only signal waits for admission and the worker.
    [Theory]
    [InlineData("marker")]
    [InlineData("writable")]
    [InlineData("read-only")]
    public void A_placement_without_the_runtime_type_refuses_the_contract_visible_signals(string signal)
    {
        var (subject, reason) = VariableSignalSubject(signal);
        var deferred = subject with { ActivityType = null, TypeCheckedAtRunTime = true };

        AssertRefused("publication", BackgroundEligibilityCriterion.C9, deferred, reason);
    }

    [Fact]
    public void A_placement_without_the_runtime_type_leaves_a_type_only_signal_to_the_worker()
    {
        // IRuntimeScopedVariableReader is a CLR fact the contract does not carry.
        var deferred = Subject<VariableReaderLeaf>() with { ActivityType = null, TypeCheckedAtRunTime = true };

        Assert.False(deferred.TouchesVariables);
        Assert.Null(BackgroundEligibility.CheckPublication(deferred));
        AssertRefused("run time", BackgroundEligibilityCriterion.C9, deferred with { ActivityType = typeof(VariableReaderLeaf) }, "reads variables by name");
    }

    [Fact]
    public void A_type_signal_the_contract_does_not_show_is_refused_from_the_type()
    {
        // A contract built before the reference facts existed: the CLR type still carries the reference input.
        AssertRefused("run time", BackgroundEligibilityCriterion.C9, Subject<WritableReferenceLeaf>(), "input 'Balance' is a VariableReference");
    }

    private static (BackgroundEligibilitySubject Subject, string Reason) VariableSignalSubject(string signal) => signal switch
    {
        "marker" => (Subject<MarkedLeaf>(Contract<MarkedLeaf>(accessesUndeclaredVariables: true)), "[AccessesUndeclaredVariables]"),
        "engine" => (Subject<EngineScriptLeaf>(Contract<EngineScriptLeaf>(accessesUndeclaredVariables: true)), "an engine script activity"),
        "writable" => (Subject<WritableReferenceLeaf>(Contract<WritableReferenceLeaf>(inputs: [ReferenceInput("Balance", ActivityVariableAccess.ReadWrite)])),
            "input 'Balance' is a VariableReference"),
        _ => (Subject<ReadOnlyReferenceLeaf>(Contract<ReadOnlyReferenceLeaf>(inputs: [ReferenceInput("Currency", ActivityVariableAccess.Read)])),
            "input 'Currency' is a ReadOnlyVariableReference")
    };

    private static ActivityInputContract ReferenceInput(string key, ActivityVariableAccess access) =>
        new(key, key, new ValueTypeDescriptor(VariableReferenceTypes.Alias), isRequired: true, isNullable: false, hasDefault: false,
            defaultValue: null, ActivityValuePolicy.Default)
        {
            VariableReference = new ActivityInputVariableReference(access, new ValueTypeDescriptor("Decimal"))
        };

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C10_refuses_inputs_that_cannot_travel_to_a_worker(string entryPoint)
    {
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C10,
            Subject<SafeLeaf>(Contract<SafeLeaf>(inputs: [Input("payload", "String", ActivityValuePolicy.Default with { IsPersistable = false })])),
            "no durable representation");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C10,
            Subject<SafeLeaf>(Contract<SafeLeaf>(inputs: [Input("content", "Stream")])), "cannot be materialized");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C10,
            Subject<SafeLeaf>(Contract<SafeLeaf>(inputs: [Input("content", "System.IO.Stream")])), "cannot be materialized");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C10,
            Subject<SafeLeaf>(Contract<SafeLeaf>(inputs: [Input("password", "String", ActivityValuePolicy.Default with { IsSensitive = true })])),
            "lose its protection");
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C10_admits_secret_references_unbound_inputs_and_Any_inputs(string entryPoint)
    {
        var secretInput = Input("token", "String", ActivityValuePolicy.Default with { IsSensitive = true, RequiresEncryption = true }, hasDefault: false);
        var secretRead = new RuntimeInputBinding("token", new ValueTypeDescriptor("String"),
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true),
            RuntimeInputBindingSource.SecretRead, secret: new RuntimeSecretReference("payments.api-key"));
        Assert.Null(Check(entryPoint, Subject<SafeLeaf>(Contract<SafeLeaf>(inputs: [secretInput])) with
        {
            InputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal) { ["token"] = secretRead }
        }));

        Assert.Null(Check(entryPoint, Subject<SafeLeaf>(Contract<SafeLeaf>(inputs: [Input("content", "Stream", hasDefault: false)]))));
        // The shape the compiler pins for an unbound optional input: an absent literal, so nothing travels.
        var sensitive = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: false);
        var absent = new RuntimeInputBinding("password", new ValueTypeDescriptor("String"), sensitive, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Absent(new ValueTypeDescriptor("String"), sensitive));
        Assert.Null(Check(entryPoint, Subject<SafeLeaf>(Contract<SafeLeaf>(inputs: [Input("password", "String", ActivityValuePolicy.Default with { IsSensitive = true }, hasDefault: false)])) with
        {
            InputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal) { ["password"] = absent }
        }));
        Assert.Null(Check(entryPoint, Subject<SafeLeaf>(Contract<SafeLeaf>(inputs: [Input("document", "Any")]))));
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void An_undeclared_activity_is_refused_after_every_engine_checked_criterion(string entryPoint)
    {
        var refusal = Check(entryPoint, Subject<UnmarkedLeaf>(Contract<UnmarkedLeaf>(supportsBackgroundExecution: false)));

        Assert.NotNull(refusal);
        Assert.Null(refusal.Criterion);
        Assert.Equal("VF-BG-001: Background execution is not eligible for activity node 'node-1': the activity type does not declare BackgroundExecutionSafe.", refusal.Message);
        // A type that also fails an engine-checked criterion is refused naming that criterion.
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C2, Subject<UnmarkedStatefulLeaf>(Contract<UnmarkedStatefulLeaf>(supportsBackgroundExecution: false)), "stateful");
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void Several_failing_criteria_report_the_first(string entryPoint)
    {
        var subject = Subject<StatefulVariableReader>(Contract<StatefulVariableReader>(
            inputs: [Input("content", "Stream")],
            result: Result("Int32", policy: ActivityValuePolicy.Default with { IsSensitive = true })));

        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C2, subject, "stateful");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C5, subject with { ActivityType = typeof(VariableReaderLeaf) }, "sensitive");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C9, subject with
        {
            ActivityType = typeof(VariableReaderLeaf),
            Contract = Contract<VariableReaderLeaf>(inputs: [Input("content", "Stream")])
        }, "variables");
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void Placement_preconditions_come_first(string entryPoint)
    {
        var undefined = Check(entryPoint, Subject<StatefulLeaf>() with { Placement = (ActivityExecutionPlacement)42 });
        Assert.Contains("execution placement '42' is not defined", undefined!.Message, StringComparison.Ordinal);
        Assert.Null(undefined.Criterion);

        var inline = Check(entryPoint, Subject<SafeLeaf>() with { Placement = ActivityExecutionPlacement.Inline });
        Assert.Contains("not placed in the background", inline!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_run_time_subject_reads_the_pinned_node()
    {
        var contract = Contract<SafeLeaf>();
        var node = new ExecutableNode(
            "node-1", "node-1", "SafeLeaf", "1.0.0", WellKnownRuntimeActivityConsumers.ClrActivity, contract.DescriptorPayload,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            ActivityExecutionPlacement.Background, activityContract: contract);

        var subject = BackgroundEligibilitySubject.ForPinnedNode(node, typeof(VariableReaderLeaf));

        Assert.Equal(ActivityExecutionPlacement.Background, subject.Placement);
        Assert.Same(contract, subject.Contract);
        AssertRefused("run time", BackgroundEligibilityCriterion.C9, subject, "variables");
    }

    [Fact]
    public void The_refusal_renders_the_code_and_the_criterion()
    {
        var refusal = new BackgroundEligibilityRefusal(BackgroundEligibilityCriterion.C9, "n1", "the activity reads variables by name");

        Assert.Equal("VF-BG-001 (C9): Background execution is not eligible for activity node 'n1': the activity reads variables by name.", refusal.Message);
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void C9_refuses_identity_only_state_access_independently_of_variable_markers(string entryPoint)
    {
        var pinned = Subject<SafeLeaf>(Contract<SafeLeaf>(accessesWorkflowState: true));
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C9, pinned, "workflow outputs or identity");
        if (entryPoint == "publication")
            AssertRefused(entryPoint, BackgroundEligibilityCriterion.C9,
                pinned with { ActivityType = null, TypeCheckedAtRunTime = true }, "workflow outputs or identity");
        AssertRefused(entryPoint, BackgroundEligibilityCriterion.C9, Subject<StateIdentityLeaf>(), "workflow outputs or identity");
    }

    [AccessesWorkflowState, BackgroundExecutionSafe]
    private sealed class StateIdentityLeaf : Activity<int>
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(Elsa.Activities.Runtime.Core.Models.ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    private static BackgroundEligibilityRefusal? Check(string entryPoint, BackgroundEligibilitySubject subject) => entryPoint switch
    {
        "publication" => BackgroundEligibility.CheckPublication(subject),
        _ => BackgroundEligibility.CheckRunTime(subject)
    };

    private static void AssertRefused(string entryPoint, BackgroundEligibilityCriterion criterion, BackgroundEligibilitySubject subject, string reason)
    {
        var refusal = Check(entryPoint, subject);
        Assert.NotNull(refusal);
        Assert.Equal(criterion, refusal.Criterion);
        Assert.StartsWith($"VF-BG-001 ({criterion}): Background execution is not eligible for activity node 'node-1': ", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(reason, refusal.Message, StringComparison.Ordinal);
    }

    private static BackgroundEligibilitySubject Subject<TActivity>(ActivityContract? contract = null) =>
        new("node-1") { ActivityType = typeof(TActivity), Contract = contract ?? Contract<TActivity>() };

    private static ActivityContract Contract<TActivity>(
        IEnumerable<ActivityInputContract>? inputs = null,
        ActivityResultContract? result = null,
        bool supportsBackgroundExecution = true,
        bool accessesUndeclaredVariables = false,
        bool accessesWorkflowState = false)
    {
        var alias = typeof(TActivity).Name;
        return new ActivityContract(
            alias, "1.0.0", "clr", JsonSerializer.SerializeToElement(new { typeAlias = alias }),
            inputs ?? [], result ?? Result("Int32"), ["Done"],
            new ActivityActivationRequirement("clr", alias), SideEffectProfile.External, supportsBackgroundExecution, accessesUndeclaredVariables, accessesWorkflowState);
    }

    private static ActivityResultContract Result(string alias, ActivityValuePolicy? policy = null, ActivityResultProjectionContract? projection = null) =>
        new(new ValueTypeDescriptor(alias), true, policy ?? ActivityValuePolicy.Default, projection is null ? [] : [projection]);

    private static ActivityInputContract Input(string key, string alias, ActivityValuePolicy? policy = null, bool hasDefault = true) =>
        new(key, key, new ValueTypeDescriptor(alias), isRequired: false, isNullable: true, hasDefault: hasDefault,
            defaultValue: hasDefault ? JsonSerializer.SerializeToElement<object?>(null) : null, policy ?? ActivityValuePolicy.Default);

    [BackgroundExecutionSafe]
    private sealed class SafeLeaf : Activity<int>
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    private sealed class UnmarkedLeaf : Activity<int>
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    private sealed class ObjectLeaf : Activity<object>
    {
        protected override ValueTask<ActivityTransition<object>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete<object>(1));
    }

    [BackgroundExecutionSafe]
    private sealed class StreamLeaf : Activity<Stream>
    {
        protected override ValueTask<ActivityTransition<Stream>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete<Stream>(Stream.Null));
    }

    [BackgroundExecutionSafe]
    [AccessesUndeclaredVariables]
    private sealed class MarkedLeaf : Activity<int>
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    private sealed class EngineScriptLeaf : Activity<int>, IStepVariableSessionConsumer
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    private sealed class WritableReferenceLeaf : Activity<int>
    {
        [ActivityInput]
        public VariableReference<decimal> Balance { get; set; } = default!;

        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    private sealed class ReadOnlyReferenceLeaf : Activity<int>
    {
        [ActivityInput]
        public ReadOnlyVariableReference<string> Currency { get; set; } = default!;

        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    private sealed class VariableReaderLeaf : Activity<int>, IRuntimeScopedVariableReader
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    private sealed class OutcomeHandlerLeaf : Activity<int>, IRuntimeActivityChildOutcomeHandler
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));

        public ValueTask<RuntimeStructuralContinuation> OnChildOutcomeAsync(ActivityChildOutcomeContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    private abstract class StatefulLeafCore : Activity<int>, IStatefulActivity
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));

        public ValueTask<ActivityTransition> ResumeAsync(ActivityResumeRequest request) => throw new NotSupportedException();
    }

    [BackgroundExecutionSafe]
    private sealed class StatefulLeaf : StatefulLeafCore;

    private sealed class UnmarkedStatefulLeaf : StatefulLeafCore;

    [BackgroundExecutionSafe]
    private sealed class StatefulVariableReader : StatefulLeafCore, IRuntimeScopedVariableReader;
}
