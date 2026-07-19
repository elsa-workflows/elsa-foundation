namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

public readonly record struct IdentityRevisionStamp(string Value)
{
    public static IdentityRevisionStamp FromVersion(long version) => new("gw:" + version.ToString("D20"));

    public static IdentityRevisionStamp FromUser(string tenantId, string userId, long version) =>
        FromScoped("user", tenantId, userId, version);

    public static IdentityRevisionStamp FromRole(string tenantId, string roleId, long version) =>
        FromScoped("role", tenantId, roleId, version);

    public static bool TryParse(string? value, out IdentityRevisionStamp stamp)
    {
        if (TryGetVersion(value, out _))
        {
            stamp = new IdentityRevisionStamp(value!);
            return true;
        }

        if (TryGetScopedVersion(value, out _, out _))
        {
            stamp = new IdentityRevisionStamp(value!);
            return true;
        }

        stamp = default;
        return false;
    }

    public static bool TryGetVersion(string? value, out long version)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            value.Length == 23 &&
            value.StartsWith("gw:", StringComparison.Ordinal) &&
            long.TryParse(value.AsSpan(3), out version))
            return true;

        version = default;
        return false;
    }

    public static bool TryGetUserVersion(string? value, string tenantId, string userId, out long version) =>
        TryGetScopedVersion(value, "user", tenantId, userId, out version);

    public static bool TryGetRoleVersion(string? value, string tenantId, string roleId, out long version) =>
        TryGetScopedVersion(value, "role", tenantId, roleId, out version);

    private static IdentityRevisionStamp FromScoped(string kind, string tenantId, string entityId, long version) =>
        new("gw2:" + ScopeFingerprint(kind, tenantId, entityId) + ":" + version.ToString("D20"));

    private static bool TryGetScopedVersion(
        string? value,
        string kind,
        string tenantId,
        string entityId,
        out long version)
    {
        if (TryGetScopedVersion(value, out var fingerprint, out version) &&
            string.Equals(fingerprint, ScopeFingerprint(kind, tenantId, entityId), StringComparison.Ordinal))
            return true;

        version = default;
        return false;
    }

    private static bool TryGetScopedVersion(string? value, out string fingerprint, out long version)
    {
        const int prefixLength = 4;
        const int fingerprintLength = 64;
        const int separatorLength = 1;
        const int versionLength = 20;
        if (!string.IsNullOrWhiteSpace(value) &&
            value.Length == prefixLength + fingerprintLength + separatorLength + versionLength &&
            value.StartsWith("gw2:", StringComparison.Ordinal) &&
            value[prefixLength + fingerprintLength] == ':' &&
            long.TryParse(value.AsSpan(prefixLength + fingerprintLength + separatorLength), out version))
        {
            fingerprint = value.Substring(prefixLength, fingerprintLength);
            return true;
        }

        fingerprint = string.Empty;
        version = default;
        return false;
    }

    private static string ScopeFingerprint(string kind, string tenantId, string entityId) =>
        IdentityRequestFingerprint.FromParts(
            kind,
            IdentityCompositeDocumentId.Normalize(tenantId),
            IdentityCompositeDocumentId.Normalize(entityId)).ToString();

    public override string ToString() => Value;
}
