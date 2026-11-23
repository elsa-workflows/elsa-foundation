using Elsa.Studio.Preferences.Api.Models;
using Elsa.Studio.Preferences.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Studio.Preferences.Api;

/// <summary>
/// The owner's source-generated metadata and explicit request/response serialization profile.
/// Raw operations use this context independently of the host's HTTP JSON options.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(StudioPreferenceDocument))]
[JsonSerializable(typeof(PutStudioPreferenceRequest))]
[JsonSerializable(typeof(HomeLayoutDocument))]
[JsonSerializable(typeof(PutHomeLayoutRequest))]
internal partial class StudioPreferencesJsonContext : JsonSerializerContext
{
    internal static StudioPreferencesJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
