using Elsa.Foundation.Identity.Abstractions.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AuthSession))]
internal partial class AspNetCoreIdentityJsonContext : JsonSerializerContext
{
}
