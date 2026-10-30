using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableHashContractTests
{
    private readonly WorkflowExecutableHasher _hasher = new();

    [Fact]
    public void A_retained_v1_hash_still_verifies_without_reidentifying_the_artifact()
    {
        var legacy = LegacyWorkflowHashFixture.Load();

        Assert.Equal("sha256:7d70c113a0f4fd8857721b9cfafbb4e19b7f400700475ff8904de6a203f92edf", legacy.Identity.ArtifactHash);
        Assert.True(_hasher.MatchesHash(legacy));
        Assert.NotEqual(legacy.Identity.ArtifactHash, Hash(legacy));
        Assert.Equal("artifact-7d70c113a0f4", legacy.Identity.ArtifactId);
    }

    [Fact]
    public void Historical_verification_does_not_require_serializing_fields_outside_the_v1_coverage()
    {
        var legacy = LegacyWorkflowHashFixture.Load();
        var json = string.Concat(Enumerable.Repeat("{\"nested\":", 1100)) + "0" + new string('}', 1100);
        using var schema = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 2048 });
        var binding = legacy.RootActivity.InputBindings["value"];
        var historical = LegacyWorkflowHashFixture.WithBinding(legacy,
            new RuntimeInputBinding("value", new ValueTypeDescriptor("Elsa.Any", schema: schema.RootElement),
                binding.EffectivePolicy, RuntimeInputBindingSource.Expression, expression: binding.Expression,
                metadata: binding.Metadata));

        // V1 did not serialize destination schemas. Historical verification must keep that coverage,
        // even when a schema admitted by an older host exceeds the new renderer's depth limit.
        Assert.True(_hasher.MatchesHash(historical));
    }

    [Theory]
    [InlineData(96)]
    [InlineData(900)]
    public void Current_hashes_preserve_schemas_within_the_behavioral_writer_depth(int depth)
    {
        var legacy = LegacyWorkflowHashFixture.Load();
        var json = string.Concat(Enumerable.Repeat("{\"nested\":", depth)) + "0" + new string('}', depth);
        using var schema = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1000 });
        var binding = legacy.RootActivity.InputBindings["value"];
        var changed = LegacyWorkflowHashFixture.WithBinding(legacy,
            new RuntimeInputBinding("value", new ValueTypeDescriptor("Elsa.Any", schema: schema.RootElement),
                binding.EffectivePolicy, RuntimeInputBindingSource.Expression, expression: binding.Expression,
                metadata: binding.Metadata), _hasher);

        Assert.NotEqual(legacy.Identity.ArtifactHash, changed.Identity.ArtifactHash);
        Assert.True(_hasher.MatchesHash(changed));
    }

    [Fact]
    public void Historical_verification_does_not_accept_a_changed_expression()
    {
        var legacy = LegacyWorkflowHashFixture.Load();
        var type = legacy.RootActivity.InputBindings["value"].TargetType;
        var changed = LegacyWorkflowHashFixture.WithBinding(legacy, new RuntimeInputBinding("value", type,
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "8", legacy.RootActivity.InputBindings["value"].Expression!.ResultType),
            metadata: legacy.RootActivity.InputBindings["value"].Metadata));

        Assert.False(_hasher.MatchesHash(changed));
    }

    [Fact]
    public void Historical_verification_does_not_mask_a_current_hash_with_a_changed_destination()
    {
        var legacy = LegacyWorkflowHashFixture.Load();
        var current = LegacyWorkflowHashFixture.WithRoot(legacy, legacy.RootActivity, _hasher);
        var type = new ValueTypeDescriptor("Int32");
        var changed = LegacyWorkflowHashFixture.WithBinding(current, new RuntimeInputBinding("value", type,
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: current.RootActivity.InputBindings["value"].Expression,
            metadata: current.RootActivity.InputBindings["value"].Metadata));

        Assert.True(_hasher.MatchesHash(current));
        Assert.False(_hasher.MatchesHash(changed));
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("collection")]
    [InlineData("schema")]
    [InlineData("schema-version")]
    public void Changing_a_binding_destination_contract_changes_its_behavioral_identity(string change)
    {
        var original = LegacyWorkflowHashFixture.Load();
        var type = change switch
        {
            "alias" => new ValueTypeDescriptor("Int32"),
            "collection" => new ValueTypeDescriptor("Elsa.Any", CollectionKind.Array),
            "schema" => new ValueTypeDescriptor("Elsa.Any", schema: JsonSerializer.SerializeToElement(new { type = "integer" })),
            _ => new ValueTypeDescriptor("Elsa.Any", schemaVersion: 1)
        };
        var binding = new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: original.RootActivity.InputBindings["value"].Expression,
            metadata: original.RootActivity.InputBindings["value"].Metadata);
        var changed = LegacyWorkflowHashFixture.WithBinding(original, binding);

        Assert.NotEqual(Hash(original), Hash(changed));
    }

    [Fact]
    public void An_expression_result_contract_is_part_of_its_behavioral_identity()
    {
        var original = LegacyWorkflowHashFixture.Load();
        WorkflowExecutable Expression(string alias) => LegacyWorkflowHashFixture.WithBinding(original,
            new RuntimeInputBinding("value", new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Expression,
                expression: new RuntimeExpressionBinding("JavaScript", "getVariable('Counter') + 1", new RuntimeValueTypeDescriptor("alias", alias, null))));

        Assert.NotEqual(Hash(Expression("Elsa.Any")), Hash(Expression("Int32")));
    }

    [Theory]
    [InlineData("Variable")]
    [InlineData("WorkflowRequest")]
    public void Source_reference_fields_remain_distinct_even_when_they_contain_delimiters(string source)
    {
        var original = LegacyWorkflowHashFixture.Load();
        WorkflowExecutable Reference(string first, string second) => LegacyWorkflowHashFixture.WithBinding(original,
            source == "Variable"
                ? new RuntimeInputBinding("value", new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.VariableRead, variable: new RuntimeVariableReference(second, first))
                : new RuntimeInputBinding("value", new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new RuntimeWorkflowRequestReference(first, second)));

        Assert.NotEqual(Hash(Reference("a:b", "c")), Hash(Reference("a", "b:c")));
    }

    [Fact]
    public void Protection_policy_is_part_of_the_binding_contract()
    {
        var original = LegacyWorkflowHashFixture.Load();
        var binding = original.RootActivity.InputBindings["value"];
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var changed = LegacyWorkflowHashFixture.WithBinding(original, new RuntimeInputBinding("value", binding.TargetType,
            policy, RuntimeInputBindingSource.Expression, expression: binding.Expression, metadata: binding.Metadata));

        Assert.NotEqual(Hash(original), Hash(changed));
    }

    [Fact]
    public void Portable_parameter_conversion_plans_are_part_of_the_binding_contract()
    {
        var original = LegacyWorkflowHashFixture.Load();
        WorkflowExecutable ParameterPlan(string alias) => LegacyWorkflowHashFixture.WithBinding(original,
            new RuntimeInputBinding("value", new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Expression,
                expression: new RuntimeExpressionBinding("JavaScript", "args.input", parameters:
                    new Dictionary<string, Elsa.Expressions.Core.Models.ExpressionParameterBinding>
                    {
                        ["input"] = new Elsa.Expressions.Core.Models.LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(7))
                    }, parameterConversionPlans: new Dictionary<string, ValueConversionPlan>
                    {
                        ["input"] = ValueConversionPlan.Identity(new ValueTypeDescriptor(alias), ValueRepresentation.TypedValue)
                    })));

        Assert.NotEqual(Hash(ParameterPlan("Int32")), Hash(ParameterPlan("Int64")));
    }

    [Fact]
    public void Schema_property_order_does_not_change_identity()
    {
        var original = LegacyWorkflowHashFixture.Load();
        WorkflowExecutable Schema(string json)
        {
            using var document = JsonDocument.Parse(json);
            var type = new ValueTypeDescriptor("Elsa.Any", schema: document.RootElement);
            return LegacyWorkflowHashFixture.WithBinding(original, new RuntimeInputBinding("value", type,
                ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
                expression: original.RootActivity.InputBindings["value"].Expression));
        }

        Assert.Equal(Hash(Schema("{\"type\":\"integer\",\"minimum\":0}")), Hash(Schema("{\"minimum\":0,\"type\":\"integer\"}")));
    }

    private string Hash(WorkflowExecutable executable) => _hasher.ComputeHash(executable.RootActivity, executable.InputContract!,
        executable.Dependencies, executable.PersistenceMode, executable.WorkflowVariables, executable.IncidentStrategy);
}
