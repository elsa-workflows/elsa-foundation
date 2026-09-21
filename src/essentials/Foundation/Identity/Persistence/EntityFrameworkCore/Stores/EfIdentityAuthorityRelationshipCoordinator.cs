using System.Globalization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

public enum EfExternalLoginOwnershipPolicy
{
    CreateOrSameOwner,
    RevisionEnforcedRebind
}

/// <summary>
/// Atomically mutates Identity relationship rows and the registry/revision of every affected
/// authority owner. All operations execute through <see cref="EfIdentityAtomicWrite"/>.
/// </summary>
public sealed class EfIdentityAuthorityRelationshipCoordinator(
    IdentityIamDbContext context,
    EfIdentityAtomicWrite atomicWrite,
    IPersistenceAccessContextAccessor accessContextAccessor)
{
    private readonly IPersistenceAccessContextAccessor access = accessContextAccessor;
    public Task<EfIdentityWriteResult> AddUserClaimsAsync(string tenantId, string userId, long expectedUserVersion, IReadOnlyCollection<UserClaimEntity> claims, CancellationToken cancellationToken = default) =>
        MutateUserChildrenAsync("add-user-claims", tenantId, userId, expectedUserVersion, claims, UserRegistry.Claims, cancellationToken);

    public Task<EfIdentityWriteResult> RemoveUserClaimsAsync(string tenantId, string userId, long expectedUserVersion, IReadOnlyCollection<(string ClaimType, string? ClaimValue)> claims, CancellationToken cancellationToken = default) =>
        MutateUserChildrenAsync("remove-user-claims", tenantId, userId, expectedUserVersion, claims.Select(x => new UserClaimEntity { Id = ClaimId(tenantId, userId, x.ClaimType, x.ClaimValue), TenantId = tenantId, UserId = userId, ClaimType = x.ClaimType, ClaimValue = x.ClaimValue }).ToArray(), UserRegistry.Claims, cancellationToken, delete: true);

    public Task<EfIdentityWriteResult> ReplaceUserClaimAsync(string tenantId, string userId, long expectedUserVersion, string oldClaimType, string? oldClaimValue, UserClaimEntity replacement, CancellationToken cancellationToken = default)
    {
        var oldId = ClaimId(tenantId, userId, oldClaimType, oldClaimValue);
        var replacementId = ClaimId(tenantId, userId, replacement.ClaimType, replacement.ClaimValue);
        // Replacing a claim with itself is an upsert, not a delete followed by a
        // missing re-add. Keep one canonical child change in that case.
        return replacementId == oldId
            ? MutateUserChildrenAsync("replace-user-claim", tenantId, userId, expectedUserVersion, [replacement], UserRegistry.Claims, cancellationToken)
            : MutateUserChildrenAsync("replace-user-claim", tenantId, userId, expectedUserVersion,
                [replacement, new UserClaimEntity { Id = oldId, TenantId = tenantId, UserId = userId, ClaimType = oldClaimType, ClaimValue = oldClaimValue }],
                UserRegistry.Claims, cancellationToken, delete: false, replacementId: oldId);
    }

    public Task<EfIdentityWriteResult> SaveUserTokenAsync(string tenantId, string userId, long expectedUserVersion, UserTokenEntity token, CancellationToken cancellationToken = default) =>
        MutateUserTokenAsync("save-user-token", tenantId, userId, expectedUserVersion, token, delete: false, cancellationToken);

    public Task<EfIdentityWriteResult> DeleteUserTokenAsync(string tenantId, string userId, long expectedUserVersion, string loginProvider, string name, CancellationToken cancellationToken = default) =>
        MutateUserTokenAsync("delete-user-token", tenantId, userId, expectedUserVersion, new UserTokenEntity { Id = TokenId(tenantId, userId, loginProvider, name), TenantId = tenantId, UserId = userId, LoginProvider = loginProvider, Name = name }, delete: true, cancellationToken);

    public Task<EfIdentityWriteResult> RedeemRecoveryCodeAsync(string tenantId, string userId, long expectedUserVersion, string loginProvider, string name, string code, CancellationToken cancellationToken = default) =>
        MutateRecoveryCodeAsync(tenantId, userId, expectedUserVersion, loginProvider, name, code, cancellationToken);

    public Task<EfIdentityWriteResult> SaveRoleClaimAsync(string tenantId, string roleId, long expectedRoleVersion, RoleClaimEntity claim, CancellationToken cancellationToken = default) =>
        MutateRoleClaimAsync("save-role-claim", tenantId, roleId, expectedRoleVersion, claim, delete: false, cancellationToken);

    public Task<EfIdentityWriteResult> DeleteRoleClaimAsync(string tenantId, string roleId, long expectedRoleVersion, RoleClaimEntity claim, CancellationToken cancellationToken = default) =>
        MutateRoleClaimAsync("delete-role-claim", tenantId, roleId, expectedRoleVersion, claim, delete: true, cancellationToken);

    public Task<EfIdentityWriteResult> ReplaceRoleClaimAsync(
        string tenantId,
        string roleId,
        long expectedRoleVersion,
        string oldClaimType,
        string? oldClaimValue,
        RoleClaimEntity replacement,
        CancellationToken cancellationToken = default) =>
        ReplaceRoleClaimCoreAsync(tenantId, roleId, expectedRoleVersion, oldClaimType, oldClaimValue, replacement, cancellationToken);

    public Task<EfIdentityWriteResult> SaveTenantMembershipAsync(TenantMembershipEntity membership, long? expectedMembershipVersion, bool enforceMembershipVersion, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(membership, expectedMembershipVersion, enforceMembershipVersion, cancellationToken);

    public Task<EfIdentityWriteResult> SaveExternalIdentityAsync(
        ExternalIdentityEntity login,
        long? expectedNewOwnerVersion,
        long? expectedLoginVersion,
        bool enforceLoginVersion,
        EfExternalLoginOwnershipPolicy ownershipPolicy,
        bool returnOwnerResult,
        CancellationToken cancellationToken = default) =>
        MutateExternalIdentityAsync(login, expectedNewOwnerVersion, expectedLoginVersion, enforceLoginVersion, ownershipPolicy, returnOwnerResult, replaceProviderDisplayName: true, cancellationToken);

    internal Task<EfIdentityWriteResult> SaveExternalIdentityPreservingProviderDisplayNameAsync(
        ExternalIdentityEntity login,
        long? expectedNewOwnerVersion,
        long? expectedLoginVersion,
        bool enforceLoginVersion,
        EfExternalLoginOwnershipPolicy ownershipPolicy,
        bool returnOwnerResult,
        CancellationToken cancellationToken = default) =>
        MutateExternalIdentityAsync(login, expectedNewOwnerVersion, expectedLoginVersion, enforceLoginVersion, ownershipPolicy, returnOwnerResult, replaceProviderDisplayName: false, cancellationToken);

    public Task<EfIdentityWriteResult> DeleteExternalIdentityAsync(string tenantId, string userId, string provider, string providerSubject, long expectedUserVersion, CancellationToken cancellationToken = default) =>
        DeleteExternalIdentityCoreAsync(tenantId, userId, provider, providerSubject, expectedUserVersion, cancellationToken);

    public Task<EfIdentityWriteResult> AddUserRoleAsync(string tenantId, string userId, string roleId, long expectedUserVersion, UserRoleEntity link, CancellationToken cancellationToken = default) =>
        MutateUserRoleAsync(tenantId, userId, roleId, expectedUserVersion, link, delete: false, cancellationToken);

    public Task<EfIdentityWriteResult> DeleteUserRoleAsync(string tenantId, string userId, string roleId, long expectedUserVersion, CancellationToken cancellationToken = default) =>
        MutateUserRoleAsync(tenantId, userId, roleId, expectedUserVersion, new UserRoleEntity { Id = RoleLinkId(tenantId, userId, roleId), TenantId = tenantId, UserId = userId, RoleId = roleId }, delete: true, cancellationToken);

    private async Task<EfIdentityWriteResult> MutateUserChildrenAsync(string operation, string tenantId, string userId, long expectedVersion, IReadOnlyCollection<UserClaimEntity> claims, UserRegistry registry, CancellationToken cancellationToken, bool delete = false, string? replacementId = null)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var canonicalClaims = claims.ToArray();
        foreach (var claim in canonicalClaims)
            Prepare(claim, tenantId, userId);
        canonicalClaims = canonicalClaims
            .GroupBy(claim => claim.Id, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(claim => claim.Id, StringComparer.Ordinal)
            .ToArray();
        var fingerprintParts = new List<string?> { operation, tenantId, userId, expectedVersion.ToString(CultureInfo.InvariantCulture), delete.ToString(), replacementId };
        foreach (var claim in canonicalClaims.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            fingerprintParts.Add(claim.Id);
            fingerprintParts.Add(claim.ClaimType);
            fingerprintParts.Add(claim.ClaimValue);
        }
        var fingerprint = Fingerprint(fingerprintParts.ToArray());
        return await ExecuteAsync(tenantId, operation, fingerprint, async token =>
        {
            var user = await LoadUserAsync(tenantId, userId, token);
            if (user is null)
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound, null, "Identity user was not found.");
            if (user.Revision != expectedVersion)
                return Conflict(user.Id);
            var beforeCount = RelationshipCount(user);
            var ids = Registry(user, registry);
            // Both branches below read the same row by the same id, so the whole submitted set is loaded once.
            // The rows stay tracked and an id the load does not return is still an absent row, which is what the
            // add-or-update decision turns on.
            var existingClaims = await EfIdentityChildRows.LoadAsync(
                canonicalClaims.Select(claim => claim.Id).ToArray(),
                context.UserClaims,
                x => x.Id,
                token);
            foreach (var claim in canonicalClaims)
            {
                var id = claim.Id;
                var existing = existingClaims.GetValueOrDefault(id);
                if (delete || replacementId == id)
                {
                    if (existing is not null)
                    {
                        EfIdentityStoreSupport.EnsureUserClaimIdentity(existing, tenantId, userId);
                        context.UserClaims.Remove(existing);
                    }
                    ids.Remove(id);
                }
                else
                {
                    if (existing is null)
                    {
                        Prepare(claim, tenantId, userId);
                        context.UserClaims.Add(claim);
                    }
                    else
                    {
                        EfIdentityStoreSupport.EnsureUserClaimIdentity(existing, tenantId, userId);
                        // A relationship upsert advances the child revision just as the
                        // provider-neutral mutation batch does, even when its owner revision
                        // is the externally visible CAS boundary.
                        Apply(existing, claim);
                        existing.Revision = checked(existing.Revision + 1);
                    }
                    ids.Add(id);
                }
            }
            SetRegistry(user, registry, ids);
            EnsureUserRelationshipCapacity(user, beforeCount);
            user.Revision = checked(user.Revision + 1);
            return Updated(user.Id, user.Revision);
        }, cancellationToken);
    }

    private Task<EfIdentityWriteResult> MutateUserTokenAsync(string operation, string tenantId, string userId, long expectedVersion, UserTokenEntity tokenRow, bool delete, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, operation, Fingerprint(operation, tenantId, userId, expectedVersion.ToString(CultureInfo.InvariantCulture), tokenRow.LoginProvider, tokenRow.Name, tokenRow.Value), async token =>
        {
            var user = await LoadUserAsync(tenantId, userId, token);
            if (user is null)
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
            if (user.Revision != expectedVersion)
                return Conflict(user.Id);
            var id = TokenId(tenantId, userId, tokenRow.LoginProvider, tokenRow.Name);
            var existing = await context.UserTokens.SingleOrDefaultAsync(x => x.Id == id, token);
            if (existing is not null)
                EfIdentityStoreSupport.EnsureUserTokenIdentity(existing, tenantId, userId);
            var beforeCount = RelationshipCount(user);
            var ids = Registry(user, UserRegistry.Tokens);
            if (delete)
            {
                if (existing is not null)
                    context.UserTokens.Remove(existing);
                ids.Remove(id);
            }
            else
            {
                Prepare(tokenRow, tenantId, userId);
                tokenRow.Id = id;
                if (existing is null)
                    context.UserTokens.Add(tokenRow);
                else
                {
                    Apply(existing, tokenRow);
                    existing.Revision = tokenRow.Revision = checked(existing.Revision + 1);
                }
                ids.Add(id);
            }
            SetRegistry(user, UserRegistry.Tokens, ids);
            EnsureUserRelationshipCapacity(user, beforeCount);
            user.Revision = checked(user.Revision + 1);
            return Updated(user.Id, user.Revision);
        }, cancellationToken);

    private Task<EfIdentityWriteResult> MutateRecoveryCodeAsync(string tenantId, string userId, long expectedVersion, string provider, string name, string code, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, "redeem-recovery-code", Fingerprint("redeem-recovery-code", tenantId, userId, expectedVersion.ToString(CultureInfo.InvariantCulture), provider, name, code, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)), async token =>
        {
            var user = await LoadUserAsync(tenantId, userId, token);
            if (user is null || user.Revision != expectedVersion)
                return user is null ? new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound) : Conflict(user.Id);
            var id = TokenId(tenantId, userId, provider, name);
            var row = await context.UserTokens.SingleOrDefaultAsync(x => x.Id == id, token);
            if (row is null)
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
            EfIdentityStoreSupport.EnsureUserTokenIdentity(row, tenantId, userId);
            var codes = (row.Value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if (!codes.Remove(code))
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
            row.Value = string.Join(';', codes.Order(StringComparer.Ordinal));
            row.Revision = checked(row.Revision + 1);
            user.Revision = checked(user.Revision + 1);
            return Updated(user.Id, user.Revision);
        }, cancellationToken);

    private Task<EfIdentityWriteResult> MutateRoleClaimAsync(string operation, string tenantId, string roleId, long expectedVersion, RoleClaimEntity claim, bool delete, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, operation, Fingerprint(operation, tenantId, roleId, expectedVersion.ToString(CultureInfo.InvariantCulture), claim.ClaimType, claim.ClaimValue), async token =>
        {
            var role = await LoadRoleAsync(tenantId, roleId, token);
            if (role is null)
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
            if (role.Revision != expectedVersion)
                return Conflict(role.Id);
            var id = ClaimId(tenantId, roleId, claim.ClaimType, claim.ClaimValue);
            claim.Id = id;
            var existing = await context.RoleClaims.SingleOrDefaultAsync(x => x.Id == id, token);
            if (existing is not null)
                EfIdentityStoreSupport.EnsureRoleClaimIdentity(existing, tenantId, roleId);
            var beforeCount = RelationshipCount(role);
            var ids = EfIdentityStoreSupport.DeserializeSet(role.ClaimIdsJson).ToHashSet(StringComparer.Ordinal);
            if (delete)
            { if (existing is not null) context.RoleClaims.Remove(existing); ids.Remove(id); }
            else
            {
                Prepare(claim, tenantId, roleId);
                if (existing is null)
                    context.RoleClaims.Add(claim);
                else
                {
                    Apply(existing, claim);
                    existing.Revision = claim.Revision = checked(existing.Revision + 1);
                }
                ids.Add(id);
            }
            role.ClaimIdsJson = EfIdentityStoreSupport.SerializeSet(ids);
            EnsureRoleRelationshipCapacity(role, beforeCount);
            role.Revision = checked(role.Revision + 1);
            return Updated(role.Id, role.Revision);
        }, cancellationToken);

    private Task<EfIdentityWriteResult> ReplaceRoleClaimCoreAsync(
        string tenantId,
        string roleId,
        long expectedVersion,
        string oldClaimType,
        string? oldClaimValue,
        RoleClaimEntity replacement,
        CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, "replace-role-claim", Fingerprint("replace-role-claim", tenantId, roleId, expectedVersion.ToString(CultureInfo.InvariantCulture), oldClaimType, oldClaimValue, replacement.ClaimType, replacement.ClaimValue), async token =>
        {
            var role = await LoadRoleAsync(tenantId, roleId, token);
            if (role is null)
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
            if (role.Revision != expectedVersion)
                return Conflict(role.Id);

            var oldId = ClaimId(tenantId, roleId, oldClaimType, oldClaimValue);
            var newId = ClaimId(tenantId, roleId, replacement.ClaimType, replacement.ClaimValue);
            var oldRow = await context.RoleClaims.SingleOrDefaultAsync(x => x.Id == oldId, token);
            var newRow = oldId == newId ? oldRow : await context.RoleClaims.SingleOrDefaultAsync(x => x.Id == newId, token);
            if (oldRow is not null)
                EfIdentityStoreSupport.EnsureRoleClaimIdentity(oldRow, tenantId, roleId);
            if (newRow is not null && !ReferenceEquals(newRow, oldRow))
                EfIdentityStoreSupport.EnsureRoleClaimIdentity(newRow, tenantId, roleId);
            if (newRow is not null && oldId != newId)
                return Conflict(newId);
            var beforeCount = RelationshipCount(role);
            var ids = EfIdentityStoreSupport.DeserializeSet(role.ClaimIdsJson).ToHashSet(StringComparer.Ordinal);
            if (oldRow is not null && oldId != newId)
                context.RoleClaims.Remove(oldRow);
            Prepare(replacement, tenantId, roleId);
            if (newRow is null)
            { if (oldId == newId) newRow = oldRow; if (newRow is null) context.RoleClaims.Add(replacement); }
            else
            {
                Apply(newRow, replacement);
                newRow.Revision = replacement.Revision = checked(newRow.Revision + 1);
            }
            ids.Remove(oldId);
            ids.Add(newId);
            role.ClaimIdsJson = EfIdentityStoreSupport.SerializeSet(ids);
            EnsureRoleRelationshipCapacity(role, beforeCount);
            role.Revision = checked(role.Revision + 1);
            return Updated(role.Id, role.Revision);
        }, cancellationToken);

    private Task<EfIdentityWriteResult> MutateMembershipAsync(TenantMembershipEntity membership, long? expectedVersion, bool enforce, CancellationToken cancellationToken) =>
        ExecuteAsync(membership.TenantId, "save-tenant-membership", Fingerprint("save-tenant-membership", membership.TenantId, membership.UserId, membership.Status.ToString(CultureInfo.InvariantCulture), membership.RoleIdsJson, membership.DirectPermissionsJson, expectedVersion?.ToString(CultureInfo.InvariantCulture), enforce.ToString()), async token =>
        {
            var user = await LoadUserAsync(membership.TenantId, membership.UserId, token);
            if (user is null)
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
            var id = EfIdentityStoreSupport.RecordId(membership.TenantId, membership.UserId);
            var existing = await context.TenantMemberships.SingleOrDefaultAsync(x => x.Id == id, token);
            if (existing is not null)
                EfIdentityStoreSupport.EnsureTenantMembershipIdentity(existing, membership.TenantId, membership.UserId);
            if (enforce && expectedVersion is { } expected &&
                ((expected == 0 && existing is not null) || (expected > 0 && (existing is null || existing.Revision != expected))))
                return existing is null ? (expected == 0 ? new EfIdentityWriteResult(EfIdentityWriteStatus.Conflict, Message: "Identity membership already exists.") : new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound)) : Conflict(id);
            membership.Id = id;
            Prepare(membership, membership.TenantId, membership.UserId);
            if (existing is null)
            { membership.Revision = 1; context.TenantMemberships.Add(membership); }
            else
            { membership.Revision = checked(existing.Revision + 1); Apply(existing, membership); existing.Revision = membership.Revision; }
            var beforeCount = RelationshipCount(user);
            var ids = Registry(user, UserRegistry.TenantMemberships);
            ids.Add(id);
            SetRegistry(user, UserRegistry.TenantMemberships, ids);
            EnsureUserRelationshipCapacity(user, beforeCount);
            user.Revision = checked(user.Revision + 1);
            return Updated(id, membership.Revision);
        }, cancellationToken, replayByFingerprint: enforce);

    private async Task<EfIdentityWriteResult> MutateExternalIdentityAsync(ExternalIdentityEntity login, long? expectedNewOwnerVersion, long? expectedLoginVersion, bool enforceLoginVersion, EfExternalLoginOwnershipPolicy policy, bool returnOwnerResult, bool replaceProviderDisplayName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);
        _ = EfIdentityStoreSupport.ExternalOrderKey(login.Provider, login.ProviderSubject);
        var operation = "save-external-login";
        var fingerprint = Fingerprint(
            operation,
            login.TenantId,
            login.Provider,
            replaceProviderDisplayName ? "replace-display-name" : "preserve-display-name",
            replaceProviderDisplayName ? login.ProviderDisplayName : null,
            login.ProviderSubject,
            login.UserId,
            login.LinkedAt.ToString("O", CultureInfo.InvariantCulture),
            login.LastSeenAt?.ToString("O", CultureInfo.InvariantCulture),
            login.LinkPolicy.ToString(CultureInfo.InvariantCulture),
            expectedNewOwnerVersion?.ToString(CultureInfo.InvariantCulture),
            expectedLoginVersion?.ToString(CultureInfo.InvariantCulture),
            policy.ToString(),
            returnOwnerResult.ToString());
        return await ExecuteAsync(login.TenantId, operation, fingerprint, async token =>
        {
            var id = EfIdentityStoreSupport.CompoundKey(login.TenantId, login.Provider, login.ProviderSubject);
            login.Id = id;
            var existing = await context.ExternalIdentities.SingleOrDefaultAsync(x => x.Id == id, token);
            if (existing is not null)
                EfIdentityStoreSupport.EnsureExternalIdentity(existing, login.TenantId, login.Provider, login.ProviderSubject);
            if (existing is not null &&
                policy == EfExternalLoginOwnershipPolicy.CreateOrSameOwner &&
                !Same(existing.UserId, login.UserId))
                return Conflict(id);
            if (enforceLoginVersion && expectedLoginVersion is { } expected &&
                ((expected == 0 && existing is not null) || (expected > 0 && (existing is null || existing.Revision != expected))))
            {
                return existing is null
                    ? expected == 0
                        ? new EfIdentityWriteResult(EfIdentityWriteStatus.Conflict, Message: "Identity external identity already exists.")
                        : new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound)
                    : Conflict(id);
            }
            var owners = new[] { existing?.UserId, login.UserId }
                .Where(x => x is not null)
                .Select(x => EfIdentityStoreSupport.Normalize(x!))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var loaded = new Dictionary<string, UserEntity>(StringComparer.Ordinal);
            foreach (var owner in owners)
            {
                var row = await LoadUserAsync(login.TenantId, owner, token);
                if (row is null)
                    return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
                loaded.Add(owner, row);
            }

            var newOwnerKey = EfIdentityStoreSupport.Normalize(login.UserId);
            if (expectedNewOwnerVersion is { } newExpected &&
                loaded.TryGetValue(newOwnerKey, out var newOwner) &&
                newOwner.Revision != newExpected)
                return Conflict(newOwner.Id);

            Prepare(login, login.TenantId, login.UserId);
            if (existing is not null && !replaceProviderDisplayName)
                login.ProviderDisplayName = existing.ProviderDisplayName;
            var newRevision = existing is null ? 1 : checked(existing.Revision + 1);
            login.Revision = newRevision;
            if (existing is null)
                context.ExternalIdentities.Add(login);
            else
                Apply(existing, login);
            EfIdentityWriteResult? ownerResult = null;
            foreach (var pair in loaded)
            {
                var beforeCount = RelationshipCount(pair.Value);
                var ids = Registry(pair.Value, UserRegistry.Logins);
                if (string.Equals(pair.Key, newOwnerKey, StringComparison.Ordinal))
                {
                    ids.Add(id);
                }
                else
                    ids.Remove(id);
                SetRegistry(pair.Value, UserRegistry.Logins, ids);
                EnsureUserRelationshipCapacity(pair.Value, beforeCount);
                pair.Value.Revision = checked(pair.Value.Revision + 1);
                if (string.Equals(pair.Key, newOwnerKey, StringComparison.Ordinal))
                    ownerResult = Updated(pair.Value.Id, pair.Value.Revision);
            }
            return returnOwnerResult ? ownerResult! : Updated(id, newRevision);
        }, cancellationToken, replayByFingerprint: enforceLoginVersion || expectedNewOwnerVersion is not null);
    }

    private async Task<EfIdentityWriteResult> DeleteExternalIdentityCoreAsync(string tenantId, string userId, string provider, string providerSubject, long expectedVersion, CancellationToken cancellationToken) =>
        await ExecuteAsync(tenantId, "delete-external-login", Fingerprint("delete-external-login", tenantId, userId, provider, providerSubject, expectedVersion.ToString(CultureInfo.InvariantCulture)), async token =>
        {
            var user = await LoadUserAsync(tenantId, userId, token);
            if (user is null)
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
            if (user.Revision != expectedVersion)
                return Conflict(user.Id);
            var id = EfIdentityStoreSupport.CompoundKey(tenantId, provider, providerSubject);
            var login = await context.ExternalIdentities.SingleOrDefaultAsync(x => x.Id == id, token);
            if (login is not null)
            {
                EfIdentityStoreSupport.EnsureExternalIdentity(login, tenantId, provider, providerSubject, userId);
                context.ExternalIdentities.Remove(login);
            }
            var ids = Registry(user, UserRegistry.Logins);
            ids.Remove(id);
            SetRegistry(user, UserRegistry.Logins, ids);
            user.Revision = checked(user.Revision + 1);
            return Updated(user.Id, user.Revision);
        }, cancellationToken);

    private Task<EfIdentityWriteResult> MutateUserRoleAsync(string tenantId, string userId, string roleId, long expectedVersion, UserRoleEntity link, bool delete, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, delete ? "delete-user-role" : "add-user-role", Fingerprint(delete ? "delete-user-role" : "add-user-role", tenantId, userId, roleId, expectedVersion.ToString(CultureInfo.InvariantCulture)), async token =>
        {
            var user = await LoadUserAsync(tenantId, userId, token);
            var role = await LoadRoleAsync(tenantId, roleId, token);
            if (user is null || role is null)
                return new EfIdentityWriteResult(EfIdentityWriteStatus.NotFound);
            if (user.Revision != expectedVersion)
                return Conflict(user.Id);
            var id = EfIdentityStoreSupport.CompoundKey(tenantId, userId, roleId);
            var existing = await context.UserRoles.SingleOrDefaultAsync(x => x.Id == id, token);
            if (existing is not null)
                EfIdentityStoreSupport.EnsureUserRoleIdentity(existing, tenantId, userId, roleId);
            var beforeUserCount = RelationshipCount(user);
            var beforeRoleCount = RelationshipCount(role);
            var userIds = Registry(user, UserRegistry.RoleLinks);
            var roleIds = EfIdentityStoreSupport.DeserializeSet(role.UserLinkIdsJson).ToHashSet(StringComparer.Ordinal);
            var userRoleIds = EfIdentityStoreSupport.DeserializeSet(user.RoleIdsJson).ToHashSet(StringComparer.Ordinal);
            userRoleIds.RemoveWhere(existingRoleId => Same(existingRoleId, roleId));
            if (delete)
            { if (existing is not null) context.UserRoles.Remove(existing); userIds.Remove(id); roleIds.Remove(id); }
            else
            {
                link.Id = id;
                Prepare(link, tenantId, userId, roleId);
                if (existing is null)
                    context.UserRoles.Add(link);
                else
                {
                    Apply(existing, link);
                    existing.Revision = link.Revision = checked(existing.Revision + 1);
                }
                userIds.Add(id);
                roleIds.Add(id);
                userRoleIds.Add(roleId);
            }
            SetRegistry(user, UserRegistry.RoleLinks, userIds);
            role.UserLinkIdsJson = EfIdentityStoreSupport.SerializeSet(roleIds);
            user.RoleIdsJson = EfIdentityStoreSupport.SerializeSet(userRoleIds);
            EnsureUserRelationshipCapacity(user, beforeUserCount);
            EnsureRoleRelationshipCapacity(role, beforeRoleCount);
            user.Revision = checked(user.Revision + 1);
            role.Revision = checked(role.Revision + 1);
            return Updated(user.Id, user.Revision);
        }, cancellationToken);

    private async Task<UserEntity?> LoadUserAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        var user = await context.Users.SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, userId), cancellationToken);
        if (user is not null)
            EfIdentityStoreSupport.EnsureUserIdentity(user, tenantId, userId);
        return user;
    }

    private async Task<RoleEntity?> LoadRoleAsync(string tenantId, string roleId, CancellationToken cancellationToken)
    {
        var role = await context.Roles.SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, roleId), cancellationToken);
        if (role is not null)
            EfIdentityStoreSupport.EnsureRoleIdentity(role, tenantId, roleId);
        return role;
    }

    private Task<EfIdentityWriteResult> ExecuteAsync(
        string tenantId,
        string operation,
        string fingerprint,
        Func<CancellationToken, Task<EfIdentityWriteResult>> stage,
        CancellationToken cancellationToken,
        bool replayByFingerprint = true)
    {
        EfIdentityStoreSupport.EnsureTenant(access, tenantId);
        return atomicWrite.ExecuteAsync(
            EfIdentityAtomicMutation.Create(
                operation,
                fingerprint,
                tenantId,
                replayByFingerprint ? fingerprint : null),
            stage,
            cancellationToken).AsTask();
    }

    private static void Prepare(UserClaimEntity row, string tenant, string user) { row.TenantId = tenant; row.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(tenant); row.UserId = user; row.UserLookupKey = EfIdentityStoreSupport.Lookup(tenant, user); row.ClaimKey = EfIdentityStoreSupport.CompoundKey(tenant, row.ClaimType, row.ClaimValue); row.Id = ClaimId(tenant, user, row.ClaimType, row.ClaimValue); row.Revision = row.Revision == 0 ? 1 : row.Revision; }
    private static void Prepare(RoleClaimEntity row, string tenant, string role) { row.TenantId = tenant; row.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(tenant); row.RoleId = role; row.RoleLookupKey = EfIdentityStoreSupport.Lookup(tenant, role); row.ClaimKey = EfIdentityStoreSupport.CompoundKey(tenant, row.ClaimType, row.ClaimValue); row.Id = ClaimId(tenant, role, row.ClaimType, row.ClaimValue); row.Revision = row.Revision == 0 ? 1 : row.Revision; }
    private static void Prepare(UserRoleEntity row, string tenant, string user, string role) { row.TenantId = tenant; row.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(tenant); row.UserId = user; row.UserLookupKey = EfIdentityStoreSupport.Lookup(tenant, user); row.RoleId = role; row.RoleLookupKey = EfIdentityStoreSupport.Lookup(tenant, role); row.Revision = row.Revision == 0 ? 1 : row.Revision; }
    private static void Prepare(UserTokenEntity row, string tenant, string user) { row.TenantId = tenant; row.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(tenant); row.UserId = user; row.UserLookupKey = EfIdentityStoreSupport.Lookup(tenant, user); row.TokenKey = EfIdentityStoreSupport.CompoundKey(tenant, row.LoginProvider, row.Name); row.Revision = row.Revision == 0 ? 1 : row.Revision; }
    private static void Prepare(ExternalIdentityEntity row, string tenant, string user) { row.TenantId = tenant; row.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(tenant); row.ProviderLookupKey = EfIdentityStoreSupport.Lookup(tenant, row.Provider); row.ProviderSubjectLookupKey = EfIdentityStoreSupport.Lookup(tenant, row.ProviderSubject); row.ExternalOrderKey = EfIdentityStoreSupport.ExternalOrderKey(row.Provider, row.ProviderSubject); row.UserLookupKey = EfIdentityStoreSupport.Lookup(tenant, user); row.Revision = row.Revision == 0 ? 1 : row.Revision; }
    private static void Prepare(TenantMembershipEntity row, string tenant, string user) { row.TenantId = tenant; row.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(tenant); row.UserId = user; row.UserLookupKey = EfIdentityStoreSupport.Lookup(tenant, user); row.Revision = row.Revision == 0 ? 1 : row.Revision; }
    private static void Apply(UserClaimEntity target, UserClaimEntity source) { target.TenantId = source.TenantId; target.TenantLookupKey = source.TenantLookupKey; target.UserId = source.UserId; target.UserLookupKey = source.UserLookupKey; target.ClaimType = source.ClaimType; target.ClaimValue = source.ClaimValue; target.ClaimKey = source.ClaimKey; }
    private static void Apply(RoleClaimEntity target, RoleClaimEntity source) { target.TenantId = source.TenantId; target.TenantLookupKey = source.TenantLookupKey; target.RoleId = source.RoleId; target.RoleLookupKey = source.RoleLookupKey; target.ClaimType = source.ClaimType; target.ClaimValue = source.ClaimValue; target.ClaimKey = source.ClaimKey; }
    private static void Apply(UserTokenEntity target, UserTokenEntity source) { target.TenantId = source.TenantId; target.TenantLookupKey = source.TenantLookupKey; target.UserId = source.UserId; target.UserLookupKey = source.UserLookupKey; target.LoginProvider = source.LoginProvider; target.Name = source.Name; target.TokenKey = source.TokenKey; target.Value = source.Value; }
    private static void Apply(UserRoleEntity target, UserRoleEntity source) { target.TenantId = source.TenantId; target.TenantLookupKey = source.TenantLookupKey; target.UserId = source.UserId; target.UserLookupKey = source.UserLookupKey; target.RoleId = source.RoleId; target.RoleLookupKey = source.RoleLookupKey; }
    private static void Apply(ExternalIdentityEntity target, ExternalIdentityEntity source) { target.TenantId = source.TenantId; target.TenantLookupKey = source.TenantLookupKey; target.UserId = source.UserId; target.UserLookupKey = source.UserLookupKey; target.Provider = source.Provider; target.ProviderDisplayName = source.ProviderDisplayName; target.ProviderLookupKey = source.ProviderLookupKey; target.ProviderSubject = source.ProviderSubject; target.ProviderSubjectLookupKey = source.ProviderSubjectLookupKey; target.ExternalOrderKey = source.ExternalOrderKey; target.LinkedAt = source.LinkedAt; target.LastSeenAt = source.LastSeenAt; target.LinkPolicy = source.LinkPolicy; target.Revision = source.Revision; }
    private static void Apply(TenantMembershipEntity target, TenantMembershipEntity source) { target.Status = source.Status; target.RoleIdsJson = source.RoleIdsJson; target.DirectPermissionsJson = source.DirectPermissionsJson; }

    private static HashSet<string> Registry(UserEntity user, UserRegistry registry) => EfIdentityStoreSupport.DeserializeSet(registry switch { UserRegistry.Claims => user.ClaimIdsJson, UserRegistry.Logins => user.LoginIdsJson, UserRegistry.RoleLinks => user.RoleLinkIdsJson, UserRegistry.Tokens => user.TokenIdsJson, UserRegistry.TenantMemberships => user.TenantMembershipIdsJson, _ => "[]" }).ToHashSet(StringComparer.Ordinal);
    private static int RelationshipCount(UserEntity user)
    {
        var count = 0;
        foreach (var registry in Enum.GetValues<UserRegistry>())
            count += Registry(user, registry).Count;
        return count;
    }

    private static int RelationshipCount(RoleEntity role)
    {
        return EfIdentityStoreSupport.DeserializeSet(role.ClaimIdsJson).Count +
               EfIdentityStoreSupport.DeserializeSet(role.UserLinkIdsJson).Count;
    }

    private static void EnsureUserRelationshipCapacity(UserEntity user, int previousCount) =>
        EnsureRelationshipCapacity(RelationshipCount(user), previousCount, "user");

    private static void EnsureRoleRelationshipCapacity(RoleEntity role, int previousCount) =>
        EnsureRelationshipCapacity(RelationshipCount(role), previousCount, "role");

    private static void EnsureRelationshipCapacity(int nextCount, int previousCount, string owner)
    {
        if (nextCount > EfIdentityStoreSupport.MaximumMaterializedListEntries && nextCount > previousCount)
            throw new IdentityEntityFrameworkAdmissionException($"The Identity {owner} relationship registry exceeds the {EfIdentityStoreSupport.MaximumMaterializedListEntries}-entry limit.");
    }

    private static void SetRegistry(UserEntity user, UserRegistry registry, HashSet<string> values) { var json = EfIdentityStoreSupport.SerializeSet(values); switch (registry) { case UserRegistry.Claims: user.ClaimIdsJson = json; break; case UserRegistry.Logins: user.LoginIdsJson = json; break; case UserRegistry.RoleLinks: user.RoleLinkIdsJson = json; break; case UserRegistry.Tokens: user.TokenIdsJson = json; break; case UserRegistry.TenantMemberships: user.TenantMembershipIdsJson = json; break; } }
    private static string ClaimId(string tenant, string owner, string type, string? value) => EfIdentityStoreSupport.CompoundKey(tenant, owner, type, value);
    private static string TokenId(string tenant, string user, string provider, string name) => EfIdentityStoreSupport.CompoundKey(tenant, user, provider, name);
    private static string RoleLinkId(string tenant, string user, string role) => EfIdentityStoreSupport.CompoundKey(tenant, user, role);
    private static string Fingerprint(params string?[] values) => EfIdentityStoreSupport.Fingerprint(values);
    private static bool Same(string? x, string? y) => string.Equals(EfIdentityStoreSupport.Normalize(x), EfIdentityStoreSupport.Normalize(y), StringComparison.Ordinal);
    private static EfIdentityWriteResult Updated(string id, long revision) => new(EfIdentityWriteStatus.Updated, revision, "Identity relationship updated.", id);
    private static EfIdentityWriteResult Conflict(string? id = null) => new(EfIdentityWriteStatus.Conflict, null, "Identity relationship conflicted.", id);
    private enum UserRegistry { Claims, Logins, RoleLinks, Tokens, TenantMemberships }
}
