using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Api.Models;

namespace Elsa.Expressions.Api;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ExpressionDescriptorsResponse))]
[JsonSerializable(typeof(VariableTypeDescriptorsResponse))]
internal partial class ExpressionsJsonContext : JsonSerializerContext
{
    internal static ExpressionsJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
