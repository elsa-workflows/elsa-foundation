using System.Security.Cryptography;
using System.Text;
using Groundwork.Kernel;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>Explicit v2 storage routing for one OpenTelemetry source.</summary>
public sealed record V2OpenTelemetryBinding(string TenantId, string ScopeId, string SourceId)
{
    public static V2OpenTelemetryBinding Default { get; } = new("default", "default", "open-telemetry");

    public StorageScope StorageScope => new(CanonicalIdentity(TenantId, ScopeId, SourceId));

    public void Validate()
    {
        ValidatePart(TenantId, nameof(TenantId));
        ValidatePart(ScopeId, nameof(ScopeId));
        ValidatePart(SourceId, nameof(SourceId));
        _ = StorageScope;
    }

    private static void ValidatePart(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 64 || value.Any(character => character is < '!' or > '~'))
            throw new ArgumentException(
                "OpenTelemetry v2 binding values must use printable ASCII and be bounded to 64 characters.",
                parameterName);
    }

    private static string CanonicalIdentity(string tenant, string scope, string source)
    {
        var value = $"{tenant.Length}:{tenant}{scope.Length}:{scope}{source.Length}:{source}";
        return "otel-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
