
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 264 (#3122): a converted variable has no starting value when Elsa 3 had none, and otherwise the value Elsa 3's
/// rule gave it, as a <c>Literal</c> publication accepts. A value Elsa 3's rule cannot convert is refused.
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

    public static TheoryData<string, bool, string, string> Values => new()
    {
        { "String", false, "hello", "\"hello\"" },
        { "String", false, "", "\"\"" },
        { "Int32", false, "5", "5" },
        { "Boolean", false, "true", "true" },
        // The corpus names enums by their assembly-qualified CLR type; this host registers the enum.
        { "System.Net.HttpStatusCode, System.Net.Primitives", false, "OK", "200" },
        // Elsa 3 kept an Object variable's text as it was, JSON or not.
        { "Object", false, """{"a":1}""", "\"{\\u0022a\\u0022:1}\"" },
        // Elsa 3 parsed text starting with { or [ into a structured type.
        { "Test.StringList", false, """["a","b"]""", """["a","b"]""" },
        { "Test.ObjectDictionary", false, """{"a":1}""", """{"a":1}""" },
        { "String", true, """["x"]""", """["x"]""" }
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void A_starting_value_is_the_value_Elsa_3_gave_it_as_a_literal(string typeName, bool isArray, string value, string expectedJson)
    {
        var source = new Elsa3Variable { Id = "v", Name = "v", TypeName = typeName, IsArray = isArray, Value = value };

        Assert.True(Elsa3VariableMapper.TryMap(Registry(), ObjectConverter, source, out var variable, out var failure), failure);

        var initial = Assert.IsType<Elsa.Expressions.Core.Models.ArgumentValue>(variable.Default);
        Assert.Equal("Literal", initial.ExpressionType);
        Assert.Equal(expectedJson, Assert.IsType<JsonElement>(initial.Value).GetRawText());
    }

    [Theory]
    // Elsa 3 started this one as 0, the type's default, without a warning; it is refused as an authoring mistake.
    [InlineData("Int32", "abc", "cannot be converted to its type 'Int32'")]
    // Elsa 3 logged a warning and started this one empty.
    [InlineData("Test.ObjectDictionary", "{bad", "cannot be converted to its type")]
    // Elsa 3's bare collection aliases are reserved in Elsa 4, so no host can register them.
    [InlineData("HttpHeaders", "{}", "is not known in Elsa 4")]
    // A CLR type the host registers no alias for runs as Object in Elsa 4.
    [InlineData("System.DayOfWeek", "Monday", "is not known in Elsa 4")]
    [InlineData("List<String>", """["a"]""", "is not known in Elsa 4")]
    [InlineData("ObjectDictionary", """{"a":1}""", "is not known in Elsa 4")]
    public void A_starting_value_that_cannot_start_as_in_Elsa_3_is_refused(string typeName, string value, string reason)
    {
        var source = new Elsa3Variable { Id = "v", Name = "count", TypeName = typeName, Value = value };

        Assert.False(Elsa3VariableMapper.TryMap(Registry(), ObjectConverter, source, out _, out var failure));
        Assert.Contains("Variable 'count'", failure);
        Assert.Contains(reason, failure);
    }

    [Fact]
    public async Task A_workflow_variable_whose_value_cannot_be_converted_refuses_the_definition()
    {
        var result = await ConvertAsync(
            new Elsa3Variable { Id = "name", Name = "name", TypeName = "String", Value = "ok" },
            new Elsa3Variable { Id = "count", Name = "count", TypeName = "Object" },
            new Elsa3Variable { Id = "headers", Name = "headers", TypeName = "HttpHeaders", Value = "{}" });

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.VariableValueUnconvertible, refusal.Code);
        Assert.Equal("$.variables[2]", refusal.Path);
        Assert.Equal(Elsa3VariableMapper.UnconvertibleGuidance, refusal.Guidance);
        Assert.Equal("headers", refusal.Metadata["VariableName"]);
    }

    [Fact]
    public async Task A_container_variable_whose_value_cannot_be_converted_refuses_the_definition()
    {
        var root = Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))));
        root.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "HttpHeaders", value = "{}" } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.VariableValueUnconvertible, refusal.Code);
        Assert.Contains("Variable 'local'", refusal.Message);
    }

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi")))), variables: variables));

    /// <summary>The conversion registry plus aliases a host can register: an enum and two collections.</summary>
    private static IWellKnownTypeRegistry Registry()
    {
        var registry = ConversionServices.TypeRegistry();
        registry.RegisterType(typeof(System.Net.HttpStatusCode), "System.Net.HttpStatusCode");
        registry.RegisterType(typeof(List<string>), "Test.StringList");
        registry.RegisterType(typeof(Dictionary<string, object>), "Test.ObjectDictionary");
        return registry;
    }
}
