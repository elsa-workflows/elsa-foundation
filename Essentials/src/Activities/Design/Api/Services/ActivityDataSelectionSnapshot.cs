using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Request identity over the owner's existing serialized state, not a value-serialization profile.</summary>
internal static class ActivityDataSelectionSnapshot
{
    internal static string Hash<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, ActivitiesDesignJsonOptions.Create());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, element);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    internal static ActivityDefinitionDraftState Read(JsonElement element)
    {
        var options = ActivitiesDesignJsonOptions.Create();
        options.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
        options.RespectRequiredConstructorParameters = true;
        var state = element.Deserialize<ActivityDefinitionDraftState>(options) ?? throw new JsonException();
        if (state.Contract is null || state.Provider is null || state.Options is null ||
            state.Contract.Inputs is null || state.Contract.Outputs is null || state.Contract.Outcomes is null)
            throw new JsonException();
        return state;
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) Write(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
