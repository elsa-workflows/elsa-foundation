using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 246 FR-002: the contract an imported reusable activity is published with carries the workflow's inputs and
/// outputs and exactly the outcomes the conversion derived, so callers route on what the publication declares.
/// </summary>
public sealed class Elsa3ImportedActivityContractTests
{
    [Fact]
    public async Task Inputs_and_outputs_keep_their_keys_names_and_types_and_default_to_json_storage()
    {
        var definition = Reusable(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("x")))));
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "message", Type = "String" }];
        definition.Outputs = [new Elsa3WorkflowArgumentDefinition { Name = "result", Type = "String" }];

        var converted = await ConvertAsync(definition);
        var contract = Elsa3ImportedActivityContract.Build(converted);

        var input = Assert.Single(contract.Inputs);
        Assert.Equal((converted.State.Inputs.Single().ReferenceKey, "message"), (input.ReferenceKey, input.Name));
        Assert.Equal(converted.State.Inputs.Single().Type, input.Type);
        Assert.False(input.IsRequired);
        Assert.True(input.IsNullable);
        Assert.Equal("elsa.json", input.StorageDriverKey);
        var output = Assert.Single(contract.Outputs);
        Assert.Equal((converted.State.Outputs.Single().ReferenceKey, "result"), (output.ReferenceKey, output.Name));
        Assert.Equal(converted.State.Outputs.Single().StorageDriverType ?? "elsa.json", output.StorageDriverKey);
    }

    [Fact]
    public async Task A_value_typed_input_or_output_is_not_nullable()
    {
        // The publisher refuses a nullable member of a type without null (activity.contract.nullability-unavailable).
        var definition = Reusable(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("x")))));
        // CLR names, the legacy Elsa 3 token form this suite's type registry resolves.
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "approve", Type = "System.Boolean" }];
        definition.Outputs = [new Elsa3WorkflowArgumentDefinition { Name = "count", Type = "System.Int32" }];

        var contract = Elsa3ImportedActivityContract.Build(await ConvertAsync(definition));

        Assert.False(Assert.Single(contract.Inputs).IsNullable);
        Assert.False(Assert.Single(contract.Outputs).IsNullable);
    }

    [Fact]
    public async Task Outcomes_are_exactly_the_converted_boundary_outcomes()
    {
        var definition = Reusable(Sequence("root", Activity("fail", "Elsa.Complete", ("outcomes", Literal("fail")))), "fail");

        var converted = await ConvertAsync(definition);
        var contract = Elsa3ImportedActivityContract.Build(converted);

        Assert.Equal(converted.BoundaryOutcomes, contract.Outcomes);
        Assert.Equal(["done", "fail"], contract.Outcomes.Select(outcome => outcome.ReferenceKey));
    }

    [Fact]
    public async Task A_body_without_complete_declares_only_done()
    {
        var converted = await ConvertAsync(Reusable(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("x"))))));

        Assert.Equal("done", Assert.Single(Elsa3ImportedActivityContract.Build(converted).Outcomes).ReferenceKey);
    }

    private static async Task<Elsa3.Mapping.Conversion.Elsa3ConvertedDefinition> ConvertAsync(Elsa3WorkflowDefinition definition)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, isReusable: true);
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return result.Value!;
    }

    private static Elsa3WorkflowDefinition Reusable(Elsa3Activity root, params string[] outcomes)
    {
        var definition = ConversionServices.Definition(root);
        definition.Options = new Elsa3WorkflowOptions { UsableAsActivity = true };
        definition.Outcomes = outcomes.ToList();
        return definition;
    }
}
