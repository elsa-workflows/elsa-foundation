using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// A workflow's inputs are its request members, keyed by name, so a workflow offers the request read of an input
/// rather than an <c>Input</c> read; a stored <c>Input</c> read is still admitted, because publication lowers it to that
/// request read. A reusable body, whose inputs are its boundary's, keeps offering <c>Input</c>.
/// </summary>
public sealed partial class ActivityDataSelectionAnalyzerTests
{
    private static readonly InputDefinition RenamedInput = new("customer-ref", "customer", new("String"), null, "Customer", null, true);

    [Fact]
    public async Task A_workflow_offers_the_request_read_of_an_input_by_its_name()
    {
        using var provider = Provider();
        var document = Document(Sequence(Node("consumer"))) with { Inputs = [RenamedInput] };

        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"));

        var binding = Assert.Single(result.Sources, source => source.Id == "input:customer-ref").Binding!;
        Assert.Equal("WorkflowRequest", binding.ExpressionType);
        Assert.Equal("customer", Assert.IsType<JsonElement>(binding.Value).GetProperty("memberKey").GetString());
    }

    [Fact]
    public async Task A_reusable_body_keeps_offering_an_Input_read_of_its_boundary_input()
    {
        using var provider = Provider();
        var document = Document(Sequence(Node("consumer"))) with { ResourceKind = "ActivityDefinition", Inputs = [RenamedInput] };

        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"));

        var binding = Assert.Single(result.Sources, source => source.Id == "input:customer-ref").Binding!;
        Assert.Equal("Input", binding.ExpressionType);
        Assert.Equal("customer-ref", Assert.IsType<VariableReference>(binding.Value).ReferenceKey);
    }

    [Theory]
    [InlineData("customer-ref", true)]
    [InlineData("customer", false)]
    public async Task A_stored_workflow_level_Input_read_is_admitted_by_its_reference_key(string referenceKey, bool admitted)
    {
        using var provider = Provider();
        var document = Document(Sequence(Node("consumer", Input(new(JsonSerializer.SerializeToElement(new { referenceKey }), "Input")))))
            with { Inputs = [RenamedInput] };

        var result = await Analyzer(provider).AnalyzeAsync(document);

        if (admitted)
            Assert.Empty(result.Findings);
        else
            Assert.Equal("ActivityData/UnknownInput", Assert.Single(result.Findings).Type);
    }
}
