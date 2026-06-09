using Elsa.Serialization.Core;
using System.Text.Json.Nodes;

namespace Elsa.Serialization.SystemText.Services;

public sealed class SystemTextJsonIslandTypeHandler : IJsonIslandTypeHandler
{
    public bool CanHandle(Type type) => type == typeof(JsonObject) || type == typeof(JsonArray);

    public object Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JsonObject();

        return JsonNode.Parse(json)!;
    }

    public string Write(object value) => value.ToString()!;
}
