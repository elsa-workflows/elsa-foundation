namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// Secrets row matching Groundwork projections: tenant + normalized name key, list/search
/// facets, JSON payload, and an explicit concurrency token (not cross-provider IsRowVersion).
/// </summary>
public sealed class SecretRecord
{
    public string TenantId { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string NameSearchKey { get; set; } = "";
    public string DisplayNameSearchKey { get; set; } = "";
    public string TypeNameLookupKey { get; set; } = "";
    public string StoreNameLookupKey { get; set; } = "";
    public string? ScopeLookupKey { get; set; }
    public string Status { get; set; } = "";
    public bool HasNonExpiringActiveVersion { get; set; }
    public DateTimeOffset? MaxActiveVersionExpiresAt { get; set; }
    public string Payload { get; set; } = "{}";
    public byte[] ConcurrencyToken { get; set; } = [];
}
