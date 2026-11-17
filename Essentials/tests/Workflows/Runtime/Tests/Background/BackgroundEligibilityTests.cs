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
        bool supportsBackgroundExecution = true)
    {
        var alias = typeof(TActivity).Name;
        return new ActivityContract(
            alias, "1.0.0", "clr", JsonSerializer.SerializeToElement(new { typeAlias = alias }),
            inputs ?? [], result ?? Result("Int32"), ["Done"],
            new ActivityActivationRequirement("clr", alias), SideEffectProfile.External, supportsBackgroundExecution);
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
