using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Narrow integration surface for first-party adapters that share the Identity EF authority.
/// It keeps canonical key, read-boundary, and revision behavior owned by the persistence module
/// without granting another production assembly access to all of its internals.
/// </summary>
public static class IdentityEntityFrameworkAdapterSupport
{
    public static string Normalize(string? value) => EfIdentityStoreSupport.Normalize(value);

    public static string TenantLookup(string tenantId) => EfIdentityStoreSupport.TenantLookup(tenantId);

    public static string Lookup(string tenantId, string? value) => EfIdentityStoreSupport.Lookup(tenantId, value);

    public static string RecordId(string tenantId, string recordId) => EfIdentityStoreSupport.RecordId(tenantId, recordId);

    public static string CompoundKey(params string?[] values) => EfIdentityStoreSupport.CompoundKey(values);

    public static string FramedRecordId(params string?[] values) => IdentityEntityFrameworkKey.FramedRecordId(values);

    public static IReadOnlySet<string> DeserializeSet(string? json) => EfIdentityStoreSupport.DeserializeSet(json);

    public static Task<T> ReadAsync<T>(DbContext context, string operation, Func<Task<T>> readAsync) =>
        EfIdentityStoreSupport.ReadAsync(context, operation, readAsync);
}

/// <summary>Scoped revision-stamp integration surface for first-party Identity framework adapters.</summary>
public static class IdentityEntityFrameworkRevisionSupport
{
    public static string FromUser(string tenantId, string userId, long version) =>
        IdentityEntityFrameworkRevisionCodec.FromUser(tenantId, userId, version);

    public static string FromRole(string tenantId, string roleId, long version) =>
        IdentityEntityFrameworkRevisionCodec.FromRole(tenantId, roleId, version);

    public static bool TryGetUserVersion(string? value, string tenantId, string userId, out long version) =>
        IdentityEntityFrameworkRevisionCodec.TryGetUserVersion(value, tenantId, userId, out version);

    public static bool TryGetRoleVersion(string? value, string tenantId, string roleId, out long version) =>
        IdentityEntityFrameworkRevisionCodec.TryGetRoleVersion(value, tenantId, roleId, out version);
}
