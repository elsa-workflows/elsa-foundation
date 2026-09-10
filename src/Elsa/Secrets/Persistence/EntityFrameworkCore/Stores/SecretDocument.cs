using System.Text.Json;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Serialization;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

public sealed record SecretDocument(
    string TenantId,
    string NormalizedName,
    string NameSearchKey,
    string DisplayNameSearchKey,
    string TypeNameLookupKey,
    string StoreNameLookupKey,
    string? ScopeLookupKey,
    string Status,
    bool HasNonExpiringActiveVersion,
    DateTimeOffset? MaxActiveVersionExpiresAt,
    Secret Secret)
{
    public static SecretDocument FromSecret(Secret secret)
    {
        var activeVersions = secret.Versions
            .Where(version => version.Status == SecretStatus.Active)
            .ToArray();
        return new SecretDocument(
            secret.TenantId,
            secret.Name,
            SecretsSearchKeys.SearchKey(secret.Name),
            SecretsSearchKeys.SearchKey(secret.DisplayName),
            SecretsSearchKeys.LookupKey(secret.TypeName),
            SecretsSearchKeys.LookupKey(secret.StoreName),
            secret.Scope is null ? null : SecretsSearchKeys.LookupKey(secret.Scope),
            SecretsSearchKeys.StatusValue(secret.Status),
            activeVersions.Any(version => version.ExpiresAt is null),
            MaxUtcExpiry(activeVersions),
            secret);
    }

    public static SecretDocument Parse(string payload) =>
        JsonSerializer.Deserialize<SecretDocument>(payload, SecretsEfJson.Options)
        ?? throw new InvalidOperationException("Secret document content is invalid.");

    public string ToPayload() => JsonSerializer.Serialize(this, SecretsEfJson.Options);

    public SecretRecord ToRecord(byte[]? concurrencyToken = null) => new()
    {
        TenantId = TenantId,
        NormalizedName = NormalizedName,
        NameSearchKey = NameSearchKey,
        DisplayNameSearchKey = DisplayNameSearchKey,
        TypeNameLookupKey = TypeNameLookupKey,
        StoreNameLookupKey = StoreNameLookupKey,
        ScopeLookupKey = ScopeLookupKey,
        Status = Status,
        HasNonExpiringActiveVersion = HasNonExpiringActiveVersion,
        MaxActiveVersionExpiresAt = MaxActiveVersionExpiresAt,
        Payload = ToPayload(),
        ConcurrencyToken = concurrencyToken ?? []
    };

    private static DateTimeOffset? MaxUtcExpiry(IEnumerable<SecretVersion> activeVersions)
    {
        var expiries = activeVersions
            .Where(version => version.ExpiresAt is not null)
            .Select(version => version.ExpiresAt!.Value.ToUniversalTime())
            .ToArray();
        return expiries.Length == 0 ? null : expiries.Max();
    }

    public void CopyProjectionsTo(SecretRecord record)
    {
        record.NameSearchKey = NameSearchKey;
        record.DisplayNameSearchKey = DisplayNameSearchKey;
        record.TypeNameLookupKey = TypeNameLookupKey;
        record.StoreNameLookupKey = StoreNameLookupKey;
        record.ScopeLookupKey = ScopeLookupKey;
        record.Status = Status;
        record.HasNonExpiringActiveVersion = HasNonExpiringActiveVersion;
        record.MaxActiveVersionExpiresAt = MaxActiveVersionExpiresAt;
        record.Payload = ToPayload();
    }
}
