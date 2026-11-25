using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Intrinsic_metadata_preserves_a_contributed_convention_alias_null_contract()
    {
        using var provider = Provider(services =>
        {
            new SerializationFeature().ConfigureServices(services);
            services.AddSingleton<IVariableTypeDescriptorCatalog, NullableChoiceCatalog>();
        });
        provider.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(MetadataChoice), "Acme.NullableChoice");
        var type = new TypeReference(TypeAliasConvention.CanonicalAlias(typeof(MetadataChoice)));
        var intrinsic = Node("set") with { Intrinsic = new(AuthoredWorkflowIntrinsicKind.Set, type, new("choice")) };
        var document = Document(Sequence(intrinsic)) with { Variables = [new("choice", "Choice", type, null, null)] };
        var result = await Analyzer(provider).AnalyzeAsync(document, new("set", "value"));
        Assert.Empty(result.Findings);
        Assert.True(result.Target!.IsNullable);
    }

    private enum MetadataChoice { First = 1 }

    private sealed class NullableChoiceCatalog : IVariableTypeDescriptorCatalog
    {
        public IEnumerable<TypeDescriptor> GetDescriptors() =>
            [new("Acme.NullableChoice", typeof(MetadataChoice), "Choice", "Test", "select", new HashSet<CollectionKind>([CollectionKind.Single]), true, true, new HashSet<string> { "elsa.json" })];
        public IEnumerable<IGrouping<string, TypeDescriptor>> GetDescriptorsByCategory() => GetDescriptors().GroupBy(item => item.Category);
    }

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
        Assert.Equal("customer-ref", Assert.IsType<JsonElement>(binding.Value).GetProperty("referenceKey").GetString());
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
