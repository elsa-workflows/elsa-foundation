using Elsa.Connections.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Connections.Api;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ConnectionMetadata))]
[JsonSerializable(typeof(ListConnectionsResponse))]
[JsonSerializable(typeof(ConnectionDescriptorsResponse))]
[JsonSerializable(typeof(ConnectionPickerRequest))]
[JsonSerializable(typeof(ConnectionPickerResponse))]
[JsonSerializable(typeof(CreateConnectionRequest))]
[JsonSerializable(typeof(UpdateConnectionRequest))]
[JsonSerializable(typeof(ConnectionTestResult))]
[JsonSerializable(typeof(OAuthRedirectUriResponse))]
[JsonSerializable(typeof(OAuthAuthorizeUrlResponse))]
internal partial class ConnectionsJsonContext : JsonSerializerContext
{
    internal static ConnectionsJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

}
