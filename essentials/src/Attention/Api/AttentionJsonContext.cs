using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Attention.Core;

namespace Elsa.Attention.Api;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AttentionAggregationResult))]
internal partial class AttentionJsonContext : JsonSerializerContext
{
    internal static AttentionJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
