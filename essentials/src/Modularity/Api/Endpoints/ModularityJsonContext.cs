using Elsa.Modularity.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Modularity.Api.Endpoints;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FeatureCatalogResponse))]
[JsonSerializable(typeof(FeatureApplyRequest))]
[JsonSerializable(typeof(FeatureApplyResult))]
[JsonSerializable(typeof(ModularityError))]
internal partial class ModularityJsonContext : JsonSerializerContext
{
}
