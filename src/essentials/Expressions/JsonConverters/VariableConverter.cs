using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core.Exceptions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.JsonConverters;

/// <summary>
/// Serializes <see cref="Type"/> objects to a simple alias representing said type.
/// </summary>
/// <inheritdoc />
public sealed class VariableConverter(IVariableMapper variableMapper) : JsonConverter<IVariable>
{
    private JsonSerializerOptions? _options;

    /// <inheritdoc />
    public override IVariable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var newOptions = GetClonedOptions(options);

        VariableDefinition model;
        try
        {
            model = JsonSerializer.Deserialize<VariableDefinition>(ref reader, newOptions)!;
        }
        catch (JsonException e)
        {
            // Wrap the raw infrastructure exception at the authored-argument boundary (§2.23.5) so callers see a
            // domain exception instead of System.Text.Json internals; preserve the original cause.
            throw new ArgumentDefinitionDeserializationException("Failed to deserialize the authored variable definition.", e);
        }

        return variableMapper.Map(model);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IVariable value, JsonSerializerOptions options)
    {
        var model = variableMapper.Map(value);
        JsonSerializer.Serialize(writer, model, options);
    }

    private JsonSerializerOptions GetClonedOptions(JsonSerializerOptions options)
    {
        if (_options != null)
            return _options;

        var newOptions = new JsonSerializerOptions(options);
        var jsonPrimitiveToStringConverter = new JsonPrimitiveToStringConverter();
        newOptions.Converters.Add(jsonPrimitiveToStringConverter);
        _options = newOptions;
        return newOptions;
    }
}