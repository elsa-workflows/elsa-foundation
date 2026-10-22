using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 264 (#3122): a converted variable has no starting value when Elsa 3 had none, and otherwise the value Elsa 3
/// gave it, as a <c>Literal</c> publication accepts. A collection starts from a JSON array and a dictionary from a JSON
/// object, never from comma-separated text. A value that cannot start as it did in Elsa 3 is refused with how to fix it.
/// </summary>
public sealed class Elsa3VariableStartingValueTests
{
    private static readonly IObjectConverter ObjectConverter = SerializationServices.ObjectConverter();

    [Fact]
    public async Task A_workflow_variable_without_a_value_has_no_starting_value()
    {
        var result = await ConvertAsync(new Elsa3Variable { Id = "approval", Name = "approval", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var variable = Assert.Single(result.Value!.State.Variables);
        Assert.Equal("approval", variable.ReferenceKey);
        Assert.Null(variable.Default);
    }

    [Fact]
    public async Task A_container_variable_without_a_value_has_no_starting_value()
    {
        var root = Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))));
        root.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "String" } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var payload = Node(result.Value!.State, "root").Structure!.Payload;
        var variable = Assert.Single(payload.GetProperty("variables").EnumerateArray());
        Assert.Equal("local", variable.GetProperty("referenceKey").GetString());
        Assert.True(!variable.TryGetProperty("default", out var initial) || initial.ValueKind == JsonValueKind.Null, variable.GetRawText());
    }

    public static TheoryData<string, bool, string, string, CollectionKind, string> Values => new()
    {
        { "String", false, "hello", "String", CollectionKind.Single, "\"hello\"" },
        { "String", false, "", "String", CollectionKind.Single, "\"\"" },
        { "Int32", false, "5", "Int32", CollectionKind.Single, "5" },
        { "Boolean", false, "true", "Boolean", CollectionKind.Single, "true" },
        // The corpus names enums by their assembly-qualified CLR type; this host registers the enum.
        { "System.Net.HttpStatusCode, System.Net.Primitives", false, "OK", "System.Net.HttpStatusCode", CollectionKind.Single, "200" },
        // Elsa 3 kept an Object variable's text as it was, JSON or not.
        { "Object", false, """{"a":1}""", "Object", CollectionKind.Single, "\"{\\u0022a\\u0022:1}\"" },
        // Elsa 3 writes a list as List<T> and an array as T[]: a known element type keeps its type in a collection.
        { "List<String>", false, """["a","b"]""", "String", CollectionKind.List, """["a","b"]""" },
        { "List<Int32>", false, "[1, 2]", "Int32", CollectionKind.List, "[1,2]" },
        { "List<Object>", false, """[{"a":1}]""", "Object", CollectionKind.List, """[{"a":1}]""" },
        { "String[]", false, """["x"]""", "String", CollectionKind.Array, """["x"]""" },
        { "String", true, """["x","y"]""", "String", CollectionKind.Array, """["x","y"]""" },
        // An element type Elsa 4 does not know: an Object holding the parsed array.
        { "FormFile[]", false, """[{"name":"a.txt"}]""", "Object", CollectionKind.Single, """[{"name":"a.txt"}]""" },
        // Elsa 3's dictionaries: an Object holding the parsed object.
        { "ObjectDictionary", false, """{"a":1}""", "Object", CollectionKind.Single, """{"a":1}""" },
        { "HttpHeaders", false, """{"Accept":["application/json"]}""", "Object", CollectionKind.Single, """{"Accept":["application/json"]}""" }
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void A_starting_value_is_the_value_Elsa_3_gave_it_as_a_literal(
        string typeName, bool isArray, string value, string expectedAlias, CollectionKind expectedKind, string expectedJson)
    {
        var source = new Elsa3Variable { Id = "v", Name = "v", TypeName = typeName, IsArray = isArray, Value = value };

        Assert.True(Elsa3VariableMapper.TryMap(Registry(), ObjectConverter, source, out var variable, out var refusal), refusal?.Message);

        Assert.Equal(new TypeReference(expectedAlias, expectedKind), variable.Type);
        var initial = Assert.IsType<ArgumentValue>(variable.Default);
        Assert.Equal("Literal", initial.ExpressionType);
        Assert.Equal(expectedJson, Assert.IsType<JsonElement>(initial.Value).GetRawText());
    }

    [Theory]
    [InlineData("List<String>", "String", CollectionKind.List)]
    [InlineData("List<Object>", "Object", CollectionKind.List)]
    [InlineData("ObjectDictionary", "Object", CollectionKind.Single)]
    [InlineData("HttpHeaders", "Object", CollectionKind.Single)]
    public void A_variable_without_a_value_gets_the_same_Elsa_4_type(string typeName, string expectedAlias, CollectionKind expectedKind)
    {
        var source = new Elsa3Variable { Id = "v", Name = "v", TypeName = typeName };

        Assert.True(Elsa3VariableMapper.TryMap(Registry(), ObjectConverter, source, out var variable, out _));

        Assert.Equal(new TypeReference(expectedAlias, expectedKind), variable.Type);
        Assert.Null(variable.Default);
    }

    public static TheoryData<string, string, string, string> Refusals => new()
    {
        // Elsa 3 started this one as 0, the type's default, without a warning; it is refused as an authoring mistake.
        { "Int32", "abc", "cannot be converted to its type 'Int32'", "a valid Int32 value" },
        // No comma parsing: the refusal shows the JSON array the author most likely meant.
        { "List<String>", "a, b", "is not a JSON array", """for example ["a","b"]""" },
        { "List<Int32>", "1, 2", "is not a JSON array", "for example [1, 2]" },
        { "List<Int32>", """["a"]""", "items cannot be converted to 'Int32'", "for example [1, 2]" },
        { "FormFile[]", "{bad", "is not a JSON array", "as a JSON array" },
        { "ObjectDictionary", "a=1", "is not a JSON object", """for example {"key": "value"}""" },
        { "HttpHeaders", "Accept: text/plain", "is not a JSON object", """for example {"Accept": ["application/json"]}""" },
        // A CLR type the host registers no alias for runs as Object in Elsa 4.
        { "System.DayOfWeek", "Monday", "is not known in Elsa 4", "change the variable's type to Object" }
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_starting_value_that_cannot_start_as_in_Elsa_3_is_refused_with_how_to_fix_it(string typeName, string value, string reason, string fix)
    {
        var source = new Elsa3Variable { Id = "v", Name = "items", TypeName = typeName, Value = value };

        Assert.False(Elsa3VariableMapper.TryMap(Registry(), ObjectConverter, source, out _, out var refusal));
        Assert.Contains("Variable 'items'", refusal!.Message);
        Assert.Contains(reason, refusal.Message);
        Assert.Contains(fix, refusal.Guidance);
    }

    [Fact]
    public async Task A_workflow_variable_whose_value_cannot_start_refuses_the_definition()
    {
        var result = await ConvertAsync(
            new Elsa3Variable { Id = "name", Name = "name", TypeName = "String", Value = "ok" },
            new Elsa3Variable { Id = "count", Name = "count", TypeName = "Object" },
            new Elsa3Variable { Id = "names", Name = "names", TypeName = "List<String>", Value = "a, b" });

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.VariableValueUnconvertible, refusal.Code);
        Assert.Equal("$.variables[2]", refusal.Path);
        Assert.Contains("""["a","b"]""", refusal.Guidance);
        Assert.Equal("names", refusal.Metadata["VariableName"]);
    }

    [Fact]
    public async Task A_container_variable_whose_value_cannot_start_refuses_the_definition()
    {
        var root = Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))));
        root.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "HttpHeaders", value = "x" } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.VariableValueUnconvertible, refusal.Code);
        Assert.Contains("Variable 'local'", refusal.Message);
        Assert.Contains("JSON object", refusal.Guidance);
    }

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi")))), variables: variables));

    /// <summary>The conversion registry plus an enum alias a host can register.</summary>
    private static IWellKnownTypeRegistry Registry()
    {
        var registry = ConversionServices.TypeRegistry();
        registry.RegisterType(typeof(System.Net.HttpStatusCode), "System.Net.HttpStatusCode");
        return registry;
    }
}
