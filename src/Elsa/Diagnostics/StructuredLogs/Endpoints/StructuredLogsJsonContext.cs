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
internal sealed partial class StructuredLogsJsonContext : JsonSerializerContext;
