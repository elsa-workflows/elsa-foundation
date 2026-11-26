using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Attention.Core;

namespace Elsa.Attention.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AttentionAggregationResult))]
internal partial class AttentionJsonContext : JsonSerializerContext
{
}
