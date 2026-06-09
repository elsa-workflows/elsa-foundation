using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Server.Constants;

internal static class JsonSerializerOptionsCache
{
    internal static readonly JsonSerializerOptions Instance = Create();


    private static JsonSerializerOptions Create()
    {
        var result = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        result.Converters.Add(new JsonStringEnumConverter());

        return result;
    }
}