using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Dashboard;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkflowPortfolioSnapshot))]
[JsonSerializable(typeof(WorkflowRunHealthSnapshot))]
internal partial class WorkflowsDashboardJsonContext : JsonSerializerContext
{
    internal static WorkflowsDashboardJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
