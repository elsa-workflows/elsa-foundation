using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
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
        var model = JsonSerializer.Deserialize<VariableDefinition>(ref reader, newOptions)!;
        var variable = variableMapper.Map(model);

        return variable;
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