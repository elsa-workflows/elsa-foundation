using System.Globalization;
namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Preserves the public Identity revision shape while storage is EF-backed.</summary>
internal static class IdentityEntityFrameworkRevisionCodec
{
    public static string FromVersion(long version)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), "Identity revisions must be positive.");
        return "gw:" + version.ToString("D20", CultureInfo.InvariantCulture);
    }

    public static bool TryGetVersion(string? value, out long version)
    {
        version = default;
        return value is { Length: 23 } &&
               value.StartsWith("gw:", StringComparison.Ordinal) &&
               long.TryParse(value.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out version) &&
               version > 0 &&
               string.Equals(value, FromVersion(version), StringComparison.Ordinal);
    }

    public static string FromUser(string tenantId, string userId, long version) =>
        FromScoped("user", tenantId, userId, version);

    public static string FromRole(string tenantId, string roleId, long version) =>
        FromScoped("role", tenantId, roleId, version);

    public static bool TryGetUserVersion(string? value, string tenantId, string userId, out long version) =>
        TryGetScopedVersion(value, "user", tenantId, userId, out version);

    public static bool TryGetRoleVersion(string? value, string tenantId, string roleId, out long version) =>
        TryGetScopedVersion(value, "role", tenantId, roleId, out version);

    private static string FromScoped(string kind, string tenantId, string entityId, long version) =>
        $"gw2:{ScopeFingerprint(kind, tenantId, entityId)}:{version.ToString("D20", CultureInfo.InvariantCulture)}";

    private static bool TryGetScopedVersion(string? value, string kind, string tenantId, string entityId, out long version)
    {
        version = 0;
        const int prefixLength = 4;
        const int fingerprintLength = 64;
        const int versionLength = 20;
        if (value is not { Length: prefixLength + fingerprintLength + 1 + versionLength } ||
            !value.StartsWith("gw2:", StringComparison.Ordinal) ||
            value[prefixLength + fingerprintLength] != ':' ||
            !long.TryParse(value.AsSpan(prefixLength + fingerprintLength + 1), NumberStyles.None, CultureInfo.InvariantCulture, out version) ||
            version <= 0)
            return false;

        return string.Equals(value.Substring(prefixLength, fingerprintLength), ScopeFingerprint(kind, tenantId, entityId), StringComparison.Ordinal) &&
               string.Equals(value, FromScoped(kind, tenantId, entityId, version), StringComparison.Ordinal);
    }

    private static string ScopeFingerprint(string kind, string tenantId, string entityId) =>
        // Use the shared framed UTF-16 hash so unpaired surrogate code units remain distinct.
        // UTF-8 encoding here would replace those units and could alias two valid Elsa IDs.
        IdentityEntityFrameworkKey.FramedRecordId(kind, Normalize(tenantId), Normalize(entityId));

    // Keep scoped framework stamps on the same canonical Unicode casing policy as every
    // tenant-local EF key. This includes compatibility mappings (for example Garay) that
    // are not available in every runtime ICU table.
    private static string Normalize(string value) => IdentityEntityFrameworkKey.Normalize(value);
}
