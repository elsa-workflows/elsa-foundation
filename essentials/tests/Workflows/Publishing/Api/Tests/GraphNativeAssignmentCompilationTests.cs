using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class GraphNativeAssignmentCompilationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    [Theory]
    [InlineData("variable", "body")]
    [InlineData("request", "body")]
    [InlineData("variable", "graph-boundary")]
    public async Task Graph_local_assignments_pin_the_native_typed_conversion_and_canonical_declarations(string source, string ownerId)
    {
        using var services = new ServiceCollection().AddScoped<IActivityTemplateInputBindingCompiler>(_ =>
            new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create())).BuildServiceProvider();
        var structure = new NativeOwner();
        var provider = new GraphActivityProvider(structure, new ActivityContractAuthoringValidator(new Capabilities()),
            services.GetRequiredService<IServiceScopeFactory>());
        var value = source == "variable" ? new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "number", declaringScopeId = ownerId }), "Variable")
            : new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "amount" }), "Input");
        var assignment = new ActivityNode("assign", "elsa.intrinsic.set@1", [new("value", value, null, null, null, null)], [])
        { Intrinsic = new(AuthoredWorkflowIntrinsicKind.Set, new("Elsa.Any"), new("text", ownerId)) };
        VariableDefinition[] declarations = [new("number", "Number", new("Double"), null, new(1.5, "Literal")), new("text", "Text", new("String"), null, new("", "Literal"))];
        var root = new ActivityNode(ownerId, "sequence-version", [], [], new("owned", "1", JsonSerializer.SerializeToElement(new { activities = new[] { assignment }, variables = declarations }, Options)));
        var contract = new DesignContract("1", source == "request" ? [new("amount", "Amount", new("Double"), true, false, null, "elsa.json")] : [], [], [new("done", "Done", true)]);
        var dependency = new ActivityResolvedDependency("sequence-definition", "sequence-version", "1.0.0", "sequence-template", "sequence-hash",
            new("1", [], [], [new("done", "Done", true)]), ActivityDefinitionVersionLifecycle.Active, null, ownerId, [new("AuthoredNode", ownerId)], DeclaredStructure: new("owned", "1"));
        var manifest = new ActivityProviderManifest(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new { rootActivity = root, variables = Array.Empty<object>(), outputMappings = Array.Empty<object>() }, Options));
        var result = await provider.CompileAsync(new("definition", "type", "draft", 1, "1.0.0", contract, manifest, [dependency], provider.CompilerFingerprint));
        Assert.Empty(result.Diagnostics);
        var setter = Assert.Single(result.ExecutableRoot!.DescendantsAndSelf(), node => node.IntrinsicKind == WorkflowIntrinsicKind.Set);
        Assert.Equal(ownerId, setter.IntrinsicVariable!.DeclaringScopeId);
        var binding = setter.InputBindings["value"];
        Assert.Equal("String", binding.TargetType.Alias);
        Assert.NotNull(binding.ConversionPlan);
        Assert.Equal(ValueConversionOperation.NumericToString, binding.ConversionPlan.Operation);
        Assert.NotEqual(ownerId, result.ExecutableRoot.ExecutableNodeId);
        var overlay = Assert.Single(result.Occurrences);
        var locals = overlay.Structure!.Payload.GetProperty("variables").Deserialize<RuntimeVariableDeclaration[]>(Options)!;
        Assert.Equal(new[] { "number", "text" }, locals.Select(local => local.VariableKey));
        Assert.All(locals, local => Assert.NotNull(local.InitialBinding));
    }

    // A native Set compiles its value under the input's declared policy and ignores the argument envelope's own
    // storage and sensitivity hints. The promoted Graph-local Set must compile the same binding, not a new external
    // store or sensitivity requirement.
    [Theory]
    [InlineData("external-profile", null)]
    [InlineData(null, true)]
    public async Task Graph_local_set_keeps_the_native_intrinsic_input_policy(string? storageProfile, bool? isSensitive)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        using var services = new ServiceCollection().AddScoped<IActivityTemplateInputBindingCompiler>(_ =>
            new RuntimeInputBindingCompiler(registry)).BuildServiceProvider();
        var provider = new GraphActivityProvider(new NativeOwner(), new ActivityContractAuthoringValidator(new Capabilities()),
            services.GetRequiredService<IServiceScopeFactory>());
        var value = new ArgumentValue("one", "Literal");
        var assignment = new ActivityNode("assign", "elsa.intrinsic.set@1", [new("value", value, null, null, storageProfile, isSensitive)], [])
        { Intrinsic = new(AuthoredWorkflowIntrinsicKind.Set, new("Elsa.Any"), new("text", "body")) };
        VariableDefinition[] declarations = [new("text", "Text", new("String"), null, new("", "Literal"))];
        var root = new ActivityNode("body", "sequence-version", [], [], new("owned", "1", JsonSerializer.SerializeToElement(new { activities = new[] { assignment }, variables = declarations }, Options)));
        var contract = new DesignContract("1", [], [], [new("done", "Done", true)]);
        var dependency = new ActivityResolvedDependency("sequence-definition", "sequence-version", "1.0.0", "sequence-template", "sequence-hash",
            new("1", [], [], [new("done", "Done", true)]), ActivityDefinitionVersionLifecycle.Active, null, "body", [new("AuthoredNode", "body")], DeclaredStructure: new("owned", "1"));
        var manifest = new ActivityProviderManifest(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new { rootActivity = root, variables = Array.Empty<object>(), outputMappings = Array.Empty<object>() }, Options));

        var result = await provider.CompileAsync(new("definition", "type", "draft", 1, "1.0.0", contract, manifest, [dependency], provider.CompilerFingerprint));

        Assert.Empty(result.Diagnostics);
        var binding = Assert.Single(result.ExecutableRoot!.DescendantsAndSelf(), node => node.IntrinsicKind == WorkflowIntrinsicKind.Set).InputBindings["value"];
        var native = new RuntimeInputBindingCompiler(registry).Compile("assign",
            new InputDefinition("value", "value", new("String"), null, "value", null, IsNullable: false, IsRequired: true), value);
        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal(native.EffectivePolicy.Lifecycle, binding.EffectivePolicy.Lifecycle);
        Assert.Equal(native.EffectivePolicy.Storage, binding.EffectivePolicy.Storage);
        Assert.Equal(native.EffectivePolicy.IsSensitive, binding.EffectivePolicy.IsSensitive);
        Assert.Equal(native.EffectivePolicy.RequiresEncryption, binding.EffectivePolicy.RequiresEncryption);
        Assert.NotEqual(DurableValueStorage.External, binding.EffectivePolicy.Storage);
    }

    private sealed class Capabilities : IActivityContractCapabilityCatalog
    { public IReadOnlyCollection<ActivityContractTypeCapability> Types => []; }

    private sealed class NativeOwner : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => activity.Structure is null ? [] :
            [new("body", activity.Structure.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(Options)!)];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> children) => activity;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => activity.Structure is null ? [] :
            activity.Structure.Payload.GetProperty("variables").Deserialize<VariableDefinition[]>(Options)!;
        public bool SupportsScopedVariables(ActivityNode activity) => activity.Structure is not null;
        public bool HasHandler(ActivityNodeStructure structure) => structure.Kind == "owned";
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure is null ? null :
            new("owned", "1", JsonSerializer.SerializeToElement(new { activities = ProjectChildren(activity).SelectMany(slot => slot.Activities).Select(child => child.NodeId), variables = ProjectScopedVariables(activity) }, Options));
    }
}
