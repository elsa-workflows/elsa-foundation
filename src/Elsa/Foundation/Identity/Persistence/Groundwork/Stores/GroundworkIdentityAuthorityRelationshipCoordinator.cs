using System.Globalization;
using System.Text.Json;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Groundwork.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

public enum GroundworkExternalLoginOwnershipPolicy
{
    CreateOrSameOwner,
    RevisionEnforcedRebind
}

/// <summary>
/// Owns every identity authority relationship mutation. Deterministic children and all affected
/// owner registries are changed by CAS in one replay-safe Groundwork unit of work.
/// </summary>
public sealed class GroundworkIdentityAuthorityRelationshipCoordinator
{
    private readonly GroundworkIdentityAtomicWrite _atomicWrite;

    public GroundworkIdentityAuthorityRelationshipCoordinator(GroundworkIdentityAtomicWrite atomicWrite) =>
        _atomicWrite = atomicWrite ?? throw new ArgumentNullException(nameof(atomicWrite));

    internal GroundworkIdentityAuthorityRelationshipCoordinator(GroundworkIdentityRowStore rows)
        : this(new GroundworkIdentityAtomicWrite(rows))
    {
    }

    public static GroundworkIdentityAuthorityRelationshipCoordinator ForRows(GroundworkIdentityRowStore rows) =>
        new(rows);

    public Task<GroundworkIdentityWriteResult> AddUserClaimsAsync(
        string tenantId,
        string userId,
        long expectedUserVersion,
        IReadOnlyCollection<IdentityUserClaimDocument> claims,
        CancellationToken cancellationToken) =>
        MutateUserChildrenAsync(
            "add-user-claims",
            tenantId,
            userId,
            expectedUserVersion,
            UserRegistry.Claims,
            claims.Select(claim => ChildChange.Upsert(
                IdentityStorageManifest.UserClaimDocumentKind,
                IdentityDocumentId.From(tenantId, userId, claim.ClaimType, claim.ClaimValue),
                claim)).ToArray(),
            cancellationToken);

    public Task<GroundworkIdentityWriteResult> RemoveUserClaimsAsync(
        string tenantId,
        string userId,
        long expectedUserVersion,
        IReadOnlyCollection<(string ClaimType, string? ClaimValue)> claims,
        CancellationToken cancellationToken) =>
        MutateUserChildrenAsync(
            "remove-user-claims",
            tenantId,
            userId,
            expectedUserVersion,
            UserRegistry.Claims,
            claims.Select(claim => ChildChange.Delete(
                IdentityStorageManifest.UserClaimDocumentKind,
                IdentityDocumentId.From(tenantId, userId, claim.ClaimType, claim.ClaimValue))).ToArray(),
            cancellationToken);

    public Task<GroundworkIdentityWriteResult> ReplaceUserClaimAsync(
        string tenantId,
        string userId,
        long expectedUserVersion,
        string oldClaimType,
        string? oldClaimValue,
        IdentityUserClaimDocument replacement,
        CancellationToken cancellationToken) =>
        MutateUserChildrenAsync(
            "replace-user-claim",
            tenantId,
            userId,
            expectedUserVersion,
            UserRegistry.Claims,
            [
                ChildChange.Delete(
                    IdentityStorageManifest.UserClaimDocumentKind,
                    IdentityDocumentId.From(tenantId, userId, oldClaimType, oldClaimValue)),
                ChildChange.Upsert(
                    IdentityStorageManifest.UserClaimDocumentKind,
                    IdentityDocumentId.From(tenantId, userId, replacement.ClaimType, replacement.ClaimValue),
                    replacement)
            ],
            cancellationToken);

    public Task<GroundworkIdentityWriteResult> SaveUserTokenAsync(
        string tenantId,
        string userId,
        long expectedUserVersion,
        IdentityUserTokenDocument token,
        CancellationToken cancellationToken) =>
        MutateUserChildrenAsync(
            "save-user-token",
            tenantId,
            userId,
            expectedUserVersion,
            UserRegistry.Tokens,
            [ChildChange.Upsert(
                IdentityStorageManifest.UserTokenDocumentKind,
                IdentityDocumentId.From(tenantId, userId, token.LoginProvider, token.Name),
                token)],
            cancellationToken);

    public Task<GroundworkIdentityWriteResult> DeleteUserTokenAsync(
        string tenantId,
        string userId,
        long expectedUserVersion,
        string loginProvider,
        string name,
        CancellationToken cancellationToken) =>
        MutateUserChildrenAsync(
            "delete-user-token",
            tenantId,
            userId,
            expectedUserVersion,
            UserRegistry.Tokens,
            [ChildChange.Delete(
                IdentityStorageManifest.UserTokenDocumentKind,
                IdentityDocumentId.From(tenantId, userId, loginProvider, name))],
            cancellationToken);

    public Task<GroundworkIdentityWriteResult> RedeemRecoveryCodeAsync(
        string tenantId,
        string userId,
        long expectedUserVersion,
        string loginProvider,
        string name,
        string code,
        CancellationToken cancellationToken)
    {
        var tokenId = IdentityDocumentId.From(tenantId, userId, loginProvider, name);
        // A redemption invocation is a contender, not an idempotent command shared by all callers.
        // Keep its receipt stable for this attempt while ensuring concurrent contenders cannot replay
        // the winner's successful receipt and each must pass the token CAS.
        var fingerprint = IdentityRequestFingerprint.FromParts(
            Normalize(tenantId), Normalize(userId), expectedUserVersion.ToString(CultureInfo.InvariantCulture),
            tokenId, code, Guid.NewGuid().ToString("N"));
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "redeem-recovery-code",
            fingerprint,
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.UserTokenDocumentKind);

        return _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var userEnvelope = await LoadUserAsync(unitOfWork, tenantId, userId, token);
                var tokenEnvelope = await unitOfWork.ReadAsync(
                    IdentityStorageManifest.UserTokenDocumentKind,
                    tokenId,
                    token);
                if (tokenEnvelope is null)
                    return NotFound(tokenId);

                var tokenDocument = Deserialize<IdentityUserTokenDocument>(tokenEnvelope);
                ValidateUserChild(tokenDocument.TenantId, tokenDocument.UserId, tenantId, userId);
                var codes = (tokenDocument.Value ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.Ordinal);
                if (!codes.Remove(code))
                    return NotFound(tokenId);

                var updatedToken = tokenDocument with { Value = string.Join(';', codes.Order(StringComparer.Ordinal)) };
                var tokenResult = await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.UserTokenDocumentKind,
                    tokenId,
                    updatedToken,
                    tokenEnvelope.Version,
                    token);
                if (!tokenResult.Succeeded)
                    return tokenResult;

                var existingUser = Deserialize<IdentityUserDocument>(userEnvelope);
                var userDocument = existingUser with
                {
                    TokenIds = AddSorted(existingUser.TokenIds, tokenId)
                };
                EnsureUserRelationshipCapacity(existingUser, userDocument);
                return await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.IdentityUserDocumentKind,
                    userEnvelope.Id,
                    userDocument,
                    expectedUserVersion,
                    token);
            },
            cancellationToken).AsTask();
    }

    public Task<GroundworkIdentityWriteResult> SaveRoleClaimAsync(
        string tenantId,
        string roleId,
        long expectedRoleVersion,
        IdentityRoleClaimDocument claim,
        CancellationToken cancellationToken) =>
        MutateRoleClaimAsync("save-role-claim", tenantId, roleId, expectedRoleVersion, claim, delete: false, cancellationToken);

    public Task<GroundworkIdentityWriteResult> DeleteRoleClaimAsync(
        string tenantId,
        string roleId,
        long expectedRoleVersion,
        IdentityRoleClaimDocument claim,
        CancellationToken cancellationToken) =>
        MutateRoleClaimAsync("delete-role-claim", tenantId, roleId, expectedRoleVersion, claim, delete: true, cancellationToken);

    public Task<GroundworkIdentityWriteResult> SaveTenantMembershipAsync(
        IdentityTenantMembershipDocument membership,
        long? expectedMembershipVersion,
        bool enforceMembershipVersion,
        CancellationToken cancellationToken) =>
        MutateUserChildrenAsync(
            "save-tenant-membership",
            membership.TenantId,
            membership.UserId,
            expectedUserVersion: null,
            UserRegistry.TenantMemberships,
            [ChildChange.Upsert(
                IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
                IdentityCompositeDocumentId.From(membership.TenantId, membership.UserId),
                membership,
                expectedMembershipVersion,
                enforceMembershipVersion)],
            cancellationToken,
            returnChildResult: true);

    public Task<GroundworkIdentityWriteResult> SaveExternalLoginAsync(
        IdentityExternalLoginDocument login,
        long? expectedNewOwnerVersion,
        long? expectedLoginVersion,
        bool enforceLoginVersion,
        GroundworkExternalLoginOwnershipPolicy ownershipPolicy,
        bool returnOwnerResult,
        CancellationToken cancellationToken)
    {
        ValidateExternalLogin(login);
        if (!Enum.IsDefined(ownershipPolicy))
            throw new ArgumentOutOfRangeException(nameof(ownershipPolicy), ownershipPolicy, null);
        var loginId = IdentityCompositeDocumentId.From(login.TenantId, login.LoginProvider, login.ProviderKey);
        var fingerprint = IdentityRequestFingerprint.FromParts(
            Serialize(login), expectedNewOwnerVersion?.ToString(CultureInfo.InvariantCulture),
            expectedLoginVersion?.ToString(CultureInfo.InvariantCulture), enforceLoginVersion.ToString(),
            ownershipPolicy.ToString(),
            returnOwnerResult.ToString());
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "save-external-login",
            fingerprint,
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.ExternalLoginDocumentKind);

        return _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var existingLoginEnvelope = await unitOfWork.ReadAsync(
                    IdentityStorageManifest.ExternalLoginDocumentKind,
                    loginId,
                    token);
                IdentityExternalLoginDocument? existingLogin = null;
                if (existingLoginEnvelope is not null)
                {
                    existingLogin = Deserialize<IdentityExternalLoginDocument>(existingLoginEnvelope);
                    if (!Same(existingLogin.TenantId, login.TenantId))
                        throw new InvalidOperationException("The external login belongs to a different tenant.");
                    if (ownershipPolicy is GroundworkExternalLoginOwnershipPolicy.CreateOrSameOwner &&
                        !Same(existingLogin.UserId, login.UserId))
                        return GroundworkIdentityWriteResult.ConcurrencyConflict(loginId);
                }

                var ownerIds = new[] { existingLogin?.UserId, login.UserId }
                    .Where(static value => value is not null)
                    .Select(static value => value!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var owners = new Dictionary<string, GroundworkIdentityRow>(StringComparer.Ordinal);
                foreach (var ownerId in ownerIds)
                    owners[ownerId] = await LoadUserAsync(unitOfWork, login.TenantId, ownerId, token);

                var childExpectedVersion = enforceLoginVersion
                    ? expectedLoginVersion
                    : existingLoginEnvelope?.Version ?? 0;
                var loginResult = await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.ExternalLoginDocumentKind,
                    loginId,
                    login,
                    childExpectedVersion,
                    token);
                if (!loginResult.Succeeded)
                    return loginResult;

                GroundworkIdentityWriteResult? newOwnerResult = null;
                foreach (var ownerId in ownerIds)
                {
                    var ownerEnvelope = owners[ownerId];
                    var owner = Deserialize<IdentityUserDocument>(ownerEnvelope);
                    var ownsAfter = Same(ownerId, login.UserId);
                    var updated = owner with
                    {
                        LoginIds = ownsAfter
                            ? AddSorted(owner.LoginIds, loginId)
                            : RemoveSorted(owner.LoginIds, loginId)
                    };
                    EnsureUserRelationshipCapacity(owner, updated);
                    var expectedOwnerVersion = ownsAfter && expectedNewOwnerVersion is not null
                        ? expectedNewOwnerVersion
                        : ownerEnvelope.Version;
                    var ownerResult = await SaveAsync(
                        unitOfWork,
                        IdentityStorageManifest.IdentityUserDocumentKind,
                        ownerEnvelope.Id,
                        updated,
                        expectedOwnerVersion,
                        token);
                    if (!ownerResult.Succeeded)
                        return ownerResult;
                    if (ownsAfter)
                        newOwnerResult = ownerResult;
                }

                return returnOwnerResult ? newOwnerResult! : loginResult;
            },
            cancellationToken).AsTask();
    }

    public Task<GroundworkIdentityWriteResult> DeleteExternalLoginAsync(
        string tenantId,
        string userId,
        string loginProvider,
        string providerKey,
        long expectedUserVersion,
        CancellationToken cancellationToken)
    {
        var loginId = IdentityCompositeDocumentId.From(tenantId, loginProvider, providerKey);
        var fingerprint = IdentityRequestFingerprint.FromParts(
            Normalize(tenantId), Normalize(userId), Normalize(loginProvider), Normalize(providerKey),
            expectedUserVersion.ToString(CultureInfo.InvariantCulture));
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "delete-external-login",
            fingerprint,
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.ExternalLoginDocumentKind);

        return _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var userEnvelope = await LoadUserAsync(unitOfWork, tenantId, userId, token);
                var loginEnvelope = await unitOfWork.ReadAsync(
                    IdentityStorageManifest.ExternalLoginDocumentKind,
                    loginId,
                    token);
                if (loginEnvelope is not null)
                {
                    var existingLogin = Deserialize<IdentityExternalLoginDocument>(loginEnvelope);
                    ValidateUserChild(existingLogin.TenantId, existingLogin.UserId, tenantId, userId);
                    var deleteResult = unitOfWork.Delete(
                        new GroundworkIdentityRowDelete(
                            IdentityStorageManifest.ExternalLoginDocumentKind,
                            loginId,
                            GroundworkIdentityRowWriteCondition.IfVersion(loginEnvelope.Version)),
                        token);
                    if (deleteResult.Status is not WriteOutcomeStatus.Deleted)
                        return deleteResult;
                }

                var user = Deserialize<IdentityUserDocument>(userEnvelope);
                return await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.IdentityUserDocumentKind,
                    userEnvelope.Id,
                    user with { LoginIds = RemoveSorted(user.LoginIds, loginId) },
                    expectedUserVersion,
                    token);
            },
            cancellationToken).AsTask();
    }

    public Task<GroundworkIdentityWriteResult> AddUserRoleAsync(
        string tenantId,
        string userId,
        string roleId,
        long expectedUserVersion,
        IdentityUserRoleDocument link,
        CancellationToken cancellationToken) =>
        MutateUserRoleAsync("add-user-role", tenantId, userId, roleId, expectedUserVersion, link, delete: false, cancellationToken);

    public Task<GroundworkIdentityWriteResult> DeleteUserRoleAsync(
        string tenantId,
        string userId,
        string roleId,
        long expectedUserVersion,
        CancellationToken cancellationToken) =>
        MutateUserRoleAsync("delete-user-role", tenantId, userId, roleId, expectedUserVersion, document: null, delete: true, cancellationToken);

    private Task<GroundworkIdentityWriteResult> MutateUserChildrenAsync(
        string operation,
        string tenantId,
        string userId,
        long? expectedUserVersion,
        UserRegistry registry,
        IReadOnlyCollection<ChildChange> changes,
        CancellationToken cancellationToken,
        bool returnChildResult = false)
    {
        var orderedChanges = changes
            .GroupBy(change => (change.DocumentKind, change.Id))
            .Select(group => group.Last())
            .OrderBy(change => change.DocumentKind, StringComparer.Ordinal)
            .ThenBy(change => change.Id, StringComparer.Ordinal)
            .ToArray();
        var fingerprintParts = new[]
            {
                Normalize(tenantId), Normalize(userId), registry.ToString(),
                expectedUserVersion?.ToString(CultureInfo.InvariantCulture)
            }
            .Concat(orderedChanges.Select(change => change.Fingerprint))
            .ToArray();
        var fingerprint = IdentityRequestFingerprint.FromParts(fingerprintParts);
        var mutation = GroundworkIdentityAtomicMutation.Create(
            operation,
            fingerprint,
            [IdentityStorageManifest.IdentityUserDocumentKind, .. orderedChanges.Select(change => change.DocumentKind)]);

        return _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var userEnvelope = await LoadUserAsync(unitOfWork, tenantId, userId, token);
                var user = Deserialize<IdentityUserDocument>(userEnvelope);
                IReadOnlyCollection<string> ids = GetRegistry(user, registry) ?? [];
                GroundworkIdentityWriteResult? childResult = null;
                foreach (var change in orderedChanges)
                {
                    var existing = await unitOfWork.ReadAsync(change.DocumentKind, change.Id, token);
                    if (change.DeleteChild)
                    {
                        if (existing is not null)
                        {
                            ValidateExistingUserChild(existing, tenantId, userId);
                            childResult = unitOfWork.Delete(
                                new GroundworkIdentityRowDelete(change.DocumentKind, change.Id, GroundworkIdentityRowWriteCondition.IfVersion(existing.Version)),
                                token);
                            if (childResult.Status is not WriteOutcomeStatus.Deleted)
                                return childResult;
                        }

                        ids = RemoveSorted(ids, change.Id);
                        continue;
                    }

                    ValidateChildOwner(change.Document!, tenantId, userId);
                    var childExpectedVersion = change.EnforceExpectedVersion
                        ? change.ExpectedVersion
                        : existing?.Version ?? 0;
                    childResult = await SaveAsync(
                        unitOfWork,
                        change.DocumentKind,
                        change.Id,
                        change.Document!,
                        childExpectedVersion,
                        token);
                    if (!childResult.Succeeded)
                        return childResult;
                    ids = AddSorted(ids, change.Id);
                }

                var updatedUser = SetRegistry(user, registry, ids);
                EnsureUserRelationshipCapacity(user, updatedUser);
                var ownerResult = await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.IdentityUserDocumentKind,
                    userEnvelope.Id,
                    updatedUser,
                    expectedUserVersion ?? userEnvelope.Version,
                    token);
                if (!ownerResult.Succeeded)
                    return ownerResult;
                return returnChildResult && childResult is not null ? childResult : ownerResult;
            },
            cancellationToken).AsTask();
    }

    private Task<GroundworkIdentityWriteResult> MutateRoleClaimAsync(
        string operation,
        string tenantId,
        string roleId,
        long expectedRoleVersion,
        IdentityRoleClaimDocument claim,
        bool delete,
        CancellationToken cancellationToken)
    {
        ValidateRoleChild(claim.TenantId, claim.RoleId, tenantId, roleId);
        ValidateLookupKey(
            claim.RoleLookupKey,
            IdentityDocumentId.From(tenantId, roleId),
            "role-claim owner lookup key");
        var claimId = IdentityDocumentId.From(tenantId, roleId, claim.ClaimType, claim.ClaimValue);
        var fingerprint = IdentityRequestFingerprint.FromParts(
            Serialize(claim), expectedRoleVersion.ToString(CultureInfo.InvariantCulture), delete.ToString());
        var mutation = GroundworkIdentityAtomicMutation.Create(
            operation,
            fingerprint,
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.RoleClaimDocumentKind);
        return _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var roleEnvelope = await LoadRoleAsync(unitOfWork, tenantId, roleId, token);
                var role = Deserialize<IdentityRoleDocument>(roleEnvelope);
                var existing = await unitOfWork.ReadAsync(IdentityStorageManifest.RoleClaimDocumentKind, claimId, token);
                if (delete)
                {
                    if (existing is not null)
                    {
                        var existingClaim = Deserialize<IdentityRoleClaimDocument>(existing);
                        ValidateRoleChild(existingClaim.TenantId, existingClaim.RoleId, tenantId, roleId);
                        var deleteResult = unitOfWork.Delete(
                            new GroundworkIdentityRowDelete(IdentityStorageManifest.RoleClaimDocumentKind, claimId, GroundworkIdentityRowWriteCondition.IfVersion(existing.Version)), token);
                        if (deleteResult.Status is not WriteOutcomeStatus.Deleted)
                            return deleteResult;
                    }
                }
                else
                {
                    var claimResult = await SaveAsync(
                        unitOfWork, IdentityStorageManifest.RoleClaimDocumentKind, claimId, claim, existing?.Version ?? 0, token);
                    if (!claimResult.Succeeded)
                        return claimResult;
                }

                var updated = role with
                {
                    ClaimIds = delete ? RemoveSorted(role.ClaimIds, claimId) : AddSorted(role.ClaimIds, claimId)
                };
                EnsureRoleRelationshipCapacity(role, updated);
                return await SaveAsync(
                    unitOfWork, IdentityStorageManifest.IdentityRoleDocumentKind, roleEnvelope.Id,
                    updated, expectedRoleVersion, token);
            },
            cancellationToken).AsTask();
    }

    private Task<GroundworkIdentityWriteResult> MutateUserRoleAsync(
        string operation,
        string tenantId,
        string userId,
        string roleId,
        long expectedUserVersion,
        IdentityUserRoleDocument? document,
        bool delete,
        CancellationToken cancellationToken)
    {
        if (document is not null)
        {
            ValidateUserChild(document.TenantId, document.UserId, tenantId, userId);
            if (!Same(document.RoleId, roleId))
                throw new InvalidOperationException("The user-role link belongs to a different role.");
            ValidateLookupKey(
                document.UserLookupKey,
                IdentityDocumentId.From(tenantId, userId),
                "user-role user lookup key");
            ValidateLookupKey(
                document.RoleLookupKey,
                IdentityDocumentId.From(tenantId, roleId),
                "user-role role lookup key");
        }

        var linkId = IdentityDocumentId.From(tenantId, userId, roleId);
        var fingerprint = IdentityRequestFingerprint.FromParts(
            Normalize(tenantId), Normalize(userId), Normalize(roleId),
            expectedUserVersion.ToString(CultureInfo.InvariantCulture), delete.ToString(),
            document is null ? null : Serialize(document));
        var mutation = GroundworkIdentityAtomicMutation.Create(
            operation,
            fingerprint,
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.UserRoleDocumentKind);
        return _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var userEnvelope = await LoadUserAsync(unitOfWork, tenantId, userId, token);
                var roleEnvelope = await LoadRoleAsync(unitOfWork, tenantId, roleId, token);
                var linkEnvelope = await unitOfWork.ReadAsync(IdentityStorageManifest.UserRoleDocumentKind, linkId, token);
                if (delete)
                {
                    if (linkEnvelope is not null)
                    {
                        var existingLink = Deserialize<IdentityUserRoleDocument>(linkEnvelope);
                        ValidateUserChild(existingLink.TenantId, existingLink.UserId, tenantId, userId);
                        if (!Same(existingLink.RoleId, roleId))
                            throw new InvalidOperationException("The existing user-role link belongs to a different role.");
                        var deleteResult = unitOfWork.Delete(
                            new GroundworkIdentityRowDelete(IdentityStorageManifest.UserRoleDocumentKind, linkId, GroundworkIdentityRowWriteCondition.IfVersion(linkEnvelope.Version)), token);
                        if (deleteResult.Status is not WriteOutcomeStatus.Deleted)
                            return deleteResult;
                    }
                }
                else
                {
                    var linkResult = await SaveAsync(
                        unitOfWork, IdentityStorageManifest.UserRoleDocumentKind, linkId,
                        document!, linkEnvelope?.Version ?? 0, token);
                    if (!linkResult.Succeeded)
                        return linkResult;
                }

                var user = Deserialize<IdentityUserDocument>(userEnvelope);
                var role = Deserialize<IdentityRoleDocument>(roleEnvelope);
                var updatedUser = user with { RoleLinkIds = delete ? RemoveSorted(user.RoleLinkIds, linkId) : AddSorted(user.RoleLinkIds, linkId) };
                var updatedRole = role with { UserLinkIds = delete ? RemoveSorted(role.UserLinkIds, linkId) : AddSorted(role.UserLinkIds, linkId) };
                EnsureUserRelationshipCapacity(user, updatedUser);
                EnsureRoleRelationshipCapacity(role, updatedRole);
                var userResult = await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.IdentityUserDocumentKind,
                    userEnvelope.Id,
                    updatedUser,
                    expectedUserVersion,
                    token);
                if (!userResult.Succeeded)
                    return userResult;
                var roleResult = await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.IdentityRoleDocumentKind,
                    roleEnvelope.Id,
                    updatedRole,
                    roleEnvelope.Version,
                    token);
                return roleResult.Succeeded ? userResult : roleResult;
            },
            cancellationToken).AsTask();
    }

    private static async Task<GroundworkIdentityRow> LoadUserAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        var envelope = await unitOfWork.ReadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken)
            ?? throw new InvalidOperationException("The requested user does not exist in the current persistence scope.");
        var user = Deserialize<IdentityUserDocument>(envelope);
        ValidateUserChild(user.TenantId, user.UserId, tenantId, userId);
        return envelope;
    }

    private static async Task<GroundworkIdentityRow> LoadRoleAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        string tenantId,
        string roleId,
        CancellationToken cancellationToken)
    {
        var envelope = await unitOfWork.ReadAsync(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, roleId),
            cancellationToken)
            ?? throw new InvalidOperationException("The requested role does not exist in the current persistence scope.");
        var role = Deserialize<IdentityRoleDocument>(envelope);
        ValidateRoleChild(role.TenantId, role.RoleId, tenantId, roleId);
        return envelope;
    }

    private static Task<GroundworkIdentityWriteResult> SaveAsync<TDocument>(
        GroundworkIdentityMutationBatch unitOfWork,
        string documentKind,
        string id,
        TDocument document,
        long? expectedVersion,
        CancellationToken cancellationToken) =>
        Task.FromResult(unitOfWork.Save(
            GroundworkIdentityDocumentRows.Write(documentKind, id, document, expectedVersion),
            cancellationToken));

    private static void ValidateChildOwner(object document, string tenantId, string userId)
    {
        switch (document)
        {
            case IdentityUserClaimDocument claim:
                ValidateUserChild(claim.TenantId, claim.UserId, tenantId, userId);
                ValidateLookupKey(claim.UserLookupKey, IdentityDocumentId.From(tenantId, userId), "user-claim owner lookup key");
                ValidateLookupKey(claim.ClaimKey, IdentityDocumentId.From(tenantId, claim.ClaimType, claim.ClaimValue), "user-claim lookup key");
                break;
            case IdentityUserTokenDocument token:
                ValidateUserChild(token.TenantId, token.UserId, tenantId, userId);
                ValidateLookupKey(token.UserLookupKey, IdentityDocumentId.From(tenantId, userId), "user-token owner lookup key");
                ValidateLookupKey(
                    token.TokenKey,
                    IdentityDocumentId.From(tenantId, userId, token.LoginProvider, token.Name),
                    "user-token lookup key");
                break;
            case IdentityTenantMembershipDocument membership:
                ValidateUserChild(membership.TenantId, membership.UserId, tenantId, userId);
                ValidateLookupKey(
                    membership.MembershipKey,
                    IdentityDocumentId.From(tenantId, userId),
                    "tenant-membership lookup key");
                break;
            default:
                throw new InvalidOperationException($"Unsupported identity user relationship document '{document.GetType().Name}'.");
        }
    }

    private static void ValidateExistingUserChild(GroundworkIdentityRow envelope, string tenantId, string userId)
    {
        switch (envelope.UnitId)
        {
            case IdentityStorageManifest.UserClaimDocumentKind:
                var claim = Deserialize<IdentityUserClaimDocument>(envelope);
                ValidateChildOwner(claim, tenantId, userId);
                break;
            case IdentityStorageManifest.UserTokenDocumentKind:
                var token = Deserialize<IdentityUserTokenDocument>(envelope);
                ValidateChildOwner(token, tenantId, userId);
                break;
            case IdentityStorageManifest.IdentityTenantMembershipDocumentKind:
                var membership = Deserialize<IdentityTenantMembershipDocument>(envelope);
                ValidateChildOwner(membership, tenantId, userId);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported identity user relationship document kind '{envelope.UnitId}'.");
        }
    }

    private static void ValidateExternalLogin(IdentityExternalLoginDocument login)
    {
        if (!Same(login.TenantId, login.ExternalIdentity.TenantId) ||
            !Same(login.UserId, login.ExternalIdentity.UserId) ||
            !Same(login.LoginProvider, login.ExternalIdentity.Provider) ||
            !Same(login.ProviderKey, login.ExternalIdentity.ProviderSubject))
            throw new InvalidOperationException("The external-login document and external identity disagree about their authority owner.");
        ValidateLookupKey(
            login.LoginKey,
            IdentityDocumentId.From(login.TenantId, login.LoginProvider, login.ProviderKey),
            "external-login subject lookup key");
        ValidateLookupKey(
            login.UserLookupKey,
            IdentityDocumentId.From(login.TenantId, login.UserId),
            "external-login owner lookup key");
    }

    private static void ValidateUserChild(string actualTenantId, string actualUserId, string tenantId, string userId)
    {
        if (!Same(actualTenantId, tenantId) || !Same(actualUserId, userId))
            throw new InvalidOperationException("The identity relationship belongs to a different tenant or user.");
    }

    private static void ValidateRoleChild(string actualTenantId, string actualRoleId, string tenantId, string roleId)
    {
        if (!Same(actualTenantId, tenantId) || !Same(actualRoleId, roleId))
            throw new InvalidOperationException("The identity relationship belongs to a different tenant or role.");
    }

    private static void ValidateLookupKey(string? actual, string expected, string description)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"The {description} does not match its tenant-scoped identity values.");
    }

    private static IReadOnlyCollection<string>? GetRegistry(IdentityUserDocument user, UserRegistry registry) =>
        registry switch
        {
            UserRegistry.Claims => user.ClaimIds,
            UserRegistry.Logins => user.LoginIds,
            UserRegistry.Tokens => user.TokenIds,
            UserRegistry.TenantMemberships => user.TenantMembershipIds,
            _ => throw new ArgumentOutOfRangeException(nameof(registry))
        };

    private static IdentityUserDocument SetRegistry(
        IdentityUserDocument user,
        UserRegistry registry,
        IReadOnlyCollection<string> values) =>
        registry switch
        {
            UserRegistry.Claims => user with { ClaimIds = values },
            UserRegistry.Logins => user with { LoginIds = values },
            UserRegistry.Tokens => user with { TokenIds = values },
            UserRegistry.TenantMemberships => user with { TenantMembershipIds = values },
            _ => throw new ArgumentOutOfRangeException(nameof(registry))
        };

    private static IReadOnlyCollection<string> AddSorted(IReadOnlyCollection<string>? values, string value) =>
        [.. (values ?? []).Append(value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static IReadOnlyCollection<string> RemoveSorted(IReadOnlyCollection<string>? values, string value) =>
        [.. (values ?? []).Where(item => !string.Equals(item, value, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static void EnsureUserRelationshipCapacity(IdentityUserDocument previous, IdentityUserDocument next)
    {
        EnsureRelationshipCapacity("user", UserRelationshipCount(previous), UserRelationshipCount(next));
    }

    private static void EnsureRoleRelationshipCapacity(IdentityRoleDocument previous, IdentityRoleDocument next)
    {
        EnsureRelationshipCapacity("role", RoleRelationshipCount(previous), RoleRelationshipCount(next));
    }

    private static int UserRelationshipCount(IdentityUserDocument user) =>
        DistinctCount(user.ClaimIds ?? []) +
        DistinctCount(user.LoginIds ?? []) +
        DistinctCount(user.RoleLinkIds ?? []) +
        DistinctCount(user.TokenIds ?? []) +
        DistinctCount(user.TenantMembershipIds ?? []);

    private static int RoleRelationshipCount(IdentityRoleDocument role) =>
        DistinctCount(role.ClaimIds ?? []) + DistinctCount(role.UserLinkIds ?? []);

    private static int DistinctCount(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Count();

    private static void EnsureRelationshipCapacity(string ownerKind, int previousCount, int nextCount)
    {
        if (nextCount > previousCount && nextCount > IdentityStorageManifest.MaxAggregateRelationshipEntries)
            throw new InvalidOperationException(
                $"The identity {ownerKind} aggregate exceeds the portable relationship admission limit of " +
                $"{IdentityStorageManifest.MaxAggregateRelationshipEntries} entries.");
    }

    private static GroundworkIdentityWriteResult NotFound(string id) =>
        GroundworkIdentityWriteResult.NotFound(id);

    private static TDocument Deserialize<TDocument>(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<TDocument>(row);

    private static string Serialize<TDocument>(TDocument document) =>
        JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);

    private static string Normalize(string value) => IdentityCompositeDocumentId.Normalize(value);

    private static bool Same(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private enum UserRegistry
    {
        Claims,
        Logins,
        Tokens,
        TenantMemberships
    }

    private sealed record ChildChange(
        string DocumentKind,
        string Id,
        object? Document,
        bool DeleteChild,
        long? ExpectedVersion,
        bool EnforceExpectedVersion)
    {
        public string Fingerprint => IdentityRequestFingerprint.FromParts(
            DocumentKind,
            Id,
            Document is null ? null : Serialize(Document),
            DeleteChild.ToString(),
            ExpectedVersion?.ToString(CultureInfo.InvariantCulture),
            EnforceExpectedVersion.ToString()).Value;

        public static ChildChange Upsert<TDocument>(
            string kind,
            string id,
            TDocument document,
            long? expectedVersion = null,
            bool enforceExpectedVersion = false) =>
            new(kind, id, document, DeleteChild: false, expectedVersion, enforceExpectedVersion);

        public static ChildChange Delete(string kind, string id) =>
            new(kind, id, Document: null, DeleteChild: true, ExpectedVersion: null, EnforceExpectedVersion: false);
    }
}
