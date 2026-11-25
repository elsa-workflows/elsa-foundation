using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AuthSession))]
[JsonSerializable(typeof(LoginRequest))]
internal partial class AspNetCoreIdentityJsonContext : JsonSerializerContext
{
    internal static AspNetCoreIdentityJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
