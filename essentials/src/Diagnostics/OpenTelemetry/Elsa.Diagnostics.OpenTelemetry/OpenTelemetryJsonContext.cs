using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Diagnostics.OpenTelemetry;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
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
[JsonSerializable(typeof(OpenTelemetryBindingProblemDetails))]
[JsonSerializable(typeof(TelemetryResource))]
[JsonSerializable(typeof(TelemetryTrace))]
[JsonSerializable(typeof(MetricPoint))]
[JsonSerializable(typeof(OtlpLogRecord))]
[JsonSerializable(typeof(OpenTelemetryDroppedItemSummary))]
internal partial class OpenTelemetryJsonContext : JsonSerializerContext
{
}
