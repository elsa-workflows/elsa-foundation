using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workbench.Constants;

internal static class JsonSerializerOptionsCache
{
    internal static readonly JsonSerializerOptions Instance = Create();

    private static JsonSerializerOptions Create()
    {
        var result = new JsonSerializerOptions(Elsa.Serialization.Core.JsonBaseline.Options) {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        result.Converters.Add(new JsonStringEnumConverter());
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(result, apiMode: true);

        return result;
    }
}