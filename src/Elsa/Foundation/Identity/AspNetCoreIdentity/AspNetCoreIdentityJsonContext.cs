using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AuthSession))]
[JsonSerializable(typeof(LoginRequest))]
internal partial class AspNetCoreIdentityJsonContext : JsonSerializerContext
{
}
