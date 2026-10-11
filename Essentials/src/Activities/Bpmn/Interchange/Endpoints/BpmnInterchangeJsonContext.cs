using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Workflows.Design.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AnalyzeBpmnDocumentRequest))]
[JsonSerializable(typeof(ImportBpmnDocumentRequest))]
[JsonSerializable(typeof(ExportBpmnDocumentRequest))]
[JsonSerializable(typeof(BpmnImportAnalysis))]
[JsonSerializable(typeof(BpmnImportResult))]
[JsonSerializable(typeof(ExportBpmnDocumentResult))]
[JsonSerializable(typeof(BpmnInterchangeError))]
internal partial class BpmnInterchangeJsonContext : JsonSerializerContext
{
}
