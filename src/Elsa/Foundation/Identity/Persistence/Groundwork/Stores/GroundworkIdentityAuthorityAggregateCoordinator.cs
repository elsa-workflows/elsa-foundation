using System.Globalization;
using System.Text.Json;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Groundwork.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

public enum GroundworkIdentityAuthorityConflict
{
    None,
    UserName,
    Email,
    RoleName
}

public sealed record GroundworkIdentityAuthorityWriteResult(
    GroundworkIdentityWriteResult WriteResult,
    GroundworkIdentityAuthorityConflict Conflict = GroundworkIdentityAuthorityConflict.None);

/// <summary>Owns atomic authority-root saves, reservations, and registry-driven aggregate deletes.</summary>
public sealed class GroundworkIdentityAuthorityAggregateCoordinator
{
    private readonly GroundworkIdentityAtomicWrite _atomicWrite;

    public GroundworkIdentityAuthorityAggregateCoordinator(GroundworkIdentityAtomicWrite atomicWrite) =>
        _atomicWrite = atomicWrite ?? throw new ArgumentNullException(nameof(atomicWrite));

    internal GroundworkIdentityAuthorityAggregateCoordinator(GroundworkIdentityRowStore rows)
        : this(new GroundworkIdentityAtomicWrite(rows))
    {
    }

    public static GroundworkIdentityAuthorityAggregateCoordinator ForRows(GroundworkIdentityRowStore rows) =>
        new(rows);

    public async Task<GroundworkIdentityAuthorityWriteResult> SaveUserAsync(
        IdentityUserDocument document,
        long? expectedVersion,
        bool requireUniqueEmail,
        CancellationToken cancellationToken,
        string? requestIdentity = null)
    {
        ValidateUser(document);
        var content = Serialize(document);
        var fingerprint = IdentityRequestFingerprint.FromParts(
            content,
            expectedVersion?.ToString(CultureInfo.InvariantCulture),
            requireUniqueEmail.ToString(),
            requestIdentity);
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "save-user-authority-aggregate",
            fingerprint,
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.UserNameReservationDocumentKind,
            IdentityStorageManifest.EmailReservationDocumentKind);
        var conflict = GroundworkIdentityAuthorityConflict.None;
        var result = await _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var userId = IdentityCompositeDocumentId.From(document.TenantId, document.UserId);
                var existingEnvelope = await unitOfWork.ReadAsync(
                    IdentityStorageManifest.IdentityUserDocumentKind,
                    userId,
                    token);
                var existing = existingEnvelope is null ? null : Deserialize<IdentityUserDocument>(existingEnvelope);
                if (existing is not null)
                    ValidateUser(existing);

                var userNameReservation = UserNameReservation(document);
                var reservationResult = await ReserveUserNameAsync(unitOfWork, userNameReservation, token);
                if (!reservationResult.Succeeded)
                {
                    conflict = GroundworkIdentityAuthorityConflict.UserName;
                    return reservationResult;
                }

                IdentityEmailReservationDocument? emailReservation = null;
                if (requireUniqueEmail)
                {
                    emailReservation = EmailReservation(document);
                    var emailResult = await ReserveEmailAsync(unitOfWork, emailReservation, token);
                    if (!emailResult.Succeeded)
                    {
                        conflict = GroundworkIdentityAuthorityConflict.Email;
                        return emailResult;
                    }
                }

                var merged = document with
                {
                    FrameworkState = document.FrameworkState ?? existing?.FrameworkState,
                    ClaimIds = existing?.ClaimIds,
                    LoginIds = existing?.LoginIds,
                    RoleLinkIds = existing?.RoleLinkIds,
                    TokenIds = existing?.TokenIds,
                    TenantMembershipIds = existing?.TenantMembershipIds
                };
                var userResult = await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.IdentityUserDocumentKind,
                    userId,
                    merged,
                    expectedVersion ?? existingEnvelope?.Version ?? 0,
                    token);
                if (!userResult.Succeeded)
                    return userResult;

                var oldUserNameKey = ReservationKey(document.TenantId, existing?.NormalizedUserName);
                if (!Same(oldUserNameKey, userNameReservation?.UserNameReservationKey))
                    await DeleteOwnedUserNameReservationAsync(unitOfWork, oldUserNameKey, document.TenantId, document.UserId, token);

                var oldEmailKey = ReservationKey(document.TenantId, existing?.NormalizedEmail);
                var newEmailKey = ReservationKey(document.TenantId, document.NormalizedEmail);
                if (!Same(oldEmailKey, newEmailKey))
                    await DeleteOwnedEmailReservationAsync(unitOfWork, oldEmailKey, document.TenantId, document.UserId, token);

                return userResult;
            },
            cancellationToken);
        conflict = conflict == GroundworkIdentityAuthorityConflict.None
            ? result.FailedUnitId switch
            {
                IdentityStorageManifest.UserNameReservationDocumentKind => GroundworkIdentityAuthorityConflict.UserName,
                IdentityStorageManifest.EmailReservationDocumentKind => GroundworkIdentityAuthorityConflict.Email,
                _ => GroundworkIdentityAuthorityConflict.None
            }
            : conflict;
        return new GroundworkIdentityAuthorityWriteResult(result, conflict);
    }

    public async Task<GroundworkIdentityAuthorityWriteResult> SaveRoleAsync(
        IdentityRoleDocument document,
        long? expectedVersion,
        CancellationToken cancellationToken,
        string? requestIdentity = null)
    {
        ValidateRole(document);
        var content = Serialize(document);
        var fingerprint = IdentityRequestFingerprint.FromParts(
            content,
            expectedVersion?.ToString(CultureInfo.InvariantCulture),
            requestIdentity);
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "save-role-authority-aggregate",
            fingerprint,
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.RoleNameReservationDocumentKind);
        var conflict = GroundworkIdentityAuthorityConflict.None;
        var result = await _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var roleId = IdentityCompositeDocumentId.From(document.TenantId, document.RoleId);
                var existingEnvelope = await unitOfWork.ReadAsync(
                    IdentityStorageManifest.IdentityRoleDocumentKind,
                    roleId,
                    token);
                var existing = existingEnvelope is null ? null : Deserialize<IdentityRoleDocument>(existingEnvelope);
                if (existing is not null)
                    ValidateRole(existing);

                var reservation = RoleNameReservation(document);
                var reservationResult = await ReserveRoleNameAsync(unitOfWork, reservation, token);
                if (!reservationResult.Succeeded)
                {
                    conflict = GroundworkIdentityAuthorityConflict.RoleName;
                    return reservationResult;
                }

                var merged = document with
                {
                    FrameworkState = document.FrameworkState ?? existing?.FrameworkState,
                    ClaimIds = existing?.ClaimIds,
                    UserLinkIds = existing?.UserLinkIds
                };
                var roleResult = await SaveAsync(
                    unitOfWork,
                    IdentityStorageManifest.IdentityRoleDocumentKind,
                    roleId,
                    merged,
                    expectedVersion ?? existingEnvelope?.Version ?? 0,
                    token);
                if (!roleResult.Succeeded)
                    return roleResult;

                var oldRoleNameKey = ReservationKey(document.TenantId, existing?.NormalizedRoleName);
                if (!Same(oldRoleNameKey, reservation?.RoleNameReservationKey))
                    await DeleteOwnedRoleNameReservationAsync(unitOfWork, oldRoleNameKey, document.TenantId, document.RoleId, token);
                return roleResult;
            },
            cancellationToken);
        if (conflict == GroundworkIdentityAuthorityConflict.None &&
            result.FailedUnitId == IdentityStorageManifest.RoleNameReservationDocumentKind)
            conflict = GroundworkIdentityAuthorityConflict.RoleName;
        return new GroundworkIdentityAuthorityWriteResult(result, conflict);
    }

    public Task<GroundworkIdentityWriteResult> DeleteUserAsync(
        string tenantId,
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        tenantId = Normalize(tenantId);
        userId = Normalize(userId);
        var fingerprint = IdentityRequestFingerprint.FromParts(
            tenantId,
            userId,
            expectedVersion.ToString(CultureInfo.InvariantCulture));
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "delete-user-authority-aggregate",
            fingerprint,
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityStorageManifest.UserRoleDocumentKind,
            IdentityStorageManifest.UserTokenDocumentKind,
            IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
            IdentityStorageManifest.UserNameReservationDocumentKind,
            IdentityStorageManifest.EmailReservationDocumentKind);

        return _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var userEnvelope = await unitOfWork.ReadAsync(
                    IdentityStorageManifest.IdentityUserDocumentKind,
                    IdentityCompositeDocumentId.From(tenantId, userId),
                    token);
                if (userEnvelope is null)
                    return GroundworkIdentityWriteResult.NotFound(userId);
                if (userEnvelope.Version != expectedVersion)
                    return GroundworkIdentityWriteResult.ConcurrencyConflict(userId);

                var user = Deserialize<IdentityUserDocument>(userEnvelope);
                ValidateUser(user);
                EnsureOwner(user.TenantId, user.UserId, tenantId, userId, "user authority document");

                await DeleteUserChildrenAsync<IdentityUserClaimDocument>(
                    unitOfWork, IdentityStorageManifest.UserClaimDocumentKind, user.ClaimIds,
                    child => EnsureOwner(child.TenantId, child.UserId, tenantId, userId, "user claim"), token);
                await DeleteUserChildrenAsync<IdentityExternalLoginDocument>(
                    unitOfWork, IdentityStorageManifest.ExternalLoginDocumentKind, user.LoginIds,
                    child => EnsureOwner(child.TenantId, child.UserId, tenantId, userId, "external login"), token);
                await DeleteUserChildrenAsync<IdentityUserTokenDocument>(
                    unitOfWork, IdentityStorageManifest.UserTokenDocumentKind, user.TokenIds,
                    child => EnsureOwner(child.TenantId, child.UserId, tenantId, userId, "user token"), token);
                await DeleteUserChildrenAsync<IdentityTenantMembershipDocument>(
                    unitOfWork, IdentityStorageManifest.IdentityTenantMembershipDocumentKind, user.TenantMembershipIds,
                    child => EnsureOwner(child.TenantId, child.UserId, tenantId, userId, "tenant membership"), token);

                foreach (var linkId in SortedDistinct(user.RoleLinkIds))
                {
                    var linkEnvelope = await RequireEnvelopeAsync(unitOfWork, IdentityStorageManifest.UserRoleDocumentKind, linkId, token);
                    var link = Deserialize<IdentityUserRoleDocument>(linkEnvelope);
                    EnsureOwner(link.TenantId, link.UserId, tenantId, userId, "user-role link");
                    var roleEnvelope = await RequireEnvelopeAsync(
                        unitOfWork,
                        IdentityStorageManifest.IdentityRoleDocumentKind,
                        IdentityCompositeDocumentId.From(tenantId, link.RoleId),
                        token);
                    var role = Deserialize<IdentityRoleDocument>(roleEnvelope);
                    EnsureOwner(role.TenantId, role.RoleId, tenantId, link.RoleId, "linked role");
                    var roleResult = await SaveAsync(
                        unitOfWork,
                        IdentityStorageManifest.IdentityRoleDocumentKind,
                        roleEnvelope.Id,
                        role with { UserLinkIds = RemoveSorted(role.UserLinkIds, linkId) },
                        roleEnvelope.Version,
                        token);
                    RequireSaved(roleResult, "linked role registry");
                    await RequireDeletedAsync(unitOfWork, linkEnvelope, token);
                }

                await DeleteOwnedUserNameReservationAsync(
                    unitOfWork, ReservationKey(tenantId, user.NormalizedUserName), tenantId, userId, token);
                await DeleteOwnedEmailReservationAsync(
                    unitOfWork, ReservationKey(tenantId, user.NormalizedEmail), tenantId, userId, token);
                return unitOfWork.Delete(
                    new GroundworkIdentityRowDelete(userEnvelope.UnitId, userEnvelope.Id, GroundworkIdentityRowWriteCondition.IfVersion(expectedVersion)),
                    token);
            },
            cancellationToken).AsTask();
    }

    public Task<GroundworkIdentityWriteResult> DeleteRoleAsync(
        string tenantId,
        string roleId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        tenantId = Normalize(tenantId);
        roleId = Normalize(roleId);
        var fingerprint = IdentityRequestFingerprint.FromParts(
            tenantId,
            roleId,
            expectedVersion.ToString(CultureInfo.InvariantCulture));
        var mutation = GroundworkIdentityAtomicMutation.Create(
            "delete-role-authority-aggregate",
            fingerprint,
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.RoleClaimDocumentKind,
            IdentityStorageManifest.UserRoleDocumentKind,
            IdentityStorageManifest.RoleNameReservationDocumentKind);

        return _atomicWrite.ExecuteAsync(
            mutation,
            async (unitOfWork, token) =>
            {
                var roleEnvelope = await unitOfWork.ReadAsync(
                    IdentityStorageManifest.IdentityRoleDocumentKind,
                    IdentityCompositeDocumentId.From(tenantId, roleId),
                    token);
                if (roleEnvelope is null)
                    return GroundworkIdentityWriteResult.NotFound(roleId);
                if (roleEnvelope.Version != expectedVersion)
                    return GroundworkIdentityWriteResult.ConcurrencyConflict(roleId);

                var role = Deserialize<IdentityRoleDocument>(roleEnvelope);
                ValidateRole(role);
                EnsureOwner(role.TenantId, role.RoleId, tenantId, roleId, "role authority document");

                await DeleteUserChildrenAsync<IdentityRoleClaimDocument>(
                    unitOfWork, IdentityStorageManifest.RoleClaimDocumentKind, role.ClaimIds,
                    child => EnsureOwner(child.TenantId, child.RoleId, tenantId, roleId, "role claim"), token);

                foreach (var linkId in SortedDistinct(role.UserLinkIds))
                {
                    var linkEnvelope = await RequireEnvelopeAsync(unitOfWork, IdentityStorageManifest.UserRoleDocumentKind, linkId, token);
                    var link = Deserialize<IdentityUserRoleDocument>(linkEnvelope);
                    EnsureOwner(link.TenantId, link.RoleId, tenantId, roleId, "user-role link");
                    var userEnvelope = await RequireEnvelopeAsync(
                        unitOfWork,
                        IdentityStorageManifest.IdentityUserDocumentKind,
                        IdentityCompositeDocumentId.From(tenantId, link.UserId),
                        token);
                    var user = Deserialize<IdentityUserDocument>(userEnvelope);
                    EnsureOwner(user.TenantId, user.UserId, tenantId, link.UserId, "linked user");
                    var userResult = await SaveAsync(
                        unitOfWork,
                        IdentityStorageManifest.IdentityUserDocumentKind,
                        userEnvelope.Id,
                        user with { RoleLinkIds = RemoveSorted(user.RoleLinkIds, linkId) },
                        userEnvelope.Version,
                        token);
                    RequireSaved(userResult, "linked user registry");
                    await RequireDeletedAsync(unitOfWork, linkEnvelope, token);
                }

                await DeleteOwnedRoleNameReservationAsync(
                    unitOfWork, ReservationKey(tenantId, role.NormalizedRoleName), tenantId, roleId, token);
                return unitOfWork.Delete(
                    new GroundworkIdentityRowDelete(roleEnvelope.UnitId, roleEnvelope.Id, GroundworkIdentityRowWriteCondition.IfVersion(expectedVersion)),
                    token);
            },
            cancellationToken).AsTask();
    }

    private static async Task DeleteUserChildrenAsync<TDocument>(
        GroundworkIdentityMutationBatch unitOfWork,
        string documentKind,
        IReadOnlyCollection<string>? documentIds,
        Action<TDocument> validate,
        CancellationToken cancellationToken)
    {
        foreach (var documentId in SortedDistinct(documentIds))
        {
            var envelope = await RequireEnvelopeAsync(unitOfWork, documentKind, documentId, cancellationToken);
            validate(Deserialize<TDocument>(envelope));
            await RequireDeletedAsync(unitOfWork, envelope, cancellationToken);
        }
    }

    private static async Task<GroundworkIdentityRow> RequireEnvelopeAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        string documentKind,
        string documentId,
        CancellationToken cancellationToken) =>
        await unitOfWork.ReadAsync(documentKind, documentId, cancellationToken)
        ?? throw new InvalidOperationException(
            $"Registered identity aggregate child '{documentKind}/{documentId}' does not exist.");

    private static async Task<GroundworkIdentityWriteResult> ReserveUserNameAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        IdentityUserNameReservationDocument? reservation,
        CancellationToken cancellationToken)
    {
        if (reservation is null)
            return Placeholder(IdentityStorageManifest.UserNameReservationDocumentKind);
        var existing = await unitOfWork.ReadAsync(
            IdentityStorageManifest.UserNameReservationDocumentKind,
            reservation.UserNameReservationKey,
            cancellationToken);
        if (existing is not null)
        {
            var current = Deserialize<IdentityUserNameReservationDocument>(existing);
            return SameOwner(current.TenantId, current.UserId, reservation.TenantId, reservation.UserId)
                ? GroundworkIdentityWriteResult.Saved(existing)
                : GroundworkIdentityWriteResult.ConcurrencyConflict(existing.Id);
        }

        return await SaveAsync(
            unitOfWork,
            IdentityStorageManifest.UserNameReservationDocumentKind,
            reservation.UserNameReservationKey,
            reservation,
            0,
            cancellationToken);
    }

    private static async Task<GroundworkIdentityWriteResult> ReserveEmailAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        IdentityEmailReservationDocument? reservation,
        CancellationToken cancellationToken)
    {
        if (reservation is null)
            return Placeholder(IdentityStorageManifest.EmailReservationDocumentKind);
        var existing = await unitOfWork.ReadAsync(
            IdentityStorageManifest.EmailReservationDocumentKind,
            reservation.EmailReservationKey,
            cancellationToken);
        if (existing is not null)
        {
            var current = Deserialize<IdentityEmailReservationDocument>(existing);
            return SameOwner(current.TenantId, current.UserId, reservation.TenantId, reservation.UserId)
                ? GroundworkIdentityWriteResult.Saved(existing)
                : GroundworkIdentityWriteResult.ConcurrencyConflict(existing.Id);
        }

        return await SaveAsync(
            unitOfWork,
            IdentityStorageManifest.EmailReservationDocumentKind,
            reservation.EmailReservationKey,
            reservation,
            0,
            cancellationToken);
    }

    private static async Task<GroundworkIdentityWriteResult> ReserveRoleNameAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        IdentityRoleNameReservationDocument? reservation,
        CancellationToken cancellationToken)
    {
        if (reservation is null)
            return Placeholder(IdentityStorageManifest.RoleNameReservationDocumentKind);
        var existing = await unitOfWork.ReadAsync(
            IdentityStorageManifest.RoleNameReservationDocumentKind,
            reservation.RoleNameReservationKey,
            cancellationToken);
        if (existing is not null)
        {
            var current = Deserialize<IdentityRoleNameReservationDocument>(existing);
            return SameOwner(current.TenantId, current.RoleId, reservation.TenantId, reservation.RoleId)
                ? GroundworkIdentityWriteResult.Saved(existing)
                : GroundworkIdentityWriteResult.ConcurrencyConflict(existing.Id);
        }

        return await SaveAsync(
            unitOfWork,
            IdentityStorageManifest.RoleNameReservationDocumentKind,
            reservation.RoleNameReservationKey,
            reservation,
            0,
            cancellationToken);
    }

    private static async Task DeleteOwnedUserNameReservationAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        string? key,
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (key is null)
            return;
        var envelope = await unitOfWork.ReadAsync(IdentityStorageManifest.UserNameReservationDocumentKind, key, cancellationToken);
        if (envelope is null)
            return;
        var reservation = Deserialize<IdentityUserNameReservationDocument>(envelope);
        EnsureOwner(reservation.TenantId, reservation.UserId, tenantId, userId, "user-name reservation");
        await RequireDeletedAsync(unitOfWork, envelope, cancellationToken);
    }

    private static async Task DeleteOwnedEmailReservationAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        string? key,
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (key is null)
            return;
        var envelope = await unitOfWork.ReadAsync(IdentityStorageManifest.EmailReservationDocumentKind, key, cancellationToken);
        if (envelope is null)
            return;
        var reservation = Deserialize<IdentityEmailReservationDocument>(envelope);
        if (!SameOwner(reservation.TenantId, reservation.UserId, tenantId, userId))
            return;
        await RequireDeletedAsync(unitOfWork, envelope, cancellationToken);
    }

    private static async Task DeleteOwnedRoleNameReservationAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        string? key,
        string tenantId,
        string roleId,
        CancellationToken cancellationToken)
    {
        if (key is null)
            return;
        var envelope = await unitOfWork.ReadAsync(IdentityStorageManifest.RoleNameReservationDocumentKind, key, cancellationToken);
        if (envelope is null)
            return;
        var reservation = Deserialize<IdentityRoleNameReservationDocument>(envelope);
        EnsureOwner(reservation.TenantId, reservation.RoleId, tenantId, roleId, "role-name reservation");
        await RequireDeletedAsync(unitOfWork, envelope, cancellationToken);
    }

    private static IdentityUserNameReservationDocument? UserNameReservation(IdentityUserDocument user)
    {
        if (string.IsNullOrWhiteSpace(user.NormalizedUserName))
            return null;
        var key = ReservationKey(user.TenantId, user.NormalizedUserName)!;
        return new(
            Normalize(user.TenantId),
            Normalize(user.NormalizedUserName),
            key,
            Normalize(user.UserId));
    }

    private static IdentityEmailReservationDocument? EmailReservation(IdentityUserDocument user)
    {
        if (string.IsNullOrWhiteSpace(user.NormalizedEmail))
            return null;
        var key = ReservationKey(user.TenantId, user.NormalizedEmail)!;
        return new(
            Normalize(user.TenantId),
            Normalize(user.NormalizedEmail),
            key,
            Normalize(user.UserId));
    }

    private static IdentityRoleNameReservationDocument? RoleNameReservation(IdentityRoleDocument role)
    {
        if (string.IsNullOrWhiteSpace(role.NormalizedRoleName))
            return null;
        var key = ReservationKey(role.TenantId, role.NormalizedRoleName)!;
        return new(
            Normalize(role.TenantId),
            Normalize(role.NormalizedRoleName),
            key,
            Normalize(role.RoleId));
    }

    private static void ValidateUser(IdentityUserDocument document)
    {
        EnsureOwner(document.TenantId, document.UserId, document.User.TenantId, document.User.Id, "user authority document");
        if (!Same(document.NormalizedUserNameKey, ReservationKey(document.TenantId, document.NormalizedUserName)) ||
            !Same(document.NormalizedEmailKey, ReservationKey(document.TenantId, document.NormalizedEmail)))
            throw new InvalidOperationException("The user authority document lookup keys do not match its normalized values.");
    }

    private static void ValidateRole(IdentityRoleDocument document)
    {
        EnsureOwner(document.TenantId, document.RoleId, document.Role.TenantId, document.Role.Id, "role authority document");
        if (!Same(document.NormalizedRoleNameKey, ReservationKey(document.TenantId, document.NormalizedRoleName)))
            throw new InvalidOperationException("The role authority document lookup key does not match its normalized value.");
    }

    private static async Task RequireDeletedAsync(
        GroundworkIdentityMutationBatch unitOfWork,
        GroundworkIdentityRow envelope,
        CancellationToken cancellationToken)
    {
        var result = unitOfWork.Delete(
            new GroundworkIdentityRowDelete(envelope.UnitId, envelope.Id, GroundworkIdentityRowWriteCondition.IfVersion(envelope.Version)),
            cancellationToken);
        if (result.Status is not WriteOutcomeStatus.Deleted)
            throw new InvalidOperationException($"Could not delete identity aggregate row '{envelope.UnitId}/{envelope.Id}'.");
    }

    private static void RequireSaved(GroundworkIdentityWriteResult result, string description)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not update the {description}; Groundwork returned {result.Status}.");
    }

    private static IReadOnlyCollection<string> RemoveSorted(IReadOnlyCollection<string>? values, string value) =>
        SortedDistinct(values).Where(candidate => !Same(candidate, value)).ToArray();

    private static IReadOnlyCollection<string> SortedDistinct(IReadOnlyCollection<string>? values) =>
        (values ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

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

    private static GroundworkIdentityWriteResult Placeholder(string kind) =>
        GroundworkIdentityWriteResult.Saved(new GroundworkIdentityRow(
            kind, "none", IdentityStorageManifest.SchemaVersion, 0, "{}", new Dictionary<string, object?>()));

    private static void EnsureOwner(
        string actualTenantId,
        string actualOwnerId,
        string expectedTenantId,
        string expectedOwnerId,
        string relationship)
    {
        if (!SameOwner(actualTenantId, actualOwnerId, expectedTenantId, expectedOwnerId))
            throw new InvalidOperationException($"The {relationship} belongs to a different tenant or owner.");
    }

    private static bool SameOwner(string tenantId, string ownerId, string expectedTenantId, string expectedOwnerId) =>
        Same(tenantId, expectedTenantId) && Same(ownerId, expectedOwnerId);

    private static string? ReservationKey(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IdentityDocumentId.From(tenantId, value);

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static string Normalize(string value) => IdentityCompositeDocumentId.Normalize(value);

    private static string Serialize<TDocument>(TDocument document) =>
        JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);

    private static TDocument Deserialize<TDocument>(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<TDocument>(row);
}
