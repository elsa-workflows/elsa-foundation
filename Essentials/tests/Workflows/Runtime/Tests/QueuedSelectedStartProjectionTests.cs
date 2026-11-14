using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class QueuedSelectedStartTests
{
    private static readonly ValueTypeDescriptor ProjectionJson = new(TypeAliasConvention.CanonicalAlias(typeof(JsonElement)));

    [Theory]
    [InlineData(PersistenceMode.Durable, false, "literal-dot")]
    [InlineData(PersistenceMode.Balanced, false, "literal-dot")]
    [InlineData(PersistenceMode.Durable, true, "literal-dot")]
    [InlineData(PersistenceMode.Balanced, true, "literal-dot")]
    [InlineData(PersistenceMode.Durable, false, "nested")]
    [InlineData(PersistenceMode.Balanced, true, "nested")]
    [InlineData(PersistenceMode.Durable, false, "trim")]
    [InlineData(PersistenceMode.Balanced, true, "trim")]
    [InlineData(PersistenceMode.Durable, false, "dollar")]
    [InlineData(PersistenceMode.Balanced, true, "dollar")]
    [InlineData(PersistenceMode.Durable, false, "explicit-null")]
    [InlineData(PersistenceMode.Balanced, true, "explicit-null")]
    [InlineData(PersistenceMode.Durable, false, "optional-unused")]
    [InlineData(PersistenceMode.Balanced, true, "optional-unused")]
    [InlineData(PersistenceMode.Durable, false, "sensitive-unused")]
    [InlineData(PersistenceMode.Balanced, true, "sensitive-unused")]
    public async Task Native_prior_result_projections_use_completion_selection_and_atomic_policy(PersistenceMode mode, bool expression, string shape)
    {
        var owner = ProjectionOwner(shape);
        await using var harness = Build(owner, mode, ResultServices);
        await Setup(harness, owner, ResultConsumer(expression, "shown.key"));
        var original = JsonSerializer.Serialize(owner.Executable);
        await RunLexical(harness, owner);
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        var value = activity.InputSnapshot!.Values["value"];
        Assert.Equal(shape == "explicit-null" ? ValuePresence.ExplicitNull : ValuePresence.Present, value.Presence);
        if (shape != "explicit-null") Assert.Equal(shape is "literal-dot" ? "literal" : shape is "nested" or "trim" ? "nested" : "selected", value.InlineValue!.Value.GetString());
        Assert.Equal(shape == "sensitive-unused", value.Policy.IsSensitive);
        if (shape == "sensitive-unused")
        {
            Assert.Equal("full", value.Policy.RedactionMode);
            Assert.Equal("test-only", value.Policy.RetentionPolicy);
        }
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        var whole = Assert.Single(state.TestContext!.Results).Result;
        Assert.Equal(shape == "dollar" ? FixtureString : ProjectionJson, whole.Type);
        Assert.Null(activity.SchedulingActivityExecutionId);
        Assert.Null(activity.ParentActivityExecutionId);
        Assert.All(await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId),
            row => Assert.True(row.SourceActivityExecutionId is null || row.SourceActivityExecutionId == activity.InvocationId));
        Assert.Equal(original, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(owner.Executable!.Identity.ArtifactId)));
    }

    [Theory]
    [InlineData(false, "required-missing")]
    [InlineData(true, "required-missing")]
    [InlineData(false, "required-null")]
    [InlineData(true, "required-null")]
    [InlineData(false, "wrong-type")]
    [InlineData(true, "wrong-type")]
    [InlineData(false, "wrong-policy")]
    [InlineData(true, "encryption")]
    [InlineData(false, "transient")]
    [InlineData(true, "resource")]
    [InlineData(false, "case")]
    [InlineData(true, "case")]
    [InlineData(false, "optional-missing-read")]
    [InlineData(true, "optional-missing-read")]
    [InlineData(false, "unknown-key")]
    [InlineData(true, "unknown-key")]
    public async Task Native_projection_setup_validates_unread_declarations_before_any_execution(bool expression, string defect)
    {
        var owner = ProjectionOwner(defect);
        await using var harness = Build(owner, PersistenceMode.Balanced, ResultServices);
        await Setup(harness, owner, ResultConsumer(expression, defect == "unknown-key" ? "unknown" : "shown.key"));
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        var prepared = await harness.Services.GetRequiredService<IRuntimeTestContextPreparer>().PrepareAsync(owner.Entry!, owner.Executable!, harness.ExecutionId,
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>());
        Assert.False(prepared.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(prepared.Findings).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Projection_declarations_do_not_change_whole_fixture_reads(bool expression)
    {
        var owner = ProjectionOwner("sensitive-unused"); owner.QualifyTarget = node => node.IntrinsicKind == WorkflowIntrinsicKind.Set;
        owner.WorkflowVariables = [new("whole", "whole", ProjectionJson, FixtureSensitive,
            new("whole", ProjectionJson, FixtureSensitive, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(ProjectionJson, JsonSerializer.SerializeToElement(new { }), FixtureSensitive)))];
        await using var harness = Build(owner, PersistenceMode.Balanced, ResultServices);
        var binding = new RuntimeInputBinding(WorkflowIntrinsicInputKeys.Value, ProjectionJson, FixtureSensitive,
            expression ? RuntimeInputBindingSource.Expression : RuntimeInputBindingSource.ActivityResult,
            activityResult: expression ? null : new("before", "$result", "fixture-scope", true),
            expression: expression ? new("fixture-expression", "p", parameters: new Dictionary<string, ExpressionParameterBinding>
            { ["p"] = new ActivityResultExpressionParameterBinding("before", "$result", true) }) : null);
        var selected = new ExecutableNode("selected", "selected", "elsa.intrinsic.set", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding }, new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new("whole", VariableReference.WorkflowScopeId));
        await Setup(harness, owner, selected); await RunLexical(harness, owner);
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        Assert.Equal("selected", state.RootVariableFrame!.Values["whole"].InlineValue!.Value.GetProperty("value").GetString());
        Assert.True(state.RootVariableFrame.Values["whole"].Policy.IsSensitive);
    }

    [Theory]
    [InlineData(PersistenceMode.Durable, false)]
    [InlineData(PersistenceMode.Balanced, false)]
    [InlineData(PersistenceMode.Durable, true)]
    [InlineData(PersistenceMode.Balanced, true)]
    public async Task Native_projection_survives_fresh_activation_without_repreparing_whole_fixture(PersistenceMode mode, bool expression)
    {
        var owner = ProjectionOwner("literal-dot");
        await using var harness = Build(owner, mode, services =>
        {
            ResultServices(services);
            new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        });
        await Setup(harness, owner, ResultConsumer(expression, "shown.key"));
        await Checkpoints(harness).HandleAsync(await StageCheckpoint(harness, owner));
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        var restored = JsonSerializer.Deserialize<WorkflowExecutionState>(JsonSerializer.Serialize(state))!;
        await using (var fresh = harness.Services.CreateAsyncScope())
            await fresh.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(restored);
        owner.Seed = _ => throw new InvalidOperationException("Projection reads must use the admitted whole fixture.");
        await harness.SweepUntilQuietAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!.Status);
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal("literal", activity.InputSnapshot!.Values["value"].InlineValue!.Value.GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unread_projection_types_never_construct_unsafe_CLR_values(bool registered)
    {
        ProjectionUnsafeValue.Constructions = 0;
        var owner = ProjectionOwner("unsafe-type");
        await using var harness = Build(owner, PersistenceMode.Balanced, ResultServices);
        if (registered) harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(ProjectionUnsafeValue),
            TypeAliasConvention.CanonicalAlias(typeof(ProjectionUnsafeValue)));
        await Setup(harness, owner, ResultConsumer(false, "shown.key"));
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Equal(0, ProjectionUnsafeValue.Constructions);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
    }

    [Fact]
    public async Task Explicit_null_projection_refuses_a_non_nullable_consumer_before_activation()
    {
        var owner = ProjectionOwner("explicit-null");
        owner.QualifyTarget = node => node.ActivityType == typeof(ProjectionNonNullableActivity).FullName;
        await using var harness = Build(owner, PersistenceMode.Balanced, ResultServices);
        var binding = FixtureBinding("value", false, "shown.key");
        await Setup(harness, owner, new("selected", "selected", typeof(ProjectionNonNullableActivity).FullName!, "1", "test.consumer",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding> { ["value"] = binding }, new Dictionary<string, string>()));
        ProjectionNonNullableActivity.Constructions = 0;
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Equal(0, ProjectionNonNullableActivity.Constructions);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
    }

    public sealed class ProjectionUnsafeValue
    {
        public static int Constructions;
        public ProjectionUnsafeValue() => Constructions++;
        public string? Text { get; set; }
    }
    public sealed class ProjectionNonNullableActivity : Activity<LexicalStringResult>
    {
        public static int Constructions;
        public ProjectionNonNullableActivity() => Constructions++;
        [ActivityInput(Key = "value")] public string Value { get; set; } = "";
        protected override ValueTask<ActivityTransition<LexicalStringResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new LexicalStringResult(Value)));
    }

    private static Owner ProjectionOwner(string shape)
    {
        var type = shape == "dollar" ? FixtureString : ProjectionJson;
        var path = shape == "dollar" ? "$" : shape is "literal-dot" or "nested" ? "a.b" : shape == "trim" ? " a . b " :
            shape == "case" ? "Value" : shape == "optional-missing-read" ? "missing" : "value";
        var payload = shape == "dollar" ? JsonSerializer.SerializeToElement("selected") : shape == "literal-dot" ?
            JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["a.b"] = "literal", ["a"] = new { b = "nested" } }) :
            shape is "nested" or "trim" ? JsonSerializer.SerializeToElement(new { a = new { b = "nested" } }) :
            shape == "explicit-null" ? JsonSerializer.SerializeToElement(new { value = (string?)null }) :
            shape == "unsafe-type" ? JsonSerializer.SerializeToElement(new { value = "selected", unread = new { text = "unsafe" } }) :
            shape == "required-null" ? JsonSerializer.SerializeToElement(new { value = "selected", unread = (string?)null }) :
            shape == "wrong-type" ? JsonSerializer.SerializeToElement(new { value = "selected", unread = 42 }) :
            JsonSerializer.SerializeToElement(new { value = "selected" });
        var projections = new List<ActivityResultProjectionContract>
        { new("shown.key", path, FixtureString, shape is not ("explicit-null" or "optional-missing-read"), ActivityValuePolicy.Default) };
        if (shape is "optional-unused" or "sensitive-unused" or "required-missing" or "required-null" or "wrong-type" or "wrong-policy" or "encryption" or "transient" or "resource" or "unsafe-type")
            projections.Add(new("unread", shape is "required-null" or "wrong-type" or "unsafe-type" ? "unread" : "missing",
                shape == "unsafe-type" ? new(TypeAliasConvention.CanonicalAlias(typeof(ProjectionUnsafeValue))) : FixtureString,
                shape is "required-missing" or "required-null" or "wrong-type", shape is "sensitive-unused" or "wrong-policy" ?
                    new(true, true, false, RedactionMode: "full", RetentionPolicy: "test-only") : shape == "encryption" ?
                    new(true, true, true) : shape == "transient" ? new(false, false, false) :
                    ActivityValuePolicy.Default, shape == "resource" ? ValueRepresentation.TransientResource : null));
        var contract = new ActivityContract("projection.producer", "1", "projection.producer", JsonSerializer.SerializeToElement(new { }), [],
            new(type, true, ActivityValuePolicy.Default, projections), ["Done"], new("projection.producer", "probe"));
        var producer = new ExecutableNode("before", "before", "projection.producer", "1", "projection.producer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract);
        var owner = ResultOwner("present");
        owner.WrapSelected = selected => new("container", "container", typeof(ProbeActivity).FullName!, "1", "test.container", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots: [new("children", [producer, selected, WorkflowExecutionHarness.NewProbeNode("after")])]);
        owner.Seed = entry => new(entry.ContextId, entry.ContextFingerprint, [],
            [new("before", "fixture-scope", ValueEnvelope.Inline(type, payload, shape == "sensitive-unused" ? FixtureSensitive : ValueProtectionPolicy.InstanceInline))]);
        return owner;
    }
}
