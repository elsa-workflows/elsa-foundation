using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Etl.Api;

internal sealed record EtlProblem(int Status, string Code);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(EtlProblem))]
internal partial class EtlProblemJsonContext : JsonSerializerContext;
