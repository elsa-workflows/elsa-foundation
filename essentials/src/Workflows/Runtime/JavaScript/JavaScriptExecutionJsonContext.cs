using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.JavaScript;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RequestModel))]
[JsonSerializable(typeof(JavaScriptExecutionErrorResponse))]
[JsonSerializable(typeof(JavaScriptExecutionProblemDetailsResponse))]
[JsonSerializable(typeof(JavaScriptExecutionProblemError))]
[JsonSerializable(typeof(JavaScriptExecutionSuccessResponse))]
[JsonSerializable(typeof(JavaScriptExecutionFailureResponse))]
internal partial class JavaScriptExecutionJsonContext : JsonSerializerContext
{
    internal static JavaScriptExecutionJsonContext WireContext { get; } = new(CreateWireOptions());

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }
}
