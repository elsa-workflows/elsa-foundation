namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>Legacy name retained for source-level host configuration while routing only to v2 units.</summary>
public sealed class GroundworkOpenTelemetryBinding
{
    private GroundworkOpenTelemetryBinding(string tenantId, string scopeId, string sourceId)
    {
        TenantId = tenantId;
        ScopeId = scopeId;
        SourceId = sourceId;
        var route = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{tenantId.Length}:{tenantId}{scopeId.Length}:{scopeId}{sourceId.Length}:{sourceId}")))[..32];
        TraceStreamId = $"otel:{route}:traces";
        SpanStreamId = $"otel:{route}:spans";
        MetricPointStreamId = $"otel:{route}:metric-points";
        LogStreamId = $"otel:{route}:logs";
    }

    public static GroundworkOpenTelemetryBinding Default { get; } = Create("default", "default", "open-telemetry");
    public string TenantId { get; }
    public string ScopeId { get; }
    public string SourceId { get; }
    public string TraceStreamId { get; }
    public string SpanStreamId { get; }
    public string MetricPointStreamId { get; }
    public string LogStreamId { get; }

    public static GroundworkOpenTelemetryBinding Create(string tenantId, string scopeId, string sourceId)
    {
        Validate(tenantId, nameof(tenantId));
        Validate(scopeId, nameof(scopeId));
        Validate(sourceId, nameof(sourceId));
        return new(tenantId, scopeId, sourceId);
    }

    internal V2OpenTelemetryBinding ToV2Binding() => new(TenantId, ScopeId, SourceId);

    internal void ValidateAll()
    {
        Validate(TenantId, nameof(TenantId));
        Validate(ScopeId, nameof(ScopeId));
        Validate(SourceId, nameof(SourceId));
    }

    private static void Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 64 || value.Any(character => character is < '!' or > '~'))
            throw new ArgumentException("Groundwork OpenTelemetry binding values must be printable ASCII and bounded to 64 characters.", parameterName);
    }
}
