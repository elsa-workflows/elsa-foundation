using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3NullPolicyConversionTests
{
    [Fact]
    public async Task Contributed_nullable_value_types_keep_the_same_authority_through_preflight_mapping_and_imported_contracts()
    {
        var types = ConversionServices.TypeRegistry();
        types.RegisterType(typeof(NullableRecord), "Acme.NullableRecord");
        var descriptors = new NullableRecordCatalog();
        await using var provider = ConversionServices.Create(RealActivityCatalog.Create(), configure: services =>
        {
            services.AddSingleton<IWellKnownTypeRegistry>(types);
            services.AddSingleton<IVariableTypeDescriptorCatalog>(descriptors);
        });
        var source = ConversionServices.Definition(Sequence("root", Write("write")),
            variables: [new Elsa3Variable { Id = "record", Name = "record", TypeName = "Acme.NullableRecord" }]);
        source.Inputs = [new() { Name = "input", Type = "Acme.NullableRecord" }];
        source.Outputs = [new() { Name = "output", Type = "Acme.NullableRecord" }];
        await using var scope = provider.CreateAsyncScope();
        var converted = await scope.ServiceProvider.GetRequiredService<IElsa3DefinitionConverter>().ConvertAsync(new(source, false));
        Assert.True(converted.Succeeded, string.Join("; ", converted.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Null(Assert.Single(converted.Value!.State.Variables).Default);
        var contract = Elsa3ImportedActivityContract.Build(converted.Value, types, descriptors);
        Assert.True(Assert.Single(contract.Inputs).IsNullable);
        Assert.True(Assert.Single(contract.Outputs).IsNullable);
    }

    private readonly record struct NullableRecord(int Value);
    private sealed class NullableRecordCatalog : IVariableTypeDescriptorCatalog
    {
        public IEnumerable<TypeDescriptor> GetDescriptors() => [new("Acme.NullableRecord", typeof(NullableRecord), "Record", "Test", "json",
            new HashSet<CollectionKind>(Enum.GetValues<CollectionKind>()), true, true, new HashSet<string> { "elsa.json" })];
        public IEnumerable<IGrouping<string, TypeDescriptor>> GetDescriptorsByCategory() => GetDescriptors().GroupBy(item => item.Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Converted_policy_choices_are_portable_definition_data(bool apply)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Write("write"))),
            feature: new Elsa3MappingFeature { ApplyElsa3NullPolicies = apply });
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var imported = JsonSerializer.Deserialize<WorkflowDefinitionState>(JsonSerializer.Serialize(result.Value!.State, options), options)!;
        Assert.Equal(apply, imported.StrategyOptions?.NullHandling?.DefaultOnNull == true);
        Assert.Equal(apply, imported.StrategyOptions?.NullHandling?.UndefinedAsNull == true);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ELS3-NULL-POLICIES");
    }

    [Theory]
    [InlineData(false, "DateTime")]
    [InlineData(true, "DateTime")]
    [InlineData(false, "Int32")]
    [InlineData(true, "Int32")]
    public async Task Array_variable_targets_accept_literal_null_under_either_policy(bool apply, string alias)
    {
        var source = ConversionServices.Definition(Sequence("root", Activity("set", "Elsa.SetVariable",
            ("variable", JsonSerializer.SerializeToElement(new { id = "target" })),
            ("value", JsonSerializer.SerializeToElement(new { typeName = alias, expression = new { type = "Literal", value = (object?)null } })))));
        source.Variables.Add(new Elsa3Variable { Id = "target", Name = "target", TypeName = alias, IsArray = true });
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), source,
            feature: new Elsa3MappingFeature { ApplyElsa3NullPolicies = apply });
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(CollectionKind.Array, Assert.Single(result.Value!.State.Variables).Type.CollectionKind);
    }

    [Theory]
    [InlineData("DateTime")]
    [InlineData("Guid")]
    public async Task Optional_inputs_without_a_supported_implicit_default_are_refused_before_publication(string alias)
    {
        var source = ConversionServices.Definition(Sequence("root", Write("write")));
        source.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "value", Type = alias }];
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), source);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ELS3-INPUT-DEFAULT-REQUIRED" && diagnostic.Guidance!.Contains("default"));
    }
}
