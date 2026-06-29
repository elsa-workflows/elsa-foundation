using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Expressions.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

public sealed class VariableMapperTests
{
    private readonly VariableMapper _mapper;

    public VariableMapperTests()
    {
        var registry = TestRegistry.SeededWithPrimitives();
        var objectConverter = new ObjectConverter(new ServiceCollection().BuildServiceProvider());
        _mapper = new VariableMapper(registry, objectConverter, new VariableDefaultValueFormatter(), NullLogger<VariableMapper>.Instance);
    }

    private static Type ClosedTypeOf(IVariable variable) => variable.GetType().GetGenericArguments()[0];

    [Theory]
    [InlineData("String", CollectionKind.Single, typeof(string))]
    [InlineData("String", CollectionKind.Array, typeof(string[]))]
    [InlineData("String", CollectionKind.List, typeof(List<string>))]
    [InlineData("String", CollectionKind.HashSet, typeof(HashSet<string>))]
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
