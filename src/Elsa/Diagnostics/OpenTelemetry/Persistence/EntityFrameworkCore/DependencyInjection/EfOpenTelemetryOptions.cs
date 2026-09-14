namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;

public class OpenTelemetryEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string TenantId { get; set; } = "default";
    public string ScopeId { get; set; } = "default";
    public string SourceId { get; set; } = "opentelemetry";
}

/// <summary>Compatibility name used by provider-host integrations.</summary>
public sealed class EfOpenTelemetryOptions : OpenTelemetryEntityFrameworkCoreOptions
{
}
