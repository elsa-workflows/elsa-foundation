using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Diagnostics.OpenTelemetry;

[JsonSourceGenerationOptions(
    UseStringEnumConverter = true,
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
    internal static OpenTelemetryJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

}
