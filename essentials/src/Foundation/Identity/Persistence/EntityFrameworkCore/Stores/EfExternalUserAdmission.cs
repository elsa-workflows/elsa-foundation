using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Wraps existing authority/relationship mutations in one outer transaction and durable acknowledgement.</summary>
internal sealed class EfExternalUserAdmission(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor access,
    EfIdentityAuthorityAggregateCoordinator aggregates,
    EfIdentityAuthorityRelationshipCoordinator relationships,
    IIdentityEmailUniquenessPolicy emailPolicy)
{
    public async ValueTask<ExternalUserAdmissionResult> ApplyAsync(ExternalUserAdmission request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Key);
        ArgumentNullException.ThrowIfNull(request.User);
        if (!string.Equals(request.Key.TenantId, request.User.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Admission user and identity must belong to the same tenant.", nameof(request));
        EfIdentityStoreSupport.EnsureTenant(access, request.Key.TenantId);
        var user = request.User with
        {
            RoleIds = request.User.RoleIds.ToHashSet(StringComparer.Ordinal),
            DirectPermissions = request.User.DirectPermissions.ToHashSet(StringComparer.Ordinal)
        };
        var users = new EfUserStore(context, access, aggregates, emailPolicy);
        var logins = new EfExternalIdentityStore(context, access, relationships, aggregates, emailPolicy);
        var roles = new EfRoleStore(context, access, aggregates);
        var now = DateTimeOffset.UtcNow;
        var securityStamp = Guid.NewGuid().ToString("N");
        var fingerprint = EfIdentityStoreSupport.Fingerprint(request.Key.TenantId, request.Key.Provider, request.Key.Issuer, request.Key.Subject,
            user.Id, user.UserName, user.Email, user.DisplayName, user.Status.ToString(), user.Ownership.ToString(),
            EfIdentityStoreSupport.SerializeSet(user.RoleIds), EfIdentityStoreSupport.SerializeSet(user.DirectPermissions),
            request.CreateUser.ToString(), request.LinkPolicy.ToString());
        var mutation = EfIdentityAtomicMutation.Create("admit-qualified-external-user", fingerprint, request.Key.TenantId);
        var result = await new EfIdentityAtomicWrite(context, accessContextAccessor: access).ExecuteAsync(
            mutation, async token =>
            {
                var already = await logins.FindByKeyAsync(request.Key, token);
                if (already is not null)
                {
                    var owner = await users.FindWithRevisionAsync(request.Key.TenantId, already.UserId, token);
                    if (owner is null || owner.Record.Status != UserStatus.Active) return Reject("user-unavailable");
                    IdentityEntityFrameworkRevisionCodec.TryGetVersion(owner.Revision, out var ownerVersion);
                    return new(EfIdentityWriteStatus.Updated, ownerVersion, Id: owner.Record.Id);
                }
                if (user.Status != UserStatus.Active) return Reject("user-unavailable");
                long? expectedOwnerVersion;
                if (request.CreateUser)
                {
                    foreach (var role in user.RoleIds)
                        if (await roles.FindAsync(user.TenantId, role, token) is null) return Reject("role-unavailable");
                    var created = await aggregates.SaveUserAsync(user, 0, emailPolicy.RequireUniqueEmail, token,
                        requestIdentity: mutation.OperationId, applyWithinAtomic: entity => entity.SecurityStamp = securityStamp);
                    if (!created.WriteResult.Succeeded) return Reject("admission-conflict");
                    expectedOwnerVersion = created.WriteResult.Version;
                }
                else
                {
                    var current = await users.FindWithRevisionAsync(user.TenantId, user.Id, token);
                    if (current is null || current.Record.Status != UserStatus.Active) return Reject("user-unavailable");
                    if (!SameSnapshot(current.Record, user)) return Reject("admission-conflict");
                    var membership = await new EfTenantMembershipStore(context, access, relationships).FindAsync(user.TenantId, user.Id, token);
                    if (!SameMembership(membership, request.ExpectedMembership)) return Reject("admission-conflict");
                    IdentityEntityFrameworkRevisionCodec.TryGetVersion(current.Revision, out var version);
                    expectedOwnerVersion = version;
                }
                var linked = await relationships.SaveExternalIdentityAsync(new ExternalIdentityEntity
                {
                    TenantId = request.Key.TenantId, Provider = request.Key.Provider, Issuer = request.Key.Issuer,
                    ProviderSubject = request.Key.Subject, UserId = user.Id, LinkedAt = now, LastSeenAt = now,
                    LinkPolicy = (int)request.LinkPolicy
                }, expectedOwnerVersion, 0, true, EfExternalLoginOwnershipPolicy.CreateOrSameOwner, true, token);
                return linked.Succeeded ? linked with { Id = user.Id } : Reject("admission-conflict");
            }, cancellationToken);
        if (result.Succeeded) return new(true, result.Id);
        // A concurrent winner may have committed after this transaction's original read. The losing user
        // creation has rolled back before this lookup; return only the active canonical owner.
        var winner = await logins.FindByKeyAsync(request.Key, cancellationToken);
        if (winner is not null)
        {
            var owner = await users.FindAsync(request.Key.TenantId, winner.UserId, cancellationToken);
            if (owner?.Status == UserStatus.Active) return new(true, owner.Id);
        }
        return new(false, Failure: string.IsNullOrEmpty(result.Message) ? "admission-conflict" : result.Message);
    }

    private static EfIdentityWriteResult Reject(string reason) => new(EfIdentityWriteStatus.Conflict, Message: reason);
    private static bool SameSnapshot(UserRecord current, UserRecord expected) =>
        current == expected with { RoleIds = current.RoleIds, DirectPermissions = current.DirectPermissions } &&
        current.RoleIds.SetEquals(expected.RoleIds) && current.DirectPermissions.SetEquals(expected.DirectPermissions);
    private static bool SameMembership(TenantMembershipRecord? current, TenantMembershipRecord? expected) =>
        current is null ? expected is null : expected is not null &&
            current == expected with { RoleIds = current.RoleIds, DirectPermissions = current.DirectPermissions } &&
            current.RoleIds.SetEquals(expected.RoleIds) && current.DirectPermissions.SetEquals(expected.DirectPermissions);
}
