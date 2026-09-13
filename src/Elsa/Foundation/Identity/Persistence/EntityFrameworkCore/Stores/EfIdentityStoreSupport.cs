using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

public enum EfIdentityWriteStatus
{
    Inserted,
    Updated,
    Deleted,
    NotFound,
    Conflict
}

public sealed record EfIdentityWriteResult(
    EfIdentityWriteStatus Status,
    long? Version = null,
    string Message = "",
    string? Id = null,
    string? FailedUnitId = null)
{
    public bool Succeeded => Status is EfIdentityWriteStatus.Inserted or EfIdentityWriteStatus.Updated or EfIdentityWriteStatus.Deleted;
}

public enum EfIdentityAuthorityConflict
{
    None,
    UserName,
    Email,
    RoleName
}

public sealed record EfIdentityAuthorityWriteResult(
    EfIdentityWriteResult WriteResult,
    EfIdentityAuthorityConflict Conflict = EfIdentityAuthorityConflict.None);

internal static class EfIdentityStoreSupport
{
    public const int MaximumMaterializedListEntries = 512;
    public const int MaximumWriteAttempts = 3;
    public const int SortableKeyWidthBytes =
        IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength * sizeof(char) + sizeof(ushort);

    public static void EnsureTenant(IPersistenceAccessContextAccessor accessor, string tenantId) =>
        accessor.Current.EnsureScope(new Elsa.Workflows.Runtime.Core.Models.PersistenceScope(tenantId));

    public static string Normalize(string? value) => IdentityEntityFrameworkKey.Normalize(value);

    public static string TenantLookup(string tenantId) => IdentityEntityFrameworkKey.RecordId(tenantId);

    public static string Lookup(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : IdentityEntityFrameworkKey.TenantRecordId(tenantId, value);

    public static string RecordId(string tenantId, string recordId) =>
        IdentityEntityFrameworkKey.TenantRecordId(tenantId, recordId);

    public static byte[] SortableOrderKey(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Contract ordering is based on the same case-insensitive canonical key used
        // for identity lookups, while the entity still retains the original payload. The
        // caller's 400-code-unit Identity contract is validated before this helper is called.
        try
        {
            return IdentityEntityFrameworkKey.SortableUtf16Bytes(Normalize(value), SortableKeyWidthBytes);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("Identity ordering value exceeds the provider-safe sortable key width.", parameterName, exception);
        }
    }

    public static byte[] ExternalOrderKey(string provider, string providerSubject)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(providerSubject);
        // Concatenate fixed-width segments so provider ordering is primary and subject ordering is
        // secondary without a delimiter collision. Each segment is independently bounded and the
        // resulting 1604-byte tuple remains indexable on every supported provider when paired
        // only with the tenant-scoped user lookup key.
        var providerKey = SortableOrderKey(provider, nameof(provider));
        var subjectKey = SortableOrderKey(providerSubject, nameof(providerSubject));
        var result = new byte[providerKey.Length + subjectKey.Length];
        providerKey.CopyTo(result, 0);
        subjectKey.CopyTo(result, providerKey.Length);
        return result;
    }

    public static string CompoundKey(params string?[] values) =>
        IdentityEntityFrameworkKey.FramedRecordId(values.Select(Normalize).ToArray());

    public static string Fingerprint(params string?[] values) =>
        IdentityEntityFrameworkKey.FramedRecordId(values);

    /// <summary>Returns the reservation unit implicated by a provider-reported unique violation.</summary>
    public static string? UniqueConflictUnit(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Entries is the provider-neutral source of truth when EF has retained the pending
        // inserts. Check the reservation rows first because a single SaveChanges call can also
        // contain a user/role root and multiple relationship rows.
        foreach (var entry in exception.Entries)
        {
            var unit = entry.Entity switch
            {
                UserNameReservationEntity => IdentityIamEfModule.UserNameReservationTableName,
                EmailReservationEntity => IdentityIamEfModule.EmailReservationTableName,
                RoleNameReservationEntity => IdentityIamEfModule.RoleNameReservationTableName,
                _ => null
            };
            if (unit is not null)
                return unit;
        }

        // Some providers do not populate DbUpdateException.Entries for a server-side unique
        // violation. Their messages include the table/index identifier; matching only our own
        // bounded names keeps this fallback provider-neutral and avoids leaking provider text.
        var message = exception.ToString();
        if (message.Contains(IdentityIamEfModule.UserNameReservationTableName, StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ux_identity_user_name_reservations_key", StringComparison.OrdinalIgnoreCase))
            return IdentityIamEfModule.UserNameReservationTableName;
        if (message.Contains(IdentityIamEfModule.EmailReservationTableName, StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ux_identity_email_reservations_key", StringComparison.OrdinalIgnoreCase))
            return IdentityIamEfModule.EmailReservationTableName;
        if (message.Contains(IdentityIamEfModule.RoleNameReservationTableName, StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ux_identity_role_name_reservations_key", StringComparison.OrdinalIgnoreCase))
            return IdentityIamEfModule.RoleNameReservationTableName;

        return null;
    }

    /// <summary>
    /// Identifies a duplicate mutation-receipt key separately from a domain reservation
    /// conflict. Both are reported as relational uniqueness errors, but the former means that
    /// another caller may already have committed the authoritative result for this operation and
    /// therefore must be reconciled and replayed.
    /// </summary>
    public static bool IsMutationReceiptConflict(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = exception.ToString();
        if (message.Contains(IdentityIamEfModule.MutationReceiptTableName, StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ux_identity_mutation_receipts_id", StringComparison.OrdinalIgnoreCase))
            return true;

        // Entries can contain every pending row in a failed batch, not only the row whose
        // constraint failed. Treat it as authoritative only when the receipt is the sole entry.
        return exception.Entries.Count == 1 && exception.Entries[0].Entity is MutationReceiptEntity;
    }

    public static string SerializeSet(IReadOnlySet<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return IdentityApplicationSetCodec.Serialize(values);
    }

    public static IReadOnlySet<string> DeserializeSet(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new HashSet<string>(StringComparer.Ordinal);
        return IdentityApplicationSetCodec.Deserialize(json);
    }

    public static IamRevisionSaveResult ToRevisionResult(EfIdentityWriteResult result) =>
        result.Status switch
        {
            EfIdentityWriteStatus.Inserted or EfIdentityWriteStatus.Updated when result.Version is { } version =>
                new(IamRevisionSaveStatus.Saved, IdentityEntityFrameworkRevisionCodec.FromVersion(version)),
            EfIdentityWriteStatus.Inserted or EfIdentityWriteStatus.Updated => new(IamRevisionSaveStatus.Saved),
            EfIdentityWriteStatus.NotFound => new(IamRevisionSaveStatus.NotFound),
            _ => new(IamRevisionSaveStatus.Conflict)
        };

    public static IamRevisionSaveResult InvalidRevision() => new(IamRevisionSaveStatus.Conflict);

    public static void Clear(DbContext context) => context.ChangeTracker.Clear();

    /// <summary>
    /// Executes a provider-neutral read behind one consistent failure boundary. EF providers can
    /// throw different exception types for the same query failure; callers should not leak those
    /// types or leave a failed tracked query in the shared scoped context.
    /// </summary>
    public static async Task<T> ReadAsync<T>(
        DbContext context,
        string operation,
        Func<Task<T>> readAsync)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(readAsync);
        try
        {
            return await readAsync();
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            throw Failure(operation, exception);
        }
    }

    public static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) =>
        new(message, exception);
}
