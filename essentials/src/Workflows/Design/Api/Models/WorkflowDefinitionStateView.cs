using System.Text.Json;
using System.Text.Json.Serialization;
﻿using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionStateView(
    IEnumerable<VariableDefinition>? Variables = null,
    ActivityNode? RootActivity = null,
    [property: JsonConverter(typeof(AuthoredWorkflowMemberListJsonConverter<InputDefinition>))]
    IEnumerable<InputDefinition>? Inputs = null,
    [property: JsonConverter(typeof(AuthoredWorkflowMemberListJsonConverter<OutputDefinition>))]
    IEnumerable<OutputDefinition>? Outputs = null,
    WorkflowStrategyOptions? StrategyOptions = null
);

// The API projection shares descriptors with CLR catalogs, whose nullable metadata remains meaningful.
// Omit that metadata only from authored workflow members, matching WorkflowDefinitionState's wire.
internal sealed class AuthoredWorkflowMemberListJsonConverter<T> : JsonConverter<IEnumerable<T>>
{
    public AuthoredWorkflowMemberListJsonConverter() { }
    public override IEnumerable<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<List<T>>(ref reader, options);

    public override void Write(Utf8JsonWriter writer, IEnumerable<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var member in value)
        {
            writer.WriteStartObject();
            foreach (var property in JsonSerializer.SerializeToElement(member, options).EnumerateObject())
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, "isNullable")) continue;
                writer.WritePropertyName(property.Name);
                // Re-enter the caller's element converter so carried defaults and UI metadata keep their raw tokens.
                JsonSerializer.Serialize(writer, property.Value, options);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
