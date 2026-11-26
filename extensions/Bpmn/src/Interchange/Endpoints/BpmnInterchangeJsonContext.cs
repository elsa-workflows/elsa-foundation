using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Workflows.Design.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AnalyzeBpmnDocumentRequest))]
[JsonSerializable(typeof(ImportBpmnDocumentRequest))]
[JsonSerializable(typeof(ExportBpmnDocumentRequest))]
[JsonSerializable(typeof(BpmnImportAnalysis))]
[JsonSerializable(typeof(BpmnImportResult))]
[JsonSerializable(typeof(ExportBpmnDocumentResult))]
[JsonSerializable(typeof(BpmnDocumentAuthoringCapabilities))]
[JsonSerializable(typeof(BpmnInterchangeError))]
internal partial class BpmnInterchangeJsonContext : JsonSerializerContext
{
    internal static BpmnInterchangeJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
