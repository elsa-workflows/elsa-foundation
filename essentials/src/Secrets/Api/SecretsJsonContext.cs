using Elsa.Secrets.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Secrets.Api;

/// <summary>
/// The owner's source-generated serializer context handed to the module endpoint group. The
/// operations currently perform their own serialization over the module's runtime-configured
/// options (camel-cased string enums), so this context only mirrors that shape for the group.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SecretMetadata))]
internal partial class SecretsJsonContext : JsonSerializerContext
{
    internal static SecretsJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

}
