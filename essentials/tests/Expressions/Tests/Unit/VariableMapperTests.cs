using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Expressions.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Expressions.Tests.Unit;

public sealed class VariableMapperTests
{
    private readonly VariableMapper _mapper;

    public VariableMapperTests()
    {
        var registry = TestRegistry.SeededWithPrimitives();
        var objectConverter = SerializationServices.ObjectConverter();
        _mapper = new VariableMapper(registry, objectConverter, new VariableDefaultValueFormatter(), NullLogger<VariableMapper>.Instance);
    }

    private static Type ClosedTypeOf(IVariable variable) => variable.GetType().GetGenericArguments()[0];

    [Fact]
    public void Typed_defaults_round_trip_as_values_instead_of_type_names_or_json_strings()
    {
        IVariable[] variables = [
            new Variable<int>("Total", 42, "total"),
            new Variable<bool>("Enabled", true, "enabled"),
            new Variable<string>("Text", "{\"n\":1}", "text"),
            new Variable<int[]>("Numbers", [1, 2], "numbers"),
            new Variable<Dictionary<string, string>>("Map", new() { ["OrderID"] = "René" }, "map")
        ];
        foreach (var original in variables)
        {
            var definition = _mapper.Map(original);
            var retained = Assert.IsType<JsonElement>(definition.Default!.Value);
            var expected = JsonConversionContract.SerializeToElement(original.DefaultValue, original.GetVariableType());
            Assert.True(JsonCanonicalForm.Equals(expected, retained));
            var restored = _mapper.Map(definition);
            Assert.Equal(original.GetVariableType(), restored.GetVariableType());
            Assert.True(JsonCanonicalForm.Equals(expected, JsonConversionContract.SerializeToElement(restored.DefaultValue, restored.GetVariableType())));
        }
        Assert.Null(_mapper.Map(new Variable<string>("Empty", null!, "empty")).Default);
    }

    [Theory]
    [InlineData("String", CollectionKind.Single, typeof(string))]
    [InlineData("String", CollectionKind.Array, typeof(string[]))]
    [InlineData("String", CollectionKind.List, typeof(List<string>))]
    [InlineData("String", CollectionKind.HashSet, typeof(HashSet<string>))]
    [InlineData("String", CollectionKind.Dictionary, typeof(Dictionary<string, string>))]
    [InlineData("Int32", CollectionKind.Single, typeof(int))]
    [InlineData("Int32", CollectionKind.Array, typeof(int[]))]
    [InlineData("Int32", CollectionKind.List, typeof(List<int>))]
    [InlineData("Int32", CollectionKind.HashSet, typeof(HashSet<int>))]
    [InlineData("Boolean", CollectionKind.Single, typeof(bool))]
    [InlineData("Boolean", CollectionKind.Array, typeof(bool[]))]
    [InlineData("Boolean", CollectionKind.List, typeof(List<bool>))]
    [InlineData("Boolean", CollectionKind.HashSet, typeof(HashSet<bool>))]
    public void Map_AliasAndCollectionKind_ResolvesToClosedClrType(string alias, CollectionKind kind, Type expected)
    {
        var definition = new VariableDefinition("ref-1", "MyVar", new TypeReference(alias, kind), null, null);

        var variable = _mapper.Map(definition);

        Assert.Equal(expected, ClosedTypeOf(variable));
    }

    [Fact]
    public void Map_UnknownAlias_FallsBackToObject()
    {
        var definition = new VariableDefinition("ref-1", "MyVar", new TypeReference("Acme.Does.Not.Exist"), null, null);

        var variable = _mapper.Map(definition);

        Assert.Equal(typeof(object), ClosedTypeOf(variable));
    }

    [Fact]
    public void Map_UnknownAlias_PreservesReferenceKeyAndName()
    {
        var definition = new VariableDefinition("ref-42", "Mystery", new TypeReference("Acme.Unknown"), null, null);

        var variable = _mapper.Map(definition);

        Assert.Equal("ref-42", variable.Id);
        Assert.Equal("Mystery", variable.Name);
    }

    [Theory]
    [InlineData(typeof(string), "String", CollectionKind.Single)]
    [InlineData(typeof(string[]), "String", CollectionKind.Array)]
    [InlineData(typeof(List<string>), "String", CollectionKind.List)]
    [InlineData(typeof(HashSet<string>), "String", CollectionKind.HashSet)]
    [InlineData(typeof(ICollection<string>), "String", CollectionKind.List)]
    [InlineData(typeof(IReadOnlyDictionary<string, string>), "String", CollectionKind.Dictionary)]
    [InlineData(typeof(int), "Int32", CollectionKind.Single)]
    [InlineData(typeof(int[]), "Int32", CollectionKind.Array)]
    [InlineData(typeof(List<int>), "Int32", CollectionKind.List)]
    [InlineData(typeof(HashSet<int>), "Int32", CollectionKind.HashSet)]
    public void Map_Variable_DecomposesValueTypeIntoAliasAndKind(Type valueType, string expectedAlias, CollectionKind expectedKind)
    {
        var variableType = typeof(Variable<>).MakeGenericType(valueType);
        var variable = (Variable)Activator.CreateInstance(variableType)!;
        variable.Id = "ref-1";
        variable.Name = "MyVar";

        var definition = _mapper.Map(variable);

        Assert.Equal(expectedAlias, definition.Type.Alias);
        Assert.Equal(expectedKind, definition.Type.CollectionKind);
    }

    [Fact]
    public void Map_RoundTrip_PreservesAliasAndKind()
    {
        var original = new VariableDefinition("ref-1", "Tags", new TypeReference("String", CollectionKind.HashSet), null, null);

        var roundTripped = _mapper.Map(_mapper.Map(original));

        Assert.Equal("String", roundTripped.Type.Alias);
        Assert.Equal(CollectionKind.HashSet, roundTripped.Type.CollectionKind);
    }
}
