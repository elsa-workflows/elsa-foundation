using Elsa.Workflows.ExecutionEvidence.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.ExecutionEvidence.Endpoints;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ExecutionEvidencePage))]
internal partial class ExecutionEvidenceJsonContext : JsonSerializerContext
{
}
