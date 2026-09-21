using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JsonConverters;
using Elsa.Expressions.Models;
using Elsa.Expressions.Services;
using Elsa.Serialization.Core.Exceptions;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// FR-018 / SC-005 (research D7): an unregistered alias is graceful — the persisted definition round-trips the
/// alias string verbatim and the mapper resolves it to <c>object</c> without throwing. T037: a malformed
/// argument payload surfaces a domain exception, not a raw <see cref="JsonException"/>.
/// </summary>
public sealed class UnknownAliasRoundTripTests
{
    private readonly JsonSerializerOptions _options;

    public UnknownAliasRoundTripTests()
    {
        var registry = TestRegistry.SeededWithPrimitives();
        var objectConverter = new ObjectConverter(new ServiceCollection().BuildServiceProvider());
        IVariableMapper mapper = new VariableMapper(registry, objectConverter, new VariableDefaultValueFormatter(), NullLogger<VariableMapper>.Instance);

        // Mirror the production payload serializer: camelCase + string enum names (CollectionKind serializes by name).
        _options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        _options.Converters.Add(new VariableConverter(mapper));
    }

    private static readonly JsonSerializerOptions PlainOptions = BuildPlainOptions();

    private static JsonSerializerOptions BuildPlainOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    [Fact]
    public void UnknownAlias_RoundTripsAliasVerbatim_AndResolvesToObject()
    {
        const string json = """{"name":"Mystery","type":{"alias":"Acme.Not.Registered","collectionKind":"Single"}}""";

        // Deserialize through the mapper: the unknown alias resolves to object without throwing.
        var variable = JsonSerializer.Deserialize<IVariable>(json, _options)!;
        Assert.Equal(typeof(object), variable.GetType().GetGenericArguments()[0]);

        // The persisted definition carries the alias as a plain string, so it round-trips verbatim.
        var definition = JsonSerializer.Deserialize<VariableDefinition>(json, PlainOptions)!;
        Assert.Equal("Acme.Not.Registered", definition.Type.Alias);

        var reserialized = JsonSerializer.Serialize(definition, PlainOptions);
        Assert.Contains("Acme.Not.Registered", reserialized);
    }

    [Fact]
    public void MalformedPayload_ThrowsDomainException_NotRawJsonException()
    {
        const string malformed = """{"name":"Broken","type":{"alias":"String","collectionKind":"#$ not valid #$"}}""";

        var exception = Assert.Throws<ArgumentDefinitionDeserializationException>(
            () => JsonSerializer.Deserialize<IVariable>(malformed, _options));

        Assert.IsType<JsonException>(exception.InnerException);
    }
}
