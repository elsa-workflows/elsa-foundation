using System.Globalization;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Derived physical projection for this new family only; never wire identity or source authority.</summary>
internal sealed class RuntimeDeliveryDiscoveryProjection
{
    private RuntimeDeliveryDiscoveryProjection(string identity)
    {
        Identity = identity;
        Hash = EfRelationalIdentity.Hash(identity);
    }
    internal string Identity { get; }
    internal string Hash { get; }

    internal static RuntimeDeliveryDiscoveryProjection Create(RuntimeDeliveryDiscoveryPartition partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        var framed = new StringBuilder();
        Value("runtime-delivery-discovery/v1");
        Value(partition.Scope.Value); Value(partition.TenantId);
        Value(((int)partition.RunKind).ToString(CultureInfo.InvariantCulture));
        Value(partition.ExecutionPartition?.Value);
        Value(partition.TestScope is null ? null : "present");
        if (partition.TestScope is { } test)
        {
            Value(test.ScopeId);
            // ContextEquals compares instants, not offsets: equal expiry instants share one projection.
            Value(test.ExpiresAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
            Value(test.TenantId); Value(test.Partition.Value);
        }
        Value(partition.Authority.SystemIdentity); Value(partition.Authority.RootInitiator);
        Value(partition.Authority.Metadata.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var item in partition.Authority.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            Value(item.Key); Value(item.Value);
        }
        // Uppercase ASCII hex preserves every framed UTF-16 code unit, including NUL/lone surrogates, and
        // avoids database collation normalization of the raw context. This full value is not index bounded.
        return new(EfRelationalIdentity.CreateOrdinalTextOrderKey(framed.ToString()));

        void Value(string? value)
        {
            if (value is null) { framed.Append("-:"); return; }
            framed.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        }
    }

    /// <summary>Both predicates execute in SQL before callers order/Take; hashes alone never select a partition.</summary>
    internal IQueryable<RuntimeDeliveryWorkEntity> FilterWork(IQueryable<RuntimeDeliveryWorkEntity> rows) =>
        rows.Where(row => row.FullDiscoveryIdentityHash == Hash && row.FullDiscoveryIdentity == Identity);

    internal IQueryable<RuntimeIngressSourceEntity> FilterSources(IQueryable<RuntimeIngressSourceEntity> rows) =>
        rows.Where(row => row.FullDiscoveryIdentityHash == Hash && row.FullDiscoveryIdentity == Identity);

    /// <summary>Canonical context must come from the recognized family's checked full source header, not a request.</summary>
    internal static void Validate(RuntimeDeliveryWorkEntity row, RuntimeDeliveryDiscoveryPartition canonical)
    {
        ArgumentNullException.ThrowIfNull(row);
        Validate(row.FullDiscoveryIdentity, row.FullDiscoveryIdentityHash, row.ScopeKey, row.ScopeKeyHash,
            row.TenantId, row.TenantIdHash, row.RunKind, row.TestScopeId, row.TestScopeIdHash,
            row.AuthorityPartitionKey, canonical);
    }

    internal static void Validate(RuntimeIngressSourceEntity row, RuntimeDeliveryDiscoveryPartition canonical)
    {
        ArgumentNullException.ThrowIfNull(row);
        Validate(row.FullDiscoveryIdentity, row.FullDiscoveryIdentityHash, row.ScopeKey, row.ScopeKeyHash,
            row.TenantId, row.TenantIdHash, row.RunKind, row.TestScopeId, row.TestScopeIdHash,
            row.AuthorityPartitionKey, canonical);
    }

    private static void Validate(string identity, string hash, string scope, string scopeHash,
        string? tenant, string? tenantHash, int runKind, string? test, string? testHash,
        string authorityKey, RuntimeDeliveryDiscoveryPartition canonical)
    {
        var expected = Create(canonical);
        if (identity != expected.Identity || hash != expected.Hash ||
            scope != EfRelationalIdentity.Encode(canonical.Scope.Value) || scopeHash != EfRelationalIdentity.Hash(canonical.Scope.Value) ||
            tenant != Encode(canonical.TenantId) || tenantHash != HashOf(canonical.TenantId) || runKind != (int)canonical.RunKind ||
            test != Encode(canonical.TestScope?.ScopeId) || testHash != HashOf(canonical.TestScope?.ScopeId) ||
            authorityKey != canonical.AuthorityPartitionKey)
            throw new InvalidDataException("The delivery discovery projection differs from canonical source context.");
    }

    private static string? Encode(string? value) => value is null ? null : EfRelationalIdentity.Encode(value);
    private static string? HashOf(string? value) => value is null ? null : EfRelationalIdentity.Hash(value);
    public override string ToString() => nameof(RuntimeDeliveryDiscoveryProjection);
}
