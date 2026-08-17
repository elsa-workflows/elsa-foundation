using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.JavaScript;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RequestModel))]
[JsonSerializable(typeof(JavaScriptExecutionErrorResponse))]
[JsonSerializable(typeof(JavaScriptExecutionProblemDetailsResponse))]
[JsonSerializable(typeof(JavaScriptExecutionProblemError))]
[JsonSerializable(typeof(JavaScriptExecutionSuccessResponse))]
[JsonSerializable(typeof(JavaScriptExecutionFailureResponse))]
internal partial class JavaScriptExecutionJsonContext : JsonSerializerContext
{
}
