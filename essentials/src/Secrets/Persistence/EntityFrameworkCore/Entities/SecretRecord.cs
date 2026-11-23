namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// Secrets row: tenant + normalized name + tombstone key, list/search
/// facets, JSON payload, and an explicit concurrency token (not cross-provider IsRowVersion).
/// </summary>
public sealed class SecretRecord
{
    public string TenantId { get; set; } = "";
    public string NormalizedName { get; set; } = "";

    /// <summary>
    /// Empty for the secret that currently holds the name, and the secret's id once it is deleted. Part of the key, so a
    /// tenant has at most one live row per name while every secret deleted under that name keeps a row of its own.
    /// </summary>
    public string Tombstone { get; set; } = "";
    public string NameSearchKey { get; set; } = "";
    public string DisplayNameSearchKey { get; set; } = "";
    public string TypeNameLookupKey { get; set; } = "";
    public string StoreNameLookupKey { get; set; } = "";
    public string? ScopeLookupKey { get; set; }
    public string Status { get; set; } = "";
    public bool HasNonExpiringActiveVersion { get; set; }
    public DateTimeOffset? MaxActiveVersionExpiresAt { get; set; }
    public string Payload { get; set; } = "{}";

    /// <summary>The persisted-schema version of this row: <see cref="SecretsEfModule.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = null!;
    public byte[] ConcurrencyToken { get; set; } = [];
}
