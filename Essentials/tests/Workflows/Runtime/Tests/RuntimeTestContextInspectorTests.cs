using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeTestContextInspectorTests
{
    [Theory]
    [InlineData("unknown-null", false)]
    [InlineData("wrong-json", false)]
    [InlineData("forbidden-null", false)]
    [InlineData("qualified-null", true)]
    [InlineData("binding-type-mismatch", false)]
    [InlineData("default", true)]
    [InlineData("default-binding-missing", false)]
    [InlineData("optional-absent", true)]
    [InlineData("optional-binding-missing", false)]
    [InlineData("required-missing", false)]
    [InlineData("unknown-optional", false)]
    public async Task Configured_member_types_nullability_and_defaults_are_qualified_before_any_owned_constructor_or_state(string profile, bool available)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        ConstructorProbe.Constructions = 0;
        var types = harness.Services.GetRequiredService<IWellKnownTypeRegistry>();
        var intType = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(int)));
        var stringType = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(string)));
        types.RegisterType(typeof(int), intType.Alias); types.RegisterType(typeof(string), stringType.Alias);
        var inputType = profile.StartsWith("unknown", StringComparison.Ordinal) ? new("unregistered-type") :
            profile is "forbidden-null" or "qualified-null" or "binding-type-mismatch" ? stringType : intType;
        var bindingType = profile == "binding-type-mismatch" ? intType : inputType;
        var payload = JsonSerializer.SerializeToElement(profile == "wrong-json" ? (object)"wrong" : 7);
        if (profile.EndsWith("null", StringComparison.Ordinal)) payload = JsonSerializer.SerializeToElement<object?>(null);
        var hasDefault = profile is "default" or "default-binding-missing";
        var input = new ActivityInputContract("Value", "Value", inputType, profile is not ("unknown-optional" or "optional-absent" or "optional-binding-missing"),
            profile is "qualified-null" or "unknown-null" or "optional-absent" or "optional-binding-missing", hasDefault, hasDefault ? payload : null, ActivityValuePolicy.Default);
        var contract = new ActivityContract(typeof(ConstructorProbe).FullName!, "1", "test.descriptor", JsonSerializer.SerializeToElement(new { }), [input],
            new(intType, true, ActivityValuePolicy.Default, []), ["Done"], new("test.descriptor", "probe"));
        var bindings = new Dictionary<string, RuntimeInputBinding>();
        if (profile is not ("default-binding-missing" or "required-missing" or "unknown-optional" or "optional-binding-missing"))
            bindings["Value"] = new("Value", bindingType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: profile == "optional-absent" ? ValueEnvelope.Absent(bindingType, ValueProtectionPolicy.InstanceInline) :
                    payload.ValueKind == JsonValueKind.Null ? ValueEnvelope.Null(bindingType, ValueProtectionPolicy.InstanceInline) :
                    ValueEnvelope.Inline(bindingType, payload, ValueProtectionPolicy.InstanceInline));
        var later = new ExecutableNode("later", "later", typeof(ConstructorProbe).FullName!, "1", "test.descriptor", JsonSerializer.SerializeToElement(new { }),
            bindings, new Dictionary<string, string>(), activityContract: contract);
        var root = new ExecutableNode("selected", "selected", "sequence", "1", "sequence", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new("Activities", [WorkflowExecutionHarness.NewProbeNode("earlier"), later])]);
        var executable = WorkflowExecutionHarness.NewExecutable(root);
        var result = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(new(executable, "selected", "inspection",
            new("setup", "fingerprint", [], []), new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>()));
        Assert.Equal(available, result.IsAvailable);
        if (!available) Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
        Assert.Equal(0, ConstructorProbe.Constructions);
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("inspection"));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("inspection"));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId));
    }

    [Fact]
    public async Task An_unrelated_capability_service_cannot_qualify_the_actual_selected_evaluator()
    {
        await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(s =>
        {
            s.AddSingleton<IPortableExpressionEvaluator, UnqualifiedEvaluator>();
            s.AddSingleton<IPortableExpressionCapabilities, UnrelatedCapabilities>();
        }).Build();
        UnqualifiedEvaluator.Calls = 0;
        var type = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(int)));
        harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(int), type.Alias);
        var selected = new ExecutableNode("selected", "selected", "elsa.intrinsic.return", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = new("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: new("Test", "42")) }, new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Return);
        var result = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(new(WorkflowExecutionHarness.NewExecutable(selected), "selected", "inspection",
            new("setup", "fingerprint", [], []), new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>()));
        Assert.False(result.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
        Assert.Equal(0, UnqualifiedEvaluator.Calls);
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("inspection"));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("inspection"));
    }

    [Fact]
    public async Task Inspection_validates_setup_without_a_trusted_entry_scope_or_execution()
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        var type = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(int)));
        harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(int), type.Alias);
        var selected = new ExecutableNode("selected", "selected", "elsa.intrinsic.return", "1", "intrinsic",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>
            { ["value"] = new("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(7), ValueProtectionPolicy.InstanceInline)) },
            new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Return);
        var executable = WorkflowExecutionHarness.NewExecutable(selected);
        var request = new RuntimeTestContextInspectionRequest(executable, selected.ExecutableNodeId,
            "inspection-only", new("setup-data", "setup-fingerprint", [], []),
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>());

        var inspector = harness.Services.GetRequiredService<IRuntimeTestContextInspector>();
        var valid = await inspector.InspectAsync(request);
        Assert.True(valid.IsAvailable, string.Join(";", valid.Findings));
        Assert.Equal(["selected"], valid.Context!.OwnedNodeIds);
        Assert.Equal("inspection-only", valid.RootVariableFrame!.ActivationId);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("inspection-only"));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("inspection-only"));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId));

        var missing = await inspector.InspectAsync(request with { ExecutableNodeId = "missing" });
        Assert.False(missing.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(missing.Findings).Kind);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.InspectAsync(request, cancelled.Token).AsTask());
    }

    [Fact]
    public async Task Duplicate_inspector_replacements_are_rejected_at_startup()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddScoped<IRuntimeTestContextInspector, UnavailableInspector>();
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var task in provider.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
        });
        Assert.Contains("one test context inspector", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("wrong-scope", false)]
    [InlineData("wrong-type", false)]
    [InlineData("owned-shadow", false)]
    [InlineData("explicit-null", true)]
    [InlineData("present", true)]
    public async Task Outside_result_fixtures_preserve_missing_null_identity_and_owned_results(string profile, bool available)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        var type = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(string)));
        var intType = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(int)));
        var types = harness.Services.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), type.Alias); types.RegisterType(typeof(int), intType.Alias);
        var result = new ActivityResultContract(type, false, ActivityValuePolicy.Default, []);
        var producerContract = new ActivityContract("test.producer", "1", "test.producer", JsonSerializer.SerializeToElement(new { }), [],
            result, ["Done"], new("test.producer", "probe"));
        var producer = new ExecutableNode("before", "before", "test.producer", "1", "test.producer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: producerContract);
        var selected = new ExecutableNode("selected", "selected", "test.consumer", "1", "test.consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = new("value", type, ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.ActivityResult, activityResult: new("before", "$result", "expected", isOptional: true)) }, new Dictionary<string, string>());
        var root = new ExecutableNode("container", "container", "container", "1", "container", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots: [new("Activities", [producer, selected])]);
        var fixture = new RuntimeTestResultFixture(profile == "owned-shadow" ? "selected" : "before", profile == "wrong-scope" ? "other" : "expected",
            profile == "explicit-null" ? ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline) :
            profile == "wrong-type" ? ValueEnvelope.Inline(intType, JsonSerializer.SerializeToElement(7), ValueProtectionPolicy.InstanceInline) :
                ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("supplied"), ValueProtectionPolicy.InstanceInline));
        var request = new RuntimeTestContextInspectionRequest(WorkflowExecutionHarness.NewExecutable(root), "selected", "inspection",
            new("setup", "fingerprint", [], profile == "missing" ? [] : [fixture]),
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>());
        var inspected = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(request);
        Assert.Equal(available, inspected.IsAvailable);
        if (available) Assert.Equal(fixture.Result.Presence, Assert.Single(inspected.Context!.Results).Result.Presence);
        else Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(inspected.Findings).Kind);
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("inspection"));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("inspection"));
    }

    [Theory]
    [InlineData("present", true)]
    [InlineData("missing-variable", false)]
    [InlineData("wrong-type", false)]
    [InlineData("non-ancestor", false)]
    [InlineData("iteration", false)]
    [InlineData("duplicate", false)]
    public async Task Supplied_frames_require_enclosing_declarations_and_are_snapshotted_without_invocations(string profile, bool available)
    {
        await using var harness = WorkflowExecutionHarness.Create().Build();
        var type = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(int)));
        var types = harness.Services.GetRequiredService<IWellKnownTypeRegistry>(); types.RegisterType(typeof(int), type.Alias);
        types.RegisterType(typeof(string), TypeAliasConvention.CanonicalAlias(typeof(string)));
        var declaration = new RuntimeVariableDeclaration("source", "source", type, ValueProtectionPolicy.InstanceInline);
        var selected = new ExecutableNode("selected", "selected", "test.consumer", "1", "test.consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = new("value", type, ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.VariableRead, variable: new("source", "container")) }, new Dictionary<string, string>());
        var root = new ExecutableNode("container", "container", "container", "1", "container", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots: [new("Activities", [selected])],
            structure: new("test.container", "1", JsonSerializer.SerializeToElement(new { variables = new[] { declaration } }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var values = new Dictionary<string, ValueEnvelope>();
        if (profile != "missing-variable") values["source"] = profile == "wrong-type"
            ? ValueEnvelope.Inline(new(TypeAliasConvention.CanonicalAlias(typeof(string))), JsonSerializer.SerializeToElement("wrong"), ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(7), ValueProtectionPolicy.InstanceInline);
        var frame = new RuntimeTestFrameSeed(profile == "non-ancestor" ? "selected" : "container",
            profile == "iteration" ? VariableFrameKind.Iteration : VariableFrameKind.Container, values);
        var inspected = await harness.Services.GetRequiredService<IRuntimeTestContextInspector>().InspectAsync(new(WorkflowExecutionHarness.NewExecutable(root),
            "selected", "inspection", new("setup", "fingerprint", profile == "duplicate" ? [frame, frame] : [frame], []),
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>()));
        Assert.Equal(available, inspected.IsAvailable);
        if (available)
        {
            values["source"] = ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(99), ValueProtectionPolicy.InstanceInline);
            Assert.Equal(7, Assert.Single(inspected.Context!.Frames).Frame.Values["source"].InlineValue!.Value.GetInt32());
            Assert.Equal(["selected"], inspected.Context.OwnedNodeIds);
        }
        else Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(inspected.Findings).Kind);
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("inspection"));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("inspection"));
    }

    private sealed class UnavailableInspector : IRuntimeTestContextInspector
    {
        public ValueTask<RuntimeTestContextPreparation> InspectAsync(RuntimeTestContextInspectionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(RuntimeTestContextPreparation.Unavailable(
            [new("test.unavailable", "Unavailable test inspector.", RuntimeTestContextFindingKind.Unavailable)]));
    }

    private sealed class ConstructorProbe
    {
        internal static int Constructions;
        public ConstructorProbe() => Interlocked.Increment(ref Constructions);
    }
    private sealed class UnqualifiedEvaluator : IPortableExpressionEvaluator
    {
        internal static int Calls;
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
        { Interlocked.Increment(ref Calls); return ValueTask.FromResult(JsonSerializer.SerializeToElement(42)); }
    }
    private sealed class UnrelatedCapabilities : IPortableExpressionCapabilities
    {
        public bool CanEvaluate(ExpressionDefinition definition) => true;
    }
}
