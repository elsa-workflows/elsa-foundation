using System.Text.Json;

namespace Elsa.Integrations.Connectors.Slack;

internal static class SlackJson
{
    public static readonly JsonSerializerOptions Options = new(Elsa.Serialization.Core.JsonBaseline.Options)
    {
        PropertyNameCaseInsensitive = true
    };

    public static T? Deserialize<T>(JsonElement element) => element.Deserialize<T>(Options);
}
