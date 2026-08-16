using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(IdentityBootstrapResponse))]
[JsonSerializable(typeof(IdentityCapabilitiesResponse))]
[JsonSerializable(typeof(AuthSession))]
[JsonSerializable(typeof(AccessTokenResponse))]
[JsonSerializable(typeof(TokenRefreshResult))]
internal partial class FoundationIdentityApiJsonContext : JsonSerializerContext
{
}
