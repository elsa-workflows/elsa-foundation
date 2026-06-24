using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Secrets.Persistence.Groundwork;

internal static class SecretsGroundworkJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
