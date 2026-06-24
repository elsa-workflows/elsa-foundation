namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;

public sealed class PersistedMetricInstrument
{
    public string Id { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? Description { get; set; }
    public int Kind { get; set; }
    public string? AttributesJson { get; set; }
}
