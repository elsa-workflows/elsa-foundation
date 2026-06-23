namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;

/// <summary>
/// Durable row form of a telemetry resource. Deliberately not derived from the shared Entity base: this is
/// a high-volume diagnostics table with its own OTEL resource identifier and no command/query entity events.
/// </summary>
public sealed class PersistedTelemetryResource
{
    public string Id { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string? ServiceInstanceId { get; set; }
    public string? TelemetrySdkLanguage { get; set; }
    public string? AttributesJson { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int Status { get; set; }
}
