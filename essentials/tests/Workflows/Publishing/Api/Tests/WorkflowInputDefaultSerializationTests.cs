using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowInputDefaultSerializationTests : IDisposable
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private readonly ServiceProvider _services = CreateServices();

    public static IEnumerable<object?[]> DefaultCases()
    {
        foreach (var serializer in new[] { "Payload", "Web", "Metadata" })
        foreach (var literal in new[] { null, "null", "false", "0", "\"\"", "42", "\"ready\"", "[1,null]", "{\"value\":null}" })
            yield return [serializer, literal];
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void Input_definition_preserves_default_presence_and_literal(string serializer, string? literal)
    {
        var json = """
                   {"referenceKey":"value","name":"value","type":{"alias":"String"},"displayName":"Value","isNullable":false,"isRequired":false
                   """ + DefaultField(literal) + (literal == "null" ? ",\"defaultSyntax\":\"Literal\"" : "") + "}";

        var input = Deserialize<InputDefinition>(json, serializer);

        Assert.False(input.IsNullable);
        Assert.False(input.IsRequired);
        AssertDefault(input.DefaultValue, literal);
        var serialized = Serialize(input, serializer);
        AssertDefaultField(serialized, literal);
        var restored = Deserialize<InputDefinition>(serialized, serializer);
        Assert.False(restored.IsNullable);
        Assert.False(restored.IsRequired);
        AssertDefault(restored.DefaultValue, literal);
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void Declared_input_preserves_default_presence_and_literal(string serializer, string? literal)
    {
        var input = Deserialize<WorkflowDeclaredInput>(DeclaredInputJson(literal), serializer);

        Assert.False(input.IsNullable);
        Assert.False(input.IsRequired);
        AssertDefault(input.DefaultValue, literal);
        var serialized = Serialize(input, serializer);
        AssertDeclaredDefaultField(serialized, literal);
        var restored = Deserialize<WorkflowDeclaredInput>(serialized, serializer);
        Assert.False(restored.IsNullable);
        Assert.False(restored.IsRequired);
        AssertDefault(restored.DefaultValue, literal);
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void Executable_contract_preserves_nested_default_presence_and_literal(string serializer, string? literal)
    {
        var json = "{\"version\":1,\"inputs\":[" + DeclaredInputJson(literal) + "]}";

        var contract = Deserialize<WorkflowExecutableInputContract>(json, serializer);

        var input = Assert.Single(contract.Inputs);
        Assert.False(input.IsNullable);
        Assert.False(input.IsRequired);
        AssertDefault(input.DefaultValue, literal);
        var serialized = Serialize(contract, serializer);
        using var document = JsonDocument.Parse(serialized);
        AssertDeclaredDefaultField(document.RootElement.GetProperty("inputs")[0].GetRawText(), literal);
        var restored = Deserialize<WorkflowExecutableInputContract>(serialized, serializer);
        Assert.Equal(WorkflowExecutableInputContract.CurrentVersion, restored.Version);
        AssertDefault(Assert.Single(restored.Inputs).DefaultValue, literal);
    }

    [Theory]
    [InlineData("Payload", false)]
    [InlineData("Payload", true)]
    [InlineData("Web", false)]
    [InlineData("Web", true)]
    [InlineData("Metadata", false)]
    [InlineData("Metadata", true)]
    public void Legacy_input_definition_null_without_default_syntax_remains_absent(string serializer, bool includeSyntax)
    {
        var json = """
                   {"referenceKey":"value","name":"value","type":{"alias":"String"},"displayName":"Value","isNullable":false,"isRequired":false,"defaultValue":null
                   """ + (includeSyntax ? ",\"defaultSyntax\":null" : "") + "}";

        var input = Deserialize<InputDefinition>(json, serializer);

        Assert.Null(input.DefaultValue);
        Assert.False(input.IsRequired);
        Assert.False(input.IsNullable);
        var serialized = Serialize(input, serializer);
        AssertDefaultField(serialized, null);
        Assert.Null(Deserialize<InputDefinition>(serialized, serializer).DefaultValue);
    }

    [Theory]
    [InlineData("Payload", false)]
    [InlineData("Payload", true)]
    [InlineData("Web", false)]
    [InlineData("Web", true)]
    [InlineData("Metadata", false)]
    [InlineData("Metadata", true)]
    public void Legacy_declared_null_without_presence_marker_remains_absent(string serializer, bool includeFalseMarker)
    {
        var json = """{"name":"value","type":{"alias":"String"},"isRequired":false,"defaultValue":null"""
                   + (includeFalseMarker ? ",\"hasDefaultValue\":false" : "") + "}";
        var input = Deserialize<WorkflowDeclaredInput>(json, serializer);
        var contract = Deserialize<WorkflowExecutableInputContract>("{\"version\":1,\"inputs\":[" + json + "]}", serializer);

        foreach (var declaration in new[] { input, Assert.Single(contract.Inputs) })
        {
            Assert.Null(declaration.DefaultValue);
            Assert.False(declaration.IsNullable);
            Assert.False(declaration.IsRequired);
            var serialized = Serialize(declaration, serializer);
            AssertDeclaredDefaultField(serialized, null);
            Assert.Null(Deserialize<WorkflowDeclaredInput>(serialized, serializer).DefaultValue);
        }

        var restored = Deserialize<WorkflowExecutableInputContract>(Serialize(contract, serializer), serializer);
        Assert.Equal(WorkflowExecutableInputContract.CurrentVersion, restored.Version);
        Assert.Null(Assert.Single(restored.Inputs).DefaultValue);
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void Public_constructors_preserve_defaults_through_serialization(string serializer, string? literal)
    {
        var value = literal is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(literal);
        var definition = new InputDefinition("value", "value", new TypeReference("String"), null, "Value", null,
            IsNullable: false, DefaultValue: value, DefaultSyntax: literal is null ? null : "Literal");

        AssertDefault(definition.DefaultValue, literal);
        AssertDefault(Deserialize<InputDefinition>(Serialize(definition, serializer), serializer).DefaultValue, literal);

        foreach (var declaration in new[]
                 {
                     new WorkflowDeclaredInput("value", new TypeReference("String"), false, value),
                     new WorkflowDeclaredInput("value", new TypeReference("String"), false, value, isNullable: true)
                 })
        {
            AssertDefault(declaration.DefaultValue, literal);
            var serialized = Serialize(declaration, serializer);
            AssertDeclaredDefaultField(serialized, literal);
            var restored = Deserialize<WorkflowDeclaredInput>(serialized, serializer);
            AssertDefault(restored.DefaultValue, literal);
            Assert.Equal(declaration.IsNullable, restored.IsNullable);
        }
    }

    [Theory]
    [InlineData("Payload")]
    [InlineData("Web")]
    [InlineData("Metadata")]
    public void Input_definition_still_requires_an_explicit_nullability_flag(string serializer)
    {
        const string json = """{"referenceKey":"value","name":"value","type":{"alias":"String"},"displayName":"Value"}""";

        Assert.Throws<JsonException>(() => Deserialize<InputDefinition>(json, serializer));
    }

    [Theory]
    [InlineData("Payload")]
    [InlineData("Web")]
    [InlineData("Metadata")]
    public void Explicit_nullability_flags_round_trip(string serializer)
    {
        var definition = Deserialize<InputDefinition>(
            """{"referenceKey":"value","name":"value","type":{"alias":"String"},"displayName":"Value","isNullable":true,"isRequired":false}""",
            serializer);
        var declaration = Deserialize<WorkflowDeclaredInput>(
            """{"name":"value","type":{"alias":"String"},"isNullable":true,"isRequired":false}""",
            serializer);

        Assert.True(definition.IsNullable);
        Assert.True(declaration.IsNullable);
        Assert.True(Deserialize<InputDefinition>(Serialize(definition, serializer), serializer).IsNullable);
        Assert.True(Deserialize<WorkflowDeclaredInput>(Serialize(declaration, serializer), serializer).IsNullable);
    }

    private static string DeclaredInputJson(string? literal) =>
        """{"name":"value","type":{"alias":"String"},"isRequired":false""" + DefaultField(literal)
        + (literal == "null" ? ",\"hasDefaultValue\":true" : "") + "}";

    private static string DefaultField(string? literal) => literal is null ? "" : ",\"defaultValue\":" + literal;

    private static void AssertDefault(JsonElement? value, string? literal)
    {
        if (literal is null)
        {
            Assert.Null(value);
            return;
        }

        Assert.True(value.HasValue, $"The JSON literal {literal} must remain a present default.");
        using var expected = JsonDocument.Parse(literal);
        Assert.Equal(expected.RootElement.ValueKind, value.Value.ValueKind);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, value.Value));
    }

    private static void AssertDefaultField(string json, string? literal)
    {
        using var document = JsonDocument.Parse(json);
        var present = document.RootElement.TryGetProperty("defaultValue", out var value);
        Assert.Equal(literal is not null, present);
        if (present)
            AssertDefault(value, literal);
    }

    private static void AssertDeclaredDefaultField(string json, string? literal)
    {
        AssertDefaultField(json, literal);
        using var document = JsonDocument.Parse(json);
        var hasMarker = document.RootElement.TryGetProperty("hasDefaultValue", out var marker);
        Assert.Equal(literal == "null", hasMarker);
        if (hasMarker)
            Assert.True(marker.GetBoolean());
    }

    private string Serialize<T>(T value, string serializer) where T : notnull => serializer switch
    {
        "Payload" => _services.GetRequiredService<IPayloadSerializer>().Serialize(value),
        "Web" => JsonSerializer.Serialize(value, WebOptions),
        "Metadata" => JsonSerializer.Serialize(value, TypeInfo<T>()),
        _ => throw new ArgumentOutOfRangeException(nameof(serializer))
    };

    private T Deserialize<T>(string json, string serializer) => serializer switch
    {
        "Payload" => _services.GetRequiredService<IPayloadSerializer>().Deserialize<T>(json),
        "Web" => JsonSerializer.Deserialize<T>(json, WebOptions)!,
        "Metadata" => JsonSerializer.Deserialize(json, TypeInfo<T>())!,
        _ => throw new ArgumentOutOfRangeException(nameof(serializer))
    };

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        (JsonTypeInfo<T>)WorkflowInputDefaultJsonContext.Default.GetTypeInfo(typeof(T))!;

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new SerializationFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(InputDefinition))]
[JsonSerializable(typeof(WorkflowDeclaredInput))]
[JsonSerializable(typeof(WorkflowExecutableInputContract))]
internal partial class WorkflowInputDefaultJsonContext : JsonSerializerContext;