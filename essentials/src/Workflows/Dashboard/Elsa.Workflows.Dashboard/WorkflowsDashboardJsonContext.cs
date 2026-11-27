using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Dashboard;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkflowPortfolioSnapshot))]
[JsonSerializable(typeof(WorkflowRunHealthSnapshot))]
internal partial class WorkflowsDashboardJsonContext : JsonSerializerContext
{
}
