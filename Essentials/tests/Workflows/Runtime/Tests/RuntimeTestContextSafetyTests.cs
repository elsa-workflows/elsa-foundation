using Elsa.Expressions.Core.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeTestContextSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_closed_owner_cannot_expose_same_name_workflow_request_or_variable(bool variable)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        var type = TypeOf<int>(harness);
        var binding = variable ? new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead, variable: new("amount", VariableReference.WorkflowScopeId)) :
            new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest,
                workflowRequest: new("amount"));
        var root = Container(Leaf("selected", binding), closed: true);
        var template = WorkflowExecutionHarness.NewExecutable(root);
        var executable = new WorkflowExecutable(template.Identity, root, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            template.CreatedAt, new Dictionary<string, string>(),
            variable ? null : new(WorkflowExecutableInputContract.CurrentVersion, [new("amount", new(type.Alias), true)]),
            null, null, null, IncidentStrategyBuiltIns.FaultReference, null,
            variable ? [new("amount", "amount", type, ValueProtectionPolicy.InstanceInline)] : []);
        var supplied = new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(7) };
        var result = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(new(executable,
            "selected", "inspection", new("setup", "fingerprint", [], []), variable ? new Dictionary<string, JsonElement>() : supplied,
            variable ? supplied : new Dictionary<string, JsonElement>()));
        Assert.False(result.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("inspection"));
    }

    [Fact]
    public async Task Custom_enum_converter_is_never_constructed_or_read_by_inspection()
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        var type = TypeOf<UnsafeEnum>(harness);
        UnsafeConverter.Constructions = UnsafeConverter.Reads = 0;
        var node = Leaf("selected", new("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(0), ValueProtectionPolicy.InstanceInline)));
        var result = await Inspect(harness, WorkflowExecutionHarness.NewExecutable(node));
        Assert.False(result.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
        Assert.Equal(0, UnsafeConverter.Constructions);
        Assert.Equal(0, UnsafeConverter.Reads);
    }

    [Theory]
    [InlineData("required-null")]
    [InlineData("missing-projection")]
    [InlineData("protected-projection")]
    public async Task Fixtures_cannot_represent_impossible_or_unprotected_atomic_completions(string profile)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        var type = profile == "required-null" ? TypeOf<string>(harness) : TypeOf<object>(harness);
        var projectionType = TypeOf<string>(harness);
        var projections = profile == "required-null" ? Array.Empty<ActivityResultProjectionContract>() :
            [new ActivityResultProjectionContract("secret", "secret", projectionType, true,
                profile == "protected-projection" ? new(true, true, true) : ActivityValuePolicy.Default)];
        var contract = new ActivityContract("producer", "1", "producer", JsonSerializer.SerializeToElement(new { }), [],
            new(type, true, ActivityValuePolicy.Default, projections), ["Done"], new("producer", "probe"));
        var before = new ExecutableNode("before", "before", "producer", "1", "producer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract);
        var root = Container(Leaf("selected"), before: before);
        var value = profile == "required-null" ? ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline) :
            ValueEnvelope.Inline(type, profile == "protected-projection" ? JsonSerializer.SerializeToElement(new { secret = "do-not-expose" }) :
                JsonSerializer.SerializeToElement(new { }), ValueProtectionPolicy.InstanceInline);
        var result = await Inspect(harness, WorkflowExecutionHarness.NewExecutable(root), [new("before", "scope", value)]);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Context);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, finding.Kind);
        Assert.DoesNotContain("do-not-expose", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RuntimeInputBindingSource.ActivityResultMember)]
    [InlineData(RuntimeInputBindingSource.VariableMember)]
    [InlineData(RuntimeInputBindingSource.WorkflowRequestMember)]
    public async Task Member_setup_is_explicitly_unavailable_until_pure_selection_preflight_is_qualified(RuntimeInputBindingSource source)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        var type = TypeOf<int>(harness);
        var binding = new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, source,
            activityResult: source == RuntimeInputBindingSource.ActivityResultMember ? new("before", "$result", null) : null,
            variable: source == RuntimeInputBindingSource.VariableMember ? new("value", VariableReference.WorkflowScopeId) : null,
            workflowRequest: source == RuntimeInputBindingSource.WorkflowRequestMember ? new("value") : null,
            selection: new(RuntimeReadSelection.CurrentSchemaVersion, ["amount"], RuntimeReadSelectionMode.DeferredFit));
        var result = await Inspect(harness, WorkflowExecutionHarness.NewExecutable(Leaf("selected", binding)));
        Assert.False(result.IsAvailable);
        Assert.Equal("runtime.test-context.member-selection-unavailable", Assert.Single(result.Findings).Code);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    public async Task Closed_input_nullability_and_protection_apply_to_supplied_and_default_values(bool nullable, bool useDefault, bool encrypted, bool available)
    {
        await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(services =>
            services.AddSingleton<IRuntimeTestScopeContractSource>(new ClosedInputs(nullable, useDefault, encrypted))).Build();
        var type = TypeOf<string>(harness);
        var selected = Leaf("selected", new("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new("amount")));
        var frame = new RuntimeTestFrameSeed("container", VariableFrameKind.Root, new Dictionary<string, ValueEnvelope>(), true,
            useDefault ? null : new Dictionary<string, ValueEnvelope> { ["amount"] = ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline) });
        var result = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(new(
            WorkflowExecutionHarness.NewExecutable(Container(selected, closed: true)), "selected", "inspection",
            new("setup", "fingerprint", [frame], []), new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>()));
        Assert.Equal(available, result.IsAvailable);
        if (available) Assert.Equal(ValuePresence.ExplicitNull, Assert.Single(result.Context!.Frames).Inputs["amount"].Presence);
        else Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
    }

    private sealed class ClosedInputs(bool nullable, bool useDefault, bool encrypted) : IRuntimeTestScopeContractSource
    {
        public ValueTask<RuntimeTestScopeContract?> GetContractAsync(WorkflowExecutable executable, ExecutableNode scopeNode,
            VariableFrameKind kind, CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeTestScopeContract?>(
            kind != VariableFrameKind.Root ? null : new(scopeNode.ExecutableNodeId, kind, true, new Dictionary<string, RuntimeVariableDeclaration>(),
                new Dictionary<string, ActivityInputContract> { ["amount"] = new("amount", "amount", new(TypeAliasConvention.CanonicalAlias(typeof(string))),
                    true, nullable, useDefault, useDefault ? JsonSerializer.SerializeToElement<object?>(null) : null,
                    encrypted ? new(true, true, true) : ActivityValuePolicy.Default) }, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_request_paths_are_unavailable_for_direct_and_expression_context(bool expression)
    {
        await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(s =>
            s.AddSingleton<IPortableExpressionEvaluator, QualifiedEvaluator>()).Build();
        var type = TypeOf<object>(harness);
        var binding = expression ? new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new("Test", "input", parameters: new Dictionary<string, ExpressionParameterBinding>
            { ["input"] = new WorkflowRequestExpressionParameterBinding("payload", "missing") })) :
            new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest,
                workflowRequest: new("payload", "missing"));
        var node = Leaf("selected", binding); var template = WorkflowExecutionHarness.NewExecutable(node);
        var executable = new WorkflowExecutable(template.Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), template.CreatedAt,
            new Dictionary<string, string>(), new(WorkflowExecutableInputContract.CurrentVersion, [new("payload", new(type.Alias), true)]),
            null, IncidentStrategyBuiltIns.FaultReference);
        var result = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(new(executable,
            "selected", "inspection", new("setup", "fingerprint", [], []),
            new Dictionary<string, JsonElement> { ["payload"] = JsonSerializer.SerializeToElement(new { }) }, new Dictionary<string, JsonElement>()));
        Assert.False(result.IsAvailable);
        Assert.Equal("runtime.test-context.legacy-path-unavailable", Assert.Single(result.Findings).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unqualified_direct_and_parameter_conversion_plans_are_unavailable(bool expression)
    {
        await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(s =>
            s.AddSingleton<IPortableExpressionEvaluator, QualifiedEvaluator>()).Build();
        var type = TypeOf<string>(harness);
        var plan = ValueConversionPlan.Identity(type, ValueRepresentation.TypedValue);
        var binding = expression ? new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new("Test", "input", parameters: new Dictionary<string, ExpressionParameterBinding>
                { ["input"] = new WorkflowRequestExpressionParameterBinding("payload") },
                parameterConversionPlans: new Dictionary<string, ValueConversionPlan> { ["input"] = plan })) :
            new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("supplied"), ValueProtectionPolicy.InstanceInline), conversionPlan: plan);
        var node = Leaf("selected", binding);
        var template = WorkflowExecutionHarness.NewExecutable(node);
        var executable = new WorkflowExecutable(template.Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), template.CreatedAt,
            new Dictionary<string, string>(), new(WorkflowExecutableInputContract.CurrentVersion, [new("payload", new(type.Alias), true)]),
            null, IncidentStrategyBuiltIns.FaultReference);
        var result = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(new(executable,
            "selected", "inspection", new("setup", "fingerprint", [], []),
            new Dictionary<string, JsonElement> { ["payload"] = JsonSerializer.SerializeToElement("supplied") }, new Dictionary<string, JsonElement>()));
        Assert.False(result.IsAvailable);
        Assert.Equal("runtime.test-context.conversion-unavailable", Assert.Single(result.Findings).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_unreferenced_fixtures_are_setup_refusals(bool duplicate)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        var type = TypeOf<string>(harness);
        var contract = new ActivityContract("producer", "1", "producer", JsonSerializer.SerializeToElement(new { }), [],
            new(type, false, ActivityValuePolicy.Default, []), ["Done"], new("producer", "probe"));
        var before = new ExecutableNode("before", "before", "producer", "1", "producer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract);
        var fixture = new RuntimeTestResultFixture("before", "scope", ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline));
        var result = await Inspect(harness, WorkflowExecutionHarness.NewExecutable(Container(Leaf("selected"), before: before)),
            duplicate ? [fixture, fixture] : [fixture]);
        Assert.Equal(!duplicate, result.IsAvailable);
        if (duplicate) Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
    }

    [Theory]
    [InlineData("JavaScript", false)]
    [InlineData("CustomScript", false)]
    [InlineData("JavaScript", true)]
    [InlineData("CustomScript", true)]
    public async Task Qualified_expression_aliases_require_the_same_complete_ambient_setup(string language, bool supplied)
    {
        await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(s =>
            s.AddSingleton<IPortableExpressionEvaluator, QualifiedEvaluator>()).Build();
        var type = TypeOf<int>(harness);
        var node = Leaf("selected", new("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new(language, "amount")));
        var executable = WorkflowExecutionHarness.NewExecutable(node, new RuntimeVariableDeclaration("amount", "amount", type,
            ValueProtectionPolicy.InstanceInline));
        var result = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(new(executable,
            "selected", "inspection", new("setup", "fingerprint", [], []), new Dictionary<string, JsonElement>(),
            supplied ? new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(7) } : new Dictionary<string, JsonElement>()));
        Assert.Equal(supplied, result.IsAvailable);
        if (!supplied) Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
    }

    private sealed class QualifiedEvaluator : IPortableExpressionEvaluator, IPortableExpressionCapabilities
    {
        public bool CanEvaluate(ExpressionDefinition definition) => true;
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => throw new InvalidOperationException("Inspection must never evaluate.");
    }

    private static ValueTypeDescriptor TypeOf<T>(WorkflowExecutionHarness harness)
    {
        var alias = TypeAliasConvention.CanonicalAlias(typeof(T));
        harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(T), alias);
        return new(alias);
    }

    private static ValueTask<RuntimeTestContextPreparation> Inspect(WorkflowExecutionHarness harness, WorkflowExecutable executable,
        IReadOnlyList<RuntimeTestResultFixture>? results = null) => harness.Services.GetRequiredService<IRuntimeTestContextInspector>()
        .InspectAsync(new(executable, "selected", "inspection", new("setup", "fingerprint", [], results ?? []),
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>()));

    private static ExecutableNode Leaf(string id, RuntimeInputBinding? input = null) => new(id, id, "test.leaf", "1", "test.leaf",
        JsonSerializer.SerializeToElement(new { }), input is null ? new Dictionary<string, RuntimeInputBinding>() :
            new Dictionary<string, RuntimeInputBinding> { ["value"] = input }, new Dictionary<string, string>());

    private static ExecutableNode Container(ExecutableNode selected, bool closed = false, ExecutableNode? before = null) => new(
        "container", "container", "test.container", "1", "test.container", JsonSerializer.SerializeToElement(new { }),
        new Dictionary<string, RuntimeInputBinding>(), closed ? new Dictionary<string, string>
        { [RuntimeMetadataKeys.ActivityExecutionScope] = RuntimeMetadataKeys.ActivityExecutionScopeClosed } : new Dictionary<string, string>(),
        childSlots: [new("Activities", before is null ? [selected] : [before, selected])]);

    [JsonConverter(typeof(UnsafeConverter))]
    private enum UnsafeEnum { Value }
    private sealed class UnsafeConverter : JsonConverter<UnsafeEnum>
    {
        internal static int Constructions, Reads;
        public UnsafeConverter() => Interlocked.Increment(ref Constructions);
        public override UnsafeEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        { Interlocked.Increment(ref Reads); return UnsafeEnum.Value; }
        public override void Write(Utf8JsonWriter writer, UnsafeEnum value, JsonSerializerOptions options) => writer.WriteNumberValue(0);
    }
}
