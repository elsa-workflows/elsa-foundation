using Elsa.Studio.Preferences.Api.Models;
using Elsa.Studio.Preferences.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Studio.Preferences.Api;

/// <summary>
/// The owner's source-generated serializer context handed to the module endpoint group. The
/// operations currently perform their own serialization over the host's HTTP JSON options, so this
/// context only mirrors that shape for the group.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(StudioPreferenceDocument))]
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
