using System.Globalization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Coordinates authority roots, uniqueness reservations, and aggregate deletion in the selected
/// Identity EF context. No provider-specific API is used; provider engines remain host-owned.
/// </summary>
public sealed class EfIdentityAuthorityAggregateCoordinator(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    EfIdentityAtomicWrite? atomicWrite = null)
{
    private readonly EfIdentityAtomicWrite atomicWrite = atomicWrite ?? new EfIdentityAtomicWrite(context, accessContextAccessor: accessContextAccessor);
    public async Task<EfIdentityAuthorityWriteResult> SaveUserAsync(
        UserRecord user,
        long? expectedVersion,
        bool requireUniqueEmail,
        CancellationToken cancellationToken = default,
        string? requestIdentity = null,
        Action<UserEntity>? applyWithinAtomic = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        ValidateUser(user);
        EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, user.TenantId);
        EfIdentityAuthorityConflict conflict = EfIdentityAuthorityConflict.None;
        var result = await ExecuteAsync(
            "save-user-authority-aggregate",
            EfIdentityStoreSupport.Fingerprint(
                user.TenantId, user.Id, user.UserName, user.Email, user.DisplayName,
                ((int)user.Status).ToString(CultureInfo.InvariantCulture), ((int)user.Ownership).ToString(CultureInfo.InvariantCulture),
                EfIdentityStoreSupport.SerializeSet(user.RoleIds),
                EfIdentityStoreSupport.SerializeSet(user.DirectPermissions),
                expectedVersion?.ToString(CultureInfo.InvariantCulture), requireUniqueEmail.ToString(), requestIdentity),
            async _ =>
            {
                var id = EfIdentityStoreSupport.RecordId(user.TenantId, user.Id);
                var existing = await context.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (existing is not null)
                    EfIdentityStoreSupport.EnsureUserIdentity(existing, user.TenantId, user.Id);
                if (!CanWrite(existing?.Revision, expectedVersion))
                    return existing is null && expectedVersion is > 0 ? NotFound(id) : Conflict(id);

                var oldName = existing?.NormalizedUserNameKey;
                var oldEmail = existing?.NormalizedEmailKey;
                if (existing is null)
                {
                    existing = new UserEntity { Id = id, Revision = 1 };
                    context.Users.Add(existing);
                }
                else
                    existing.Revision = checked(existing.Revision + 1);

                Apply(existing, user);
                applyWithinAtomic?.Invoke(existing);
                CanonicalizeLookupKeys(existing);

                // Reservation keys must reflect the final normalized values, including any
                // framework callback changes, rather than the provider-neutral raw record.
                var userName = ReservationForUserName(existing);
                if (userName is not null && !await ReserveUserNameAsync(userName, user.Id, cancellationToken))
                {
                    conflict = EfIdentityAuthorityConflict.UserName;
                    return Conflict(userName.Id);
                }

                var email = requireUniqueEmail ? ReservationForEmail(existing) : null;
                if (email is not null && !await ReserveEmailAsync(email, user.Id, cancellationToken))
                {
                    conflict = EfIdentityAuthorityConflict.Email;
                    return Conflict(email.Id);
                }

                if (!string.Equals(oldName, existing.NormalizedUserNameKey, StringComparison.Ordinal))
                    await DeleteUserNameReservationAsync(oldName, user.TenantId, user.Id, cancellationToken);
                if (!string.Equals(oldEmail, existing.NormalizedEmailKey, StringComparison.Ordinal))
                    await DeleteEmailReservationAsync(oldEmail, user.TenantId, user.Id, cancellationToken);
                return existing.Revision == 1 ? Inserted(id, 1) : Updated(id, existing.Revision);
            },
            cancellationToken,
            user.TenantId);
        return new EfIdentityAuthorityWriteResult(result, conflict == EfIdentityAuthorityConflict.None ? MapConflict(result.FailedUnitId) : conflict);
    }

    public async Task<EfIdentityAuthorityWriteResult> SaveRoleAsync(
        RoleRecord role,
        long? expectedVersion,
        CancellationToken cancellationToken = default,
        string? requestIdentity = null,
        Action<RoleEntity>? applyWithinAtomic = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        ValidateRole(role);
        EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, role.TenantId);
        EfIdentityAuthorityConflict conflict = EfIdentityAuthorityConflict.None;
        var result = await ExecuteAsync(
            "save-role-authority-aggregate",
            EfIdentityStoreSupport.Fingerprint(
                role.TenantId, role.Id, role.Name, role.Description, role.System.ToString(),
                EfIdentityStoreSupport.SerializeSet(role.Permissions), expectedVersion?.ToString(CultureInfo.InvariantCulture), requestIdentity),
            async _ =>
            {
                var id = EfIdentityStoreSupport.RecordId(role.TenantId, role.Id);
                var existing = await context.Roles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (existing is not null)
                    EfIdentityStoreSupport.EnsureRoleIdentity(existing, role.TenantId, role.Id);
                if (!CanWrite(existing?.Revision, expectedVersion))
                    return existing is null && expectedVersion is > 0 ? NotFound(id) : Conflict(id);

                var oldName = existing?.NormalizedNameKey;
                if (existing is null)
                {
                    existing = new RoleEntity { Id = id, Revision = 1 };
                    context.Roles.Add(existing);
                }
                else
                    existing.Revision = checked(existing.Revision + 1);
                Apply(existing, role);
                applyWithinAtomic?.Invoke(existing);
                CanonicalizeLookupKeys(existing);
                var reservation = ReservationForRoleName(existing);
                if (reservation is not null && !await ReserveRoleNameAsync(reservation, role.Id, cancellationToken))
                {
                    conflict = EfIdentityAuthorityConflict.RoleName;
                    return Conflict(reservation.Id);
                }

                if (!string.Equals(oldName, existing.NormalizedNameKey, StringComparison.Ordinal))
                    await DeleteRoleNameReservationAsync(oldName, role.TenantId, role.Id, cancellationToken);
                return existing.Revision == 1 ? Inserted(id, 1) : Updated(id, existing.Revision);
            },
            cancellationToken,
            role.TenantId);
        return new EfIdentityAuthorityWriteResult(result, conflict == EfIdentityAuthorityConflict.None ? MapConflict(result.FailedUnitId) : conflict);
    }

    public Task<EfIdentityWriteResult> DeleteUserAsync(
        string tenantId,
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken = default) =>
        DeleteUserCoreAsync(tenantId, userId, expectedVersion, cancellationToken);

    public Task<EfIdentityWriteResult> DeleteRoleAsync(
        string tenantId,
        string roleId,
        long expectedVersion,
        CancellationToken cancellationToken = default) =>
        DeleteRoleCoreAsync(tenantId, roleId, expectedVersion, cancellationToken);

    private async Task<EfIdentityWriteResult> DeleteUserCoreAsync(string tenantId, string userId, long expectedVersion, CancellationToken cancellationToken)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(userId, nameof(userId));
        EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, tenantId);
        return await ExecuteAsync(
            "delete-user-authority-aggregate",
            EfIdentityStoreSupport.Fingerprint(tenantId, userId, expectedVersion.ToString(CultureInfo.InvariantCulture)),
            async token =>
            {
                var id = EfIdentityStoreSupport.RecordId(tenantId, userId);
                var user = await context.Users.SingleOrDefaultAsync(x => x.Id == id, token);
                if (user is null)
                    return NotFound(id);
                if (user.Revision != expectedVersion)
                    return Conflict(id);

                EfIdentityStoreSupport.EnsureUserIdentity(user, tenantId, userId);
                var claimIds = ReadRegistry(user.ClaimIdsJson, "user claims");
                var loginIds = ReadRegistry(user.LoginIdsJson, "external logins");
                var roleLinkIds = ReadRegistry(user.RoleLinkIdsJson, "user-role links");
                var tokenIds = ReadRegistry(user.TokenIdsJson, "user tokens");
                var membershipIds = ReadRegistry(user.TenantMembershipIdsJson, "tenant memberships");
                EnsureAggregateRelationshipCapacity(claimIds, loginIds, roleLinkIds, tokenIds, membershipIds);

                foreach (var childId in claimIds)
                {
                    var claim = RequireRegistered(
                        await context.UserClaims.SingleOrDefaultAsync(x => x.Id == childId, token),
                        "user claim",
                        childId);
                    EfIdentityStoreSupport.EnsureUserClaimIdentity(claim, tenantId, userId);
                    context.UserClaims.Remove(claim);
                }

                foreach (var childId in loginIds)
                {
                    var login = RequireRegistered(
                        await context.ExternalIdentities.SingleOrDefaultAsync(x => x.Id == childId, token),
                        "external login",
                        childId);
                    EfIdentityStoreSupport.EnsureExternalIdentity(login, tenantId, login.Provider, login.ProviderSubject, userId);
                    context.ExternalIdentities.Remove(login);
                }

                foreach (var childId in tokenIds)
                {
                    var tokenRow = RequireRegistered(
                        await context.UserTokens.SingleOrDefaultAsync(x => x.Id == childId, token),
                        "user token",
                        childId);
                    EfIdentityStoreSupport.EnsureUserTokenIdentity(tokenRow, tenantId, userId);
                    context.UserTokens.Remove(tokenRow);
                }

                foreach (var childId in membershipIds)
                {
                    var membership = RequireRegistered(
                        await context.TenantMemberships.SingleOrDefaultAsync(x => x.Id == childId, token),
                        "tenant membership",
                        childId);
                    EfIdentityStoreSupport.EnsureTenantMembershipIdentity(membership, tenantId, userId);
                    context.TenantMemberships.Remove(membership);
                }

                foreach (var childId in roleLinkIds)
                {
                    var link = RequireRegistered(
                        await context.UserRoles.SingleOrDefaultAsync(x => x.Id == childId, token),
                        "user-role link",
                        childId);
                    EfIdentityStoreSupport.EnsureUserRoleIdentity(link, tenantId, userId, link.RoleId);

                    var role = RequireRegistered(
                        await context.Roles.SingleOrDefaultAsync(
                            x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, link.RoleId),
                            token),
                        "linked role",
                        link.RoleId);
                    EfIdentityStoreSupport.EnsureRoleIdentity(role, tenantId, link.RoleId);
                    role.UserLinkIdsJson = RemoveId(role.UserLinkIdsJson, link.Id);
                    role.Revision = checked(role.Revision + 1);
                    context.UserRoles.Remove(link);
                }

                if (user.NormalizedUserNameKey is not null)
                {
                    var name = await context.UserNameReservations.SingleOrDefaultAsync(
                        x => x.Id == user.NormalizedUserNameKey,
                        token);
                    if (name is not null)
                    {
                        EfIdentityStoreSupport.EnsureUserNameReservationIdentity(name, tenantId, user.NormalizedUserName!, userId);
                        context.UserNameReservations.Remove(name);
                    }
                }

                if (user.NormalizedEmailKey is not null)
                {
                    var email = await context.EmailReservations.SingleOrDefaultAsync(
                        x => x.Id == user.NormalizedEmailKey,
                        token);
                    if (email is not null)
                    {
                        EfIdentityStoreSupport.EnsureEmailReservationIdentity(email, tenantId, user.NormalizedEmail!, userId);
                        context.EmailReservations.Remove(email);
                    }
                }

                context.Users.Remove(user);
                return Deleted(id, expectedVersion);
            }, cancellationToken,
            tenantId);
    }

    private async Task<EfIdentityWriteResult> DeleteRoleCoreAsync(string tenantId, string roleId, long expectedVersion, CancellationToken cancellationToken)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(roleId, nameof(roleId));
        EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, tenantId);
        return await ExecuteAsync(
            "delete-role-authority-aggregate",
            EfIdentityStoreSupport.Fingerprint(tenantId, roleId, expectedVersion.ToString(CultureInfo.InvariantCulture)),
            async token =>
            {
                var id = EfIdentityStoreSupport.RecordId(tenantId, roleId);
                var role = await context.Roles.SingleOrDefaultAsync(x => x.Id == id, token);
                if (role is null)
                    return NotFound(id);
                if (role.Revision != expectedVersion)
                    return Conflict(id);

                EfIdentityStoreSupport.EnsureRoleIdentity(role, tenantId, roleId);
                var claimIds = ReadRegistry(role.ClaimIdsJson, "role claims");
                var roleLinkIds = ReadRegistry(role.UserLinkIdsJson, "user-role links");
                EnsureAggregateRelationshipCapacity(claimIds, roleLinkIds);

                foreach (var childId in claimIds)
                {
                    var claim = RequireRegistered(
                        await context.RoleClaims.SingleOrDefaultAsync(x => x.Id == childId, token),
                        "role claim",
                        childId);
                    EfIdentityStoreSupport.EnsureRoleClaimIdentity(claim, tenantId, roleId);
                    context.RoleClaims.Remove(claim);
                }

                foreach (var childId in roleLinkIds)
                {
                    var link = RequireRegistered(
                        await context.UserRoles.SingleOrDefaultAsync(x => x.Id == childId, token),
                        "user-role link",
                        childId);
                    EfIdentityStoreSupport.EnsureUserRoleIdentity(link, tenantId, link.UserId, roleId);

                    var user = RequireRegistered(
                        await context.Users.SingleOrDefaultAsync(
                            x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, link.UserId),
                            token),
                        "linked user",
                        link.UserId);
                    EfIdentityStoreSupport.EnsureUserIdentity(user, tenantId, link.UserId);
                    user.RoleLinkIdsJson = RemoveId(user.RoleLinkIdsJson, link.Id);
                    user.RoleIdsJson = RemoveEquivalentId(user.RoleIdsJson, link.RoleId);
                    user.Revision = checked(user.Revision + 1);
                    context.UserRoles.Remove(link);
                }

                if (role.NormalizedNameKey is not null)
                {
                    var reservation = await context.RoleNameReservations.SingleOrDefaultAsync(
                        x => x.Id == role.NormalizedNameKey,
                        token);
                    if (reservation is not null)
                    {
                        EfIdentityStoreSupport.EnsureRoleNameReservationIdentity(reservation, tenantId, role.NormalizedName!, roleId);
                        context.RoleNameReservations.Remove(reservation);
                    }
                }

                context.Roles.Remove(role);
                return Deleted(id, expectedVersion);
            }, cancellationToken,
            tenantId);
    }

    private async Task<EfIdentityWriteResult> ExecuteAsync(
        string operation,
        string fingerprint,
        Func<CancellationToken, Task<EfIdentityWriteResult>> stageAsync,
        CancellationToken cancellationToken,
        string tenantId)
    {
        return await atomicWrite.ExecuteAsync(
            EfIdentityAtomicMutation.Create(operation, fingerprint, tenantId), stageAsync, cancellationToken);
    }

    private async Task<bool> ReserveUserNameAsync(UserNameReservationEntity reservation, string userId, CancellationToken cancellationToken)
    {
        var current = await context.UserNameReservations.SingleOrDefaultAsync(x => x.Id == reservation.Id, cancellationToken);
        if (current is not null)
        {
            EfIdentityStoreSupport.EnsureUserNameReservationIdentity(current, reservation.TenantId, reservation.NormalizedUserName);
            return Same(current.UserId, userId);
        }
        context.UserNameReservations.Add(reservation);
        return true;
    }

    private async Task<bool> ReserveEmailAsync(EmailReservationEntity reservation, string userId, CancellationToken cancellationToken)
    {
        var current = await context.EmailReservations.SingleOrDefaultAsync(x => x.Id == reservation.Id, cancellationToken);
        if (current is not null)
        {
            EfIdentityStoreSupport.EnsureEmailReservationIdentity(current, reservation.TenantId, reservation.NormalizedEmail);
            return Same(current.UserId, userId);
        }
        context.EmailReservations.Add(reservation);
        return true;
    }

    private async Task<bool> ReserveRoleNameAsync(RoleNameReservationEntity reservation, string roleId, CancellationToken cancellationToken)
    {
        var current = await context.RoleNameReservations.SingleOrDefaultAsync(x => x.Id == reservation.Id, cancellationToken);
        if (current is not null)
        {
            EfIdentityStoreSupport.EnsureRoleNameReservationIdentity(current, reservation.TenantId, reservation.NormalizedRoleName);
            return Same(current.RoleId, roleId);
        }
        context.RoleNameReservations.Add(reservation);
        return true;
    }

    private async Task DeleteUserNameReservationAsync(string? key, string tenantId, string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(key))
            return;
        var current = await context.UserNameReservations.SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (current is not null)
        {
            EfIdentityStoreSupport.EnsureUserNameReservationIdentity(current, tenantId, current.NormalizedUserName, userId);
            context.UserNameReservations.Remove(current);
        }
    }

    private async Task DeleteEmailReservationAsync(string? key, string tenantId, string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(key))
            return;
        var current = await context.EmailReservations.SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (current is not null)
        {
            EfIdentityStoreSupport.EnsureEmailReservationIdentity(current, tenantId, current.NormalizedEmail, userId);
            context.EmailReservations.Remove(current);
        }
    }

    private async Task DeleteRoleNameReservationAsync(string? key, string tenantId, string roleId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(key))
            return;
        var current = await context.RoleNameReservations.SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (current is not null)
        {
            EfIdentityStoreSupport.EnsureRoleNameReservationIdentity(current, tenantId, current.NormalizedRoleName, roleId);
            context.RoleNameReservations.Remove(current);
        }
    }

    private static UserNameReservationEntity? ReservationForUserName(UserEntity user)
    {
        if (string.IsNullOrWhiteSpace(user.NormalizedUserName))
            return null;
        var normalized = user.NormalizedUserName;
        return new UserNameReservationEntity
        {
            Id = user.NormalizedUserNameKey!,
            TenantId = user.TenantId,
            TenantLookupKey = EfIdentityStoreSupport.TenantLookup(user.TenantId),
            NormalizedUserName = normalized,
            NormalizedUserNameKey = user.NormalizedUserNameKey!,
            UserId = user.UserId,
            Revision = 1
        };
    }

    private static EmailReservationEntity? ReservationForEmail(UserEntity user)
    {
        if (string.IsNullOrWhiteSpace(user.NormalizedEmail))
            return null;
        var normalized = user.NormalizedEmail;
        return new EmailReservationEntity
        {
            Id = user.NormalizedEmailKey!,
            TenantId = user.TenantId,
            TenantLookupKey = EfIdentityStoreSupport.TenantLookup(user.TenantId),
            NormalizedEmail = normalized,
            NormalizedEmailKey = user.NormalizedEmailKey!,
            UserId = user.UserId,
            Revision = 1
        };
    }

    private static RoleNameReservationEntity? ReservationForRoleName(RoleEntity role)
    {
        if (string.IsNullOrWhiteSpace(role.NormalizedName))
            return null;
        var normalized = role.NormalizedName;
        return new RoleNameReservationEntity
        {
            Id = role.NormalizedNameKey!,
            TenantId = role.TenantId,
            TenantLookupKey = EfIdentityStoreSupport.TenantLookup(role.TenantId),
            NormalizedRoleName = normalized,
            NormalizedRoleNameKey = role.NormalizedNameKey!,
            RoleId = role.RoleId,
            Revision = 1
        };
    }

    private static void CanonicalizeLookupKeys(UserEntity entity)
    {
        entity.NormalizedUserNameKey = string.IsNullOrWhiteSpace(entity.NormalizedUserName)
            ? null
            : EfIdentityStoreSupport.Lookup(entity.TenantId, entity.NormalizedUserName);
        entity.NormalizedEmailKey = string.IsNullOrWhiteSpace(entity.NormalizedEmail)
            ? null
            : EfIdentityStoreSupport.Lookup(entity.TenantId, entity.NormalizedEmail);
    }

    private static void CanonicalizeLookupKeys(RoleEntity entity) =>
        entity.NormalizedNameKey = string.IsNullOrWhiteSpace(entity.NormalizedName)
            ? null
            : EfIdentityStoreSupport.Lookup(entity.TenantId, entity.NormalizedName);

    private static void Apply(UserEntity entity, UserRecord user)
    {
        entity.TenantId = user.TenantId;
        entity.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(user.TenantId);
        entity.UserId = user.Id;
        entity.UserIdOrderKey = EfIdentityStoreSupport.SortableOrderKey(user.Id, nameof(user.Id));
        entity.UserName = user.UserName;
        entity.NormalizedUserName = string.IsNullOrWhiteSpace(user.UserName) ? null : EfIdentityStoreSupport.Normalize(user.UserName);
        entity.NormalizedUserNameKey = string.IsNullOrWhiteSpace(entity.NormalizedUserName) ? null : EfIdentityStoreSupport.Lookup(user.TenantId, entity.NormalizedUserName);
        entity.Email = user.Email;
        entity.NormalizedEmail = string.IsNullOrWhiteSpace(user.Email) ? null : EfIdentityStoreSupport.Normalize(user.Email);
        entity.NormalizedEmailKey = string.IsNullOrWhiteSpace(entity.NormalizedEmail) ? null : EfIdentityStoreSupport.Lookup(user.TenantId, entity.NormalizedEmail);
        entity.DisplayName = user.DisplayName;
        entity.Status = (int)user.Status;
        entity.Ownership = (int)user.Ownership;
        entity.RoleIdsJson = EfIdentityStoreSupport.SerializeSet(user.RoleIds);
        entity.DirectPermissionsJson = EfIdentityStoreSupport.SerializeSet(user.DirectPermissions);
    }

    private static void Apply(RoleEntity entity, RoleRecord role)
    {
        entity.TenantId = role.TenantId;
        entity.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(role.TenantId);
        entity.RoleId = role.Id;
        entity.RoleIdOrderKey = EfIdentityStoreSupport.SortableOrderKey(role.Id, nameof(role.Id));
        entity.Name = role.Name;
        entity.NormalizedName = string.IsNullOrWhiteSpace(role.Name) ? null : EfIdentityStoreSupport.Normalize(role.Name);
        entity.NormalizedNameKey = string.IsNullOrWhiteSpace(entity.NormalizedName) ? null : EfIdentityStoreSupport.Lookup(role.TenantId, entity.NormalizedName);
        entity.Description = role.Description;
        entity.PermissionsJson = EfIdentityStoreSupport.SerializeSet(role.Permissions);
        entity.System = role.System;
    }

    private static IReadOnlyList<string> ReadRegistry(string? json, string owner)
    {
        var values = EfIdentityStoreSupport.DeserializeSet(json)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (values.Length > EfIdentityStoreSupport.MaximumMaterializedListEntries)
            throw new IdentityEntityFrameworkAdmissionException($"The Identity {owner} registry exceeds the {EfIdentityStoreSupport.MaximumMaterializedListEntries}-entry limit.");
        return values;
    }

    private static void EnsureAggregateRelationshipCapacity(params IReadOnlyCollection<string>[] registries)
    {
        var count = registries.Sum(registry => registry.Count);
        if (count > EfIdentityStoreSupport.MaximumMaterializedListEntries)
            throw new IdentityEntityFrameworkAdmissionException($"The Identity aggregate relationship registry exceeds the {EfIdentityStoreSupport.MaximumMaterializedListEntries}-entry limit.");
    }

    private static T RequireRegistered<T>(T? entity, string kind, string id) where T : class
    {
        if (entity is not null)
            return entity;

        var message = $"Registered identity aggregate child '{kind}/{id}' does not exist.";
        throw new IdentityEntityFrameworkPersistenceException(message, new InvalidOperationException(message));
    }

    private static EfIdentityAuthorityConflict MapConflict(string? failedUnitId) =>
        failedUnitId switch
        {
            IdentityIamEfModule.UserNameReservationTableName => EfIdentityAuthorityConflict.UserName,
            IdentityIamEfModule.EmailReservationTableName => EfIdentityAuthorityConflict.Email,
            IdentityIamEfModule.RoleNameReservationTableName => EfIdentityAuthorityConflict.RoleName,
            _ => EfIdentityAuthorityConflict.None
        };

    private static string RemoveId(string json, string id)
    {
        var values = EfIdentityStoreSupport.DeserializeSet(json).Where(x => !string.Equals(x, id, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        return EfIdentityStoreSupport.SerializeSet(values);
    }

    private static string RemoveEquivalentId(string json, string id)
    {
        var values = EfIdentityStoreSupport.DeserializeSet(json).Where(x => !Same(x, id)).ToHashSet(StringComparer.Ordinal);
        return EfIdentityStoreSupport.SerializeSet(values);
    }

    private static bool CanWrite(long? current, long? expected) => expected is null || (current is null ? expected == 0 : current == expected);
    private static bool Same(string? left, string? right) => string.Equals(EfIdentityStoreSupport.Normalize(left), EfIdentityStoreSupport.Normalize(right), StringComparison.Ordinal);
    private static void ValidateIdentity(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength)
            throw new ArgumentException($"Identity key values cannot exceed {IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength} UTF-16 code units.", parameter);
        _ = EfIdentityStoreSupport.Normalize(value);
    }
    private static void ValidateUser(UserRecord user) { ValidateIdentity(user.TenantId, nameof(user.TenantId)); ValidateIdentity(user.Id, nameof(user.Id)); ArgumentNullException.ThrowIfNull(user.UserName); ArgumentNullException.ThrowIfNull(user.RoleIds); ArgumentNullException.ThrowIfNull(user.DirectPermissions); }
    private static void ValidateRole(RoleRecord role) { ValidateIdentity(role.TenantId, nameof(role.TenantId)); ValidateIdentity(role.Id, nameof(role.Id)); _ = EfIdentityStoreSupport.SortableOrderKey(role.Id, nameof(role.Id)); ArgumentNullException.ThrowIfNull(role.Name); ArgumentNullException.ThrowIfNull(role.Permissions); }
    private static EfIdentityWriteResult Inserted(string id, long version) => new(EfIdentityWriteStatus.Inserted, version, "Identity authority inserted.", id);
    private static EfIdentityWriteResult Updated(string id, long version) => new(EfIdentityWriteStatus.Updated, version, "Identity authority updated.", id);
    private static EfIdentityWriteResult Deleted(string id, long version) => new(EfIdentityWriteStatus.Deleted, version, "Identity authority deleted.", id);
    private static EfIdentityWriteResult NotFound(string? id = null) => new(EfIdentityWriteStatus.NotFound, null, "Identity authority was not found.", id);
    private static EfIdentityWriteResult Conflict(string? id = null) => new(EfIdentityWriteStatus.Conflict, null, "Identity authority write conflicted.", id);
}
