using System.Security.Cryptography;
using System.Text;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;

/// <summary>The provider-neutral scope that fences every EF OpenTelemetry row.</summary>
public sealed record EfOpenTelemetryBinding(string TenantId, string ScopeId, string SourceId)
{
    public static EfOpenTelemetryBinding Default { get; } = new("default", "default", "opentelemetry");
    public string ScopeKey { get; } = CreateScopeKey(TenantId, ScopeId, SourceId);

    public void Validate()
    {
        ValidatePart(TenantId, nameof(TenantId));
        ValidatePart(ScopeId, nameof(ScopeId));
        ValidatePart(SourceId, nameof(SourceId));
    }

    private static void ValidatePart(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException($"The OpenTelemetry binding {name} is invalid.", name);
    }

    private static string CreateScopeKey(string tenant, string scope, string source)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, tenant);
        Add(hash, scope);
        Add(hash, source);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Add(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
