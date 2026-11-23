using Elsa.Modularity.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Modularity.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FeatureCatalogResponse))]
[JsonSerializable(typeof(FeatureApplyRequest))]
[JsonSerializable(typeof(FeatureApplyResult))]
[JsonSerializable(typeof(ModularityError))]
internal partial class ModularityJsonContext : JsonSerializerContext
{
    internal static ModularityJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
