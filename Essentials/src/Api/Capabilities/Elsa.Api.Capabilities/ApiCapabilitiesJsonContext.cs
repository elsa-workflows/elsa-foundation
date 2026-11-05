using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.Capabilities.Models;

namespace Elsa.Api.Capabilities;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ApiCapabilitiesDocument))]
internal partial class ApiCapabilitiesJsonContext : JsonSerializerContext
{
}
