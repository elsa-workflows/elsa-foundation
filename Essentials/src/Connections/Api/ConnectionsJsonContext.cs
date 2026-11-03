using Elsa.Connections.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Connections.Api;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
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
internal partial class ConnectionsJsonContext : JsonSerializerContext;
