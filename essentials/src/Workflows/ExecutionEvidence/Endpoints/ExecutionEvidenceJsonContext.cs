using Elsa.Workflows.ExecutionEvidence.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.ExecutionEvidence.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ExecutionEvidencePage))]
internal partial class ExecutionEvidenceJsonContext : JsonSerializerContext
{
    internal static ExecutionEvidenceJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
