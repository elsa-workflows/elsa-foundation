using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.JavaScript.Rendering;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JavaScriptRenderingSuccessResponse))]
[JsonSerializable(typeof(JavaScriptRenderingFailureResponse))]
internal partial class JavaScriptRenderingJsonContext : JsonSerializerContext
{
    internal static JavaScriptRenderingJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
