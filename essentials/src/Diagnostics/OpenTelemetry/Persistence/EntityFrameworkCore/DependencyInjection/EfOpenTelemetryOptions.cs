using Elsa.Persistence.EntityFramework;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;

public class OpenTelemetryEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Optional database schema for this module's tables and its own migrations history table. Falls back to
    /// <see cref="EfSchema.ConfigurationKey"/>, then to the provider's own default. Ignored on SQLite and refused
    /// on MySQL, where a schema is a database.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per scope.</summary>
    public bool Pooling { get; set; }
    public string TenantId { get; set; } = "default";
    public string ScopeId { get; set; } = "default";
    public string SourceId { get; set; } = "opentelemetry";
}

/// <summary>Compatibility name used by provider-host integrations.</summary>
public sealed class EfOpenTelemetryOptions : OpenTelemetryEntityFrameworkCoreOptions
{
}
