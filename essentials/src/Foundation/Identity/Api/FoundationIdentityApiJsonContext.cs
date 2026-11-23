using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Api.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.Api;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(IdentityBootstrapResponse))]
[JsonSerializable(typeof(IdentityCapabilitiesResponse))]
[JsonSerializable(typeof(AuthSession))]
[JsonSerializable(typeof(AccessTokenResponse))]
[JsonSerializable(typeof(TokenRefreshResult))]
[JsonSerializable(typeof(RefreshTokenRequest))]
internal partial class FoundationIdentityApiJsonContext : JsonSerializerContext
{
    internal static FoundationIdentityApiJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
