using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Persistence.EntityFramework;
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
    // Pinned: the Identity EF behavior tests assert that writes give up after 3 attempts.
    public const int MaximumWriteAttempts = 3;

    /// <summary>An unconditional save retries every lost race: it re-reads the winner's row and writes over it.</summary>
    public static readonly EfWriteRetry UnconditionalWrites = new(
        MaximumWriteAttempts,
        EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient);

    /// <summary>A create-only or compare-and-swap save reports a lost race as a conflict, so it retries only a transient provider failure.</summary>
    public static readonly EfWriteRetry TransientWrites = new(MaximumWriteAttempts, EfWriteConflict.Transient);
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

    public static void EnsureUserIdentity(UserEntity entity, string tenantId, string userId)
    {
        EnsureIdentity(entity.TenantId, tenantId, "user tenant");
        EnsureIdentity(entity.UserId, userId, "user identifier");
        EnsureExact(entity.Id, RecordId(tenantId, userId), "user record key");
        EnsureExact(entity.TenantLookupKey, TenantLookup(tenantId), "user tenant lookup key");
        EnsureExact(entity.UserIdOrderKey, SortableOrderKey(userId, nameof(userId)), "user order key");
        EnsureNullableExact(
            entity.NormalizedUserNameKey,
            string.IsNullOrWhiteSpace(entity.NormalizedUserName) ? null : Lookup(tenantId, entity.NormalizedUserName),
            "user-name lookup key");
        EnsureNullableExact(
            entity.NormalizedEmailKey,
            string.IsNullOrWhiteSpace(entity.NormalizedEmail) ? null : Lookup(tenantId, entity.NormalizedEmail),
            "user-email lookup key");
    }

    public static void EnsureRoleIdentity(RoleEntity entity, string tenantId, string roleId)
    {
        EnsureIdentity(entity.TenantId, tenantId, "role tenant");
        EnsureIdentity(entity.RoleId, roleId, "role identifier");
        EnsureExact(entity.Id, RecordId(tenantId, roleId), "role record key");
        EnsureExact(entity.TenantLookupKey, TenantLookup(tenantId), "role tenant lookup key");
        EnsureExact(entity.RoleIdOrderKey, SortableOrderKey(roleId, nameof(roleId)), "role order key");
        EnsureNullableExact(
            entity.NormalizedNameKey,
            string.IsNullOrWhiteSpace(entity.NormalizedName) ? null : Lookup(tenantId, entity.NormalizedName),
            "role-name lookup key");
    }

    public static void EnsureUserClaimIdentity(UserClaimEntity entity, string tenantId, string userId)
    {
        EnsureUserChildIdentity(entity.Id, entity.TenantId, entity.TenantLookupKey, entity.UserId, entity.UserLookupKey, tenantId, userId, CompoundKey(tenantId, userId, entity.ClaimType, entity.ClaimValue), "user claim");
        EnsureExact(entity.ClaimKey, CompoundKey(tenantId, entity.ClaimType, entity.ClaimValue), "user-claim lookup key");
    }

    public static void EnsureRoleClaimIdentity(RoleClaimEntity entity, string tenantId, string roleId)
    {
        EnsureIdentity(entity.TenantId, tenantId, "role-claim tenant");
        EnsureIdentity(entity.RoleId, roleId, "role-claim owner");
        EnsureExact(entity.Id, CompoundKey(tenantId, roleId, entity.ClaimType, entity.ClaimValue), "role-claim record key");
        EnsureExact(entity.TenantLookupKey, TenantLookup(tenantId), "role-claim tenant lookup key");
        EnsureExact(entity.RoleLookupKey, Lookup(tenantId, roleId), "role-claim owner lookup key");
        EnsureExact(entity.ClaimKey, CompoundKey(tenantId, entity.ClaimType, entity.ClaimValue), "role-claim lookup key");
    }

    public static void EnsureUserRoleIdentity(UserRoleEntity entity, string tenantId, string userId, string roleId)
    {
        EnsureUserChildIdentity(entity.Id, entity.TenantId, entity.TenantLookupKey, entity.UserId, entity.UserLookupKey, tenantId, userId, CompoundKey(tenantId, userId, roleId), "user-role link");
        EnsureIdentity(entity.RoleId, roleId, "user-role role");
        EnsureExact(entity.RoleLookupKey, Lookup(tenantId, roleId), "user-role role lookup key");
    }

    public static void EnsureUserTokenIdentity(UserTokenEntity entity, string tenantId, string userId)
    {
        EnsureUserChildIdentity(entity.Id, entity.TenantId, entity.TenantLookupKey, entity.UserId, entity.UserLookupKey, tenantId, userId, CompoundKey(tenantId, userId, entity.LoginProvider, entity.Name), "user token");
        EnsureExact(entity.TokenKey, CompoundKey(tenantId, entity.LoginProvider, entity.Name), "user-token lookup key");
    }

    public static void EnsureTenantMembershipIdentity(TenantMembershipEntity entity, string tenantId, string userId) =>
        EnsureUserChildIdentity(entity.Id, entity.TenantId, entity.TenantLookupKey, entity.UserId, entity.UserLookupKey, tenantId, userId, RecordId(tenantId, userId), "tenant membership");

    public static void EnsureExternalIdentity(
        ExternalIdentityEntity entity,
        string tenantId,
        string provider,
        string providerSubject,
        string? expectedUserId = null)
    {
        EnsureIdentity(entity.TenantId, tenantId, "external-login tenant");
        EnsureIdentity(entity.Provider, provider, "external-login provider");
        EnsureIdentity(entity.ProviderSubject, providerSubject, "external-login subject");
        if (expectedUserId is not null)
            EnsureIdentity(entity.UserId, expectedUserId, "external-login owner");
        EnsureExact(entity.Id, CompoundKey(tenantId, provider, providerSubject), "external-login record key");
        EnsureExact(entity.TenantLookupKey, TenantLookup(tenantId), "external-login tenant lookup key");
        EnsureExact(entity.ProviderLookupKey, Lookup(tenantId, provider), "external-login provider lookup key");
        EnsureExact(entity.ProviderSubjectLookupKey, Lookup(tenantId, providerSubject), "external-login subject lookup key");
        EnsureExact(entity.UserLookupKey, Lookup(tenantId, entity.UserId), "external-login owner lookup key");
        EnsureExact(entity.ExternalOrderKey, ExternalOrderKey(provider, providerSubject), "external-login order key");
    }

    public static void EnsureUserNameReservationIdentity(
        UserNameReservationEntity entity,
        string tenantId,
        string normalizedUserName,
        string? expectedUserId = null)
    {
        EnsureReservationOwner(entity.UserId, expectedUserId, "user-name reservation owner");
        EnsureIdentity(entity.TenantId, tenantId, "user-name reservation tenant");
        EnsureIdentity(entity.NormalizedUserName, normalizedUserName, "user-name reservation value");
        var key = Lookup(tenantId, normalizedUserName);
        EnsureExact(entity.Id, key, "user-name reservation record key");
        EnsureExact(entity.TenantLookupKey, TenantLookup(tenantId), "user-name reservation tenant lookup key");
        EnsureExact(entity.NormalizedUserNameKey, key, "user-name reservation lookup key");
    }

    public static void EnsureEmailReservationIdentity(
        EmailReservationEntity entity,
        string tenantId,
        string normalizedEmail,
        string? expectedUserId = null)
    {
        EnsureReservationOwner(entity.UserId, expectedUserId, "email reservation owner");
        EnsureIdentity(entity.TenantId, tenantId, "email reservation tenant");
        EnsureIdentity(entity.NormalizedEmail, normalizedEmail, "email reservation value");
        var key = Lookup(tenantId, normalizedEmail);
        EnsureExact(entity.Id, key, "email reservation record key");
        EnsureExact(entity.TenantLookupKey, TenantLookup(tenantId), "email reservation tenant lookup key");
        EnsureExact(entity.NormalizedEmailKey, key, "email reservation lookup key");
    }

    public static void EnsureRoleNameReservationIdentity(
        RoleNameReservationEntity entity,
        string tenantId,
        string normalizedRoleName,
        string? expectedRoleId = null)
    {
        EnsureReservationOwner(entity.RoleId, expectedRoleId, "role-name reservation owner");
        EnsureIdentity(entity.TenantId, tenantId, "role-name reservation tenant");
        EnsureIdentity(entity.NormalizedRoleName, normalizedRoleName, "role-name reservation value");
        var key = Lookup(tenantId, normalizedRoleName);
        EnsureExact(entity.Id, key, "role-name reservation record key");
        EnsureExact(entity.TenantLookupKey, TenantLookup(tenantId), "role-name reservation tenant lookup key");
        EnsureExact(entity.NormalizedRoleNameKey, key, "role-name reservation lookup key");
    }

    public static string CompoundKey(params string?[] values) =>
        IdentityEntityFrameworkKey.FramedRecordId(values.Select(Normalize).ToArray());

    public static string Fingerprint(params string?[] values) =>
        IdentityEntityFrameworkKey.FramedRecordId(values);

    /// <summary>Returns the reservation unit implicated by a provider-reported unique violation.</summary>
    public static string? UniqueConflictUnit(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var providerConstraint = ClassifyProviderUniqueConstraint(exception.ToString());
        switch (providerConstraint)
        {
            case ProviderUniqueConstraint.UserNameReservation:
                return IdentityIamEfModule.UserNameReservationTableName;
            case ProviderUniqueConstraint.EmailReservation:
                return IdentityIamEfModule.EmailReservationTableName;
            case ProviderUniqueConstraint.RoleNameReservation:
                return IdentityIamEfModule.RoleNameReservationTableName;
            case ProviderUniqueConstraint.MutationReceipt:
            case ProviderUniqueConstraint.OtherIdentityUnit:
                return null;
        }

        // A sole retained entry identifies the failed unit without provider text. A batch can
        // contain both a root and reservations, so multiple entries cannot identify which row
        // caused the unique violation. Provider constraint identity above always wins because
        // Entries can contain pending rows other than the row that actually failed.
        // Entries live on the save failure itself, not on any wrapper above it.
        if (EfRelationalExceptionClassifier.FindSaveFailure(exception) is not { } update)
            return null;

        if (update.Entries.Count == 1)
        {
            var unit = update.Entries.Single().Entity switch
            {
                UserNameReservationEntity => IdentityIamEfModule.UserNameReservationTableName,
                EmailReservationEntity => IdentityIamEfModule.EmailReservationTableName,
                RoleNameReservationEntity => IdentityIamEfModule.RoleNameReservationTableName,
                _ => null
            };
            if (unit is not null)
                return unit;
        }

        return null;
    }

    /// <summary>
    /// Identifies a duplicate mutation-receipt key separately from a domain reservation
    /// conflict. Both are reported as relational uniqueness errors, but the former means that
    /// another caller may already have committed the authoritative result for this operation and
    /// therefore must be reconciled and replayed.
    /// </summary>
    public static bool IsMutationReceiptConflict(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // An execution strategy re-raises a save failure as an InvalidOperationException around the
        // DbUpdateException, and only that carries the entries this decision reads. Unwrapped, the
        // search finds the argument itself, so the answer is unchanged for a direct save failure.
        if (EfRelationalExceptionClassifier.FindSaveFailure(exception) is not { } update)
            return false;

        var providerConstraint = ClassifyProviderUniqueConstraint(update.ToString());
        if (providerConstraint != ProviderUniqueConstraint.Unidentified)
            return providerConstraint == ProviderUniqueConstraint.MutationReceipt;

        // Entries can contain every pending row in a failed batch, not only the row whose
        // constraint failed. Treat it as authoritative only when the receipt is the sole entry.
        return update.Entries.Count == 1 && update.Entries[0].Entity is MutationReceiptEntity;
    }

    private static ProviderUniqueConstraint ClassifyProviderUniqueConstraint(string message)
    {
        if (message.Contains("ux_identity_user_name_reservations_key", StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.UserNameReservation;
        if (message.Contains("ux_identity_email_reservations_key", StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.EmailReservation;
        if (message.Contains("ux_identity_role_name_reservations_key", StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.RoleNameReservation;
        if (message.Contains("ux_identity_mutation_receipts_id", StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.MutationReceipt;

        // SQL Server and PostgreSQL report the configured constraint name. MySQL can report only
        // PRIMARY; when the table is unavailable that is still authoritative evidence that a
        // pending reservation or receipt entry must not be guessed as the failed unit.
        if (message.Contains("ux_identity_", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("PK_identity_", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("for key 'PRIMARY'", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("for key `PRIMARY`", StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.OtherIdentityUnit;

        // SQLite reports table/column identifiers instead of the configured index name.
        if (message.Contains(IdentityIamEfModule.UserNameReservationTableName, StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.UserNameReservation;
        if (message.Contains(IdentityIamEfModule.EmailReservationTableName, StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.EmailReservation;
        if (message.Contains(IdentityIamEfModule.RoleNameReservationTableName, StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.RoleNameReservation;
        if (message.Contains(IdentityIamEfModule.MutationReceiptTableName, StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.MutationReceipt;
        if (message.Contains("identity_", StringComparison.OrdinalIgnoreCase))
            return ProviderUniqueConstraint.OtherIdentityUnit;

        return ProviderUniqueConstraint.Unidentified;
    }

    private enum ProviderUniqueConstraint
    {
        Unidentified,
        UserNameReservation,
        EmailReservation,
        RoleNameReservation,
        MutationReceipt,
        OtherIdentityUnit
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

    /// <summary>An unconditional save has no result to report a conflict through, so a write that did not succeed fails it.</summary>
    public static void EnsureSaved(EfIdentityWriteResult result, string failureMessage)
    {
        if (!result.Succeeded)
            throw Failure(failureMessage, new InvalidOperationException(result.Message));
    }

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
        catch (Exception exception) when (exception is not OperationCanceledException and not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure(operation, exception);
        }
    }

    public static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) =>
        new(message, exception);

    private static void EnsureUserChildIdentity(
        string id,
        string actualTenantId,
        string tenantLookupKey,
        string actualUserId,
        string userLookupKey,
        string expectedTenantId,
        string expectedUserId,
        string expectedId,
        string kind)
    {
        EnsureIdentity(actualTenantId, expectedTenantId, $"{kind} tenant");
        EnsureIdentity(actualUserId, expectedUserId, $"{kind} owner");
        EnsureExact(id, expectedId, $"{kind} record key");
        EnsureExact(tenantLookupKey, TenantLookup(expectedTenantId), $"{kind} tenant lookup key");
        EnsureExact(userLookupKey, Lookup(expectedTenantId, expectedUserId), $"{kind} owner lookup key");
    }

    private static void EnsureIdentity(string actual, string expected, string description)
    {
        if (!string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal))
            ThrowCorrupt(description);
    }

    private static void EnsureReservationOwner(string actual, string? expected, string description)
    {
        if (string.IsNullOrWhiteSpace(actual))
            ThrowCorrupt(description);
        if (expected is not null)
            EnsureIdentity(actual, expected, description);
    }

    private static void EnsureNullableExact(string? actual, string? expected, string description)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            ThrowCorrupt(description);
    }

    private static void EnsureExact(string actual, string expected, string description)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            ThrowCorrupt(description);
    }

    private static void EnsureExact(byte[] actual, byte[] expected, string description)
    {
        if (!actual.AsSpan().SequenceEqual(expected))
            ThrowCorrupt(description);
    }

    private static void ThrowCorrupt(string description)
    {
        var message = $"The persisted Identity {description} is inconsistent with its canonical authority identity.";
        throw Failure(message, new InvalidDataException(message));
    }
}
