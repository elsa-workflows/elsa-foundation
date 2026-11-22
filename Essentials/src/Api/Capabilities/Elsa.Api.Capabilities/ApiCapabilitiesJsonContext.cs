using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.Capabilities.Models;

namespace Elsa.Api.Capabilities;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ApiCapabilitiesDocument))]
internal partial class ApiCapabilitiesJsonContext : JsonSerializerContext
{
    internal static ApiCapabilitiesJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
