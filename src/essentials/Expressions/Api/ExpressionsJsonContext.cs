using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Api.Models;

namespace Elsa.Expressions.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ExpressionDescriptorsResponse))]
[JsonSerializable(typeof(VariableTypeDescriptorsResponse))]
internal partial class ExpressionsJsonContext : JsonSerializerContext
{
}
