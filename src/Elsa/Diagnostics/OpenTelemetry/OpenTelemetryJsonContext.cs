using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Diagnostics.OpenTelemetry;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OpenTelemetryResourceFilter))]
[JsonSerializable(typeof(OpenTelemetryTraceFilter))]
[JsonSerializable(typeof(OpenTelemetryMetricFilter))]
[JsonSerializable(typeof(OpenTelemetryLogFilter))]
[JsonSerializable(typeof(OpenTelemetryResourceResult))]
[JsonSerializable(typeof(OpenTelemetryTraceResult))]
[JsonSerializable(typeof(OpenTelemetryTraceDetail))]
[JsonSerializable(typeof(OpenTelemetryMetricResult))]
[JsonSerializable(typeof(OpenTelemetryLogResult))]
[JsonSerializable(typeof(OpenTelemetryStorageDiagnostics))]
[JsonSerializable(typeof(CollectorConfiguration))]
internal partial class OpenTelemetryJsonContext : JsonSerializerContext
{
}
