using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.JavaScript.Rendering;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JavaScriptRenderingSuccessResponse))]
[JsonSerializable(typeof(JavaScriptRenderingFailureResponse))]
internal partial class JavaScriptRenderingJsonContext : JsonSerializerContext
{
}
