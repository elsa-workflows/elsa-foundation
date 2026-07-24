using System.Security.Cryptography;
using System.Text;
using Groundwork.Core.Scoping;
using Groundwork.DiagnosticRecords;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>
/// Explicit persistence routing for one OpenTelemetry source. Core contracts remain provider-neutral; this
/// Groundwork leaf owns the translation to diagnostic scopes, streams, and a scoped document partition.
/// </summary>
public sealed class GroundworkOpenTelemetryBinding
{
    /// <summary>
    /// The first-party deployment binding used when a host does not select an explicit diagnostics source.
    /// Hosts that customize the binding must deploy the matching stream definitions through their selected
    /// <see cref="Groundwork.DiagnosticRecords.IDiagnosticRecordDeploymentManifestSource"/>.
    /// </summary>
    public static GroundworkOpenTelemetryBinding Default { get; } =
        Create("default", "default", "open-telemetry");

    private GroundworkOpenTelemetryBinding(string tenantId, string scopeId, string sourceId, string routeIdentity)
    {
        TenantId = tenantId;
        ScopeId = scopeId;
        SourceId = sourceId;
        var portableRouteIdentity = routeIdentity[..32];
        TraceStreamId = $"otel:{portableRouteIdentity}:traces";
        SpanStreamId = $"otel:{portableRouteIdentity}:spans";
        MetricPointStreamId = $"otel:{portableRouteIdentity}:metric-points";
        LogStreamId = $"otel:{portableRouteIdentity}:logs";
    }

    public string TenantId { get; }
    public string ScopeId { get; }
    public string SourceId { get; }
    public string TraceStreamId { get; }
    public string SpanStreamId { get; }
    public string MetricPointStreamId { get; }
    public string LogStreamId { get; }

    /// <summary>Creates a binding whose stream names are deterministically derived from the source identity.</summary>
    public static GroundworkOpenTelemetryBinding Create(string tenantId, string scopeId, string sourceId)
    {
        ValidateDiagnosticIdentifier(tenantId, nameof(tenantId));
        ValidateDiagnosticIdentifier(scopeId, nameof(scopeId));
        Validate(sourceId, nameof(sourceId));
        return new(tenantId, scopeId, sourceId, HashBinding(tenantId, scopeId, sourceId));
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
            return new($"otel-{HashBinding(TenantId, ScopeId, SourceId)}");
        }
    }

    internal void ValidateAll()
    {
        ValidateDiagnosticIdentifier(TenantId, nameof(TenantId));
        ValidateDiagnosticIdentifier(ScopeId, nameof(ScopeId));
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

    private static void ValidateDiagnosticIdentifier(string value, string parameterName)
    {
        Validate(value, parameterName);
        if (!DiagnosticStringComparisonKey.IsAsciiIgnoreCaseValue(value) ||
            char.IsWhiteSpace(value[0]) ||
            char.IsWhiteSpace(value[^1]) ||
            Encoding.UTF8.GetByteCount(value) > 64)
        {
            throw new ArgumentException(
                "Groundwork diagnostic tenant and scope identifiers must use printable ASCII, have no edge whitespace, and contain at most 64 UTF-8 bytes.",
                parameterName);
        }
    }

    private static string HashBinding(string tenantId, string scopeId, string sourceId)
    {
        var input = Encoding.UTF8.GetBytes($"{tenantId.Length}:{tenantId}{scopeId.Length}:{scopeId}{sourceId.Length}:{sourceId}");
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
}
