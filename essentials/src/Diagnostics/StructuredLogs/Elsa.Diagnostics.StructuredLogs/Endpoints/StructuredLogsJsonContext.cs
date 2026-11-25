using Elsa.Diagnostics.StructuredLogs.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StructuredLogEntry))]
[JsonSerializable(typeof(StructuredLogEntry[]))]
[JsonSerializable(typeof(DroppedEntriesSignal))]
internal sealed partial class StructuredLogsJsonContext : JsonSerializerContext
{
    internal static StructuredLogsJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

}
