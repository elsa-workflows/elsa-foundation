using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Secrets.Core.Models;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Maps one logical Secret workload process to an isolated physical tenant namespace.</summary>
internal static class SecretStorageScope
{
    public static string For(RunRequest request)
    {
        var identity = string.Join(
            '|',
            request.ComparisonCohortId,
            request.MeasurementSetId,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Provider,
            request.ProviderVersion,
            request.ProviderTopology,
            string.Join(';', request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")),
            request.Adapter,
            request.PhysicalForm,
            request.Scale,
            request.CommitSha,
            request.HarnessAssemblySha256,
            request.CompositionFingerprint,
            request.HostFingerprintSha256,
            request.Seed,
            request.InputFingerprintSha256,
            request.NativePlanIdentity,
            request.NativePlanEvidenceReference,
            request.NativePlanContentSha256,
            request.ProcessKind,
            request.ProcessIndex);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"benchmark-secret-{digest}";
    }

    public static string PhysicalTenant(string tenantId, string persistenceScope) =>
        $"{persistenceScope}:{tenantId}";

    public static Secret ToStorage(Secret source, string persistenceScope)
    {
        var copy = Clone(source);
        copy.Id = PhysicalId(source.Id, persistenceScope);
        copy.TenantId = PhysicalTenant(source.TenantId, persistenceScope);
        return copy;
    }

    public static Secret ToLogical(Secret source, string tenantId, string persistenceScope)
    {
        var copy = Clone(source);
        copy.Id = LogicalId(source.Id, persistenceScope);
        copy.TenantId = tenantId;
        return copy;
    }

    private static string PhysicalId(string id, string persistenceScope) =>
        string.Concat(persistenceScope, ":", id);

    private static string LogicalId(string id, string persistenceScope)
    {
        var prefix = persistenceScope + ":";
        return id.StartsWith(prefix, StringComparison.Ordinal) ? id[prefix.Length..] : id;
    }

    private static Secret Clone(Secret source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        Name = source.Name,
        DisplayName = source.DisplayName,
        Description = source.Description,
        TypeName = source.TypeName,
        StoreName = source.StoreName,
        Scope = source.Scope,
        Tags = new HashSet<string>(source.Tags, StringComparer.OrdinalIgnoreCase),
        Status = source.Status,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        Versions = source.Versions.Select(version => new SecretVersion
        {
            Version = version.Version,
            Status = version.Status,
            CreatedAt = version.CreatedAt,
            ExpiresAt = version.ExpiresAt,
            Payload = new SecretPayload
            {
                Value = version.Payload.Value,
                Metadata = new Dictionary<string, string>(version.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
            }
        }).ToList()
    };
}
