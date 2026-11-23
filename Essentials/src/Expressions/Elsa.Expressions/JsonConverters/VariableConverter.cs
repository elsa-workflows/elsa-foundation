using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Exceptions;
using Elsa.Expressions.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.JsonConverters;

/// <summary>
/// Serializes <see cref="Type"/> objects to a simple alias representing said type.
/// </summary>
/// <inheritdoc />
internal sealed class VariableConverter(IVariableMapper variableMapper) : JsonConverter<IVariable>
{
    /// <inheritdoc />
    public override IVariable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        VariableDefinition model;
        try
        {
            model = JsonSerializer.Deserialize<VariableDefinition>(ref reader, options)!;
        }
        catch (JsonException e)
        {
            // Wrap the raw infrastructure exception at the authored-argument boundary so callers see a
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

}
