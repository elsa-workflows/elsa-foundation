using System.Security.Cryptography;
using System.Text;
using Groundwork.Core.Scoping;
using Groundwork.DiagnosticRecords;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Explicit persistence routing for one OpenTelemetry source. Core contracts remain provider-neutral; this
/// Groundwork leaf owns the translation to diagnostic scopes, streams, and a scoped document partition.
/// </summary>
public sealed record GroundworkOpenTelemetryBinding(
    string TenantId,
    string ScopeId,
    string SourceId,
    string TraceStreamId,
    string SpanStreamId,
    string MetricPointStreamId,
    string LogStreamId)
{
    /// <summary>Creates a binding whose stream names are deterministically derived from the source identity.</summary>
    public static GroundworkOpenTelemetryBinding Create(string tenantId, string scopeId, string sourceId)
    {
        Validate(tenantId, nameof(tenantId));
        Validate(scopeId, nameof(scopeId));
        Validate(sourceId, nameof(sourceId));
        var prefix = $"open-telemetry:{sourceId}";
        return new(tenantId, scopeId, sourceId, $"{prefix}:traces", $"{prefix}:spans", $"{prefix}:metric-points", $"{prefix}:logs");
    }

    internal DiagnosticStorageScope DiagnosticScope
    {
        get
        {
            ValidateAll();
            return new(TenantId, ScopeId);
        }
    }

    /// <summary>The Groundwork document partition derived from tenant, scope, and source.</summary>
    public StorageScope DocumentStorageScope
    {
        get
        {
            ValidateAll();
            var input = Encoding.UTF8.GetBytes($"{TenantId.Length}:{TenantId}{ScopeId.Length}:{ScopeId}{SourceId.Length}:{SourceId}");
            return new($"otel-{Convert.ToHexStringLower(SHA256.HashData(input))}");
        }
    }

    internal void ValidateAll()
    {
        Validate(TenantId, nameof(TenantId));
        Validate(ScopeId, nameof(ScopeId));
        Validate(SourceId, nameof(SourceId));
        Validate(TraceStreamId, nameof(TraceStreamId));
        Validate(SpanStreamId, nameof(SpanStreamId));
        Validate(MetricPointStreamId, nameof(MetricPointStreamId));
        Validate(LogStreamId, nameof(LogStreamId));

        var streams = new[] { TraceStreamId, SpanStreamId, MetricPointStreamId, LogStreamId };
        if (streams.Distinct(StringComparer.Ordinal).Count() != streams.Length)
            throw new ArgumentException("OpenTelemetry stream identities must be distinct.", nameof(TraceStreamId));
    }

    private static void Validate(string value, string parameterName) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
}
