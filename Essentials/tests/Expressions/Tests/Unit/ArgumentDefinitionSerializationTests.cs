using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// FR-001/FR-003/SC-001: authored argument types serialize as exactly { alias, collectionKind } —
/// no decomposed CLR identity (typeName/namespace/assemblyName/assemblyVersion). TypeReference is plain
/// data, so the configured camelCase policy applies without a custom converter (FR-020).
/// </summary>
public sealed class ArgumentDefinitionSerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly string[] ForbiddenTokens = ["typeName", "namespace", "assemblyName", "assemblyVersion"];

    private static void AssertWireShape(string json)
    {
        Assert.Contains("\"alias\"", json);
        Assert.Contains("\"collectionKind\"", json);
        foreach (var token in ForbiddenTokens)
            Assert.DoesNotContain(token, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VariableDefinition_Serializes_AliasAndCollectionKindOnly()
    {
        var definition = new VariableDefinition("ref-1", "Items", new TypeReference("String", CollectionKind.List), null, null);

        var json = JsonSerializer.Serialize(definition, Options);

        AssertWireShape(json);
    }

    [Fact]
    public void VariableDefinition_RoundTrips_TypeReference()
    {
        var definition = new VariableDefinition("ref-1", "Items", new TypeReference("Int32", CollectionKind.Array), null, null);

        var json = JsonSerializer.Serialize(definition, Options);
        var deserialized = JsonSerializer.Deserialize<VariableDefinition>(json, Options)!;

        Assert.Equal("Int32", deserialized.Type.Alias);
        Assert.Equal(CollectionKind.Array, deserialized.Type.CollectionKind);
    }

    [Fact]
    public void InputDefinition_Serializes_AliasAndCollectionKindOnly()
    {
        var definition = new InputDefinition("ref-1", "Tags", new TypeReference("String", CollectionKind.HashSet), null, "Tags", null, true);

        var json = JsonSerializer.Serialize(definition, Options);

        AssertWireShape(json);
    }

    [Fact]
    public void OutputDefinition_Serializes_AliasAndCollectionKindOnly()
    {
        var definition = new OutputDefinition("ref-1", "Result", new TypeReference("Int32", CollectionKind.List), null, "Result", null, false);

        var json = JsonSerializer.Serialize(definition, Options);

        AssertWireShape(json);
    }

    [Fact]
    public void StorageDriverType_Serializes_AsBareAliasString()
    {
        var definition = new VariableDefinition("ref-1", "Items", new TypeReference("String"), "Elsa.Memory.MemoryStorageDriver", null);

        var json = JsonSerializer.Serialize(definition, Options);

        Assert.Contains("\"storageDriverType\":\"Elsa.Memory.MemoryStorageDriver\"", json);
    }
}
