using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;

public sealed partial class GroundworkIdentityUserStore
{
    public async Task<IList<Claim>> GetClaimsAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var envelopes = await QueryAsync(
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityStorageManifest.ListUserClaimsQuery,
            (IdentityStorageManifest.UserLookupKeyField, UserLookupKey(CurrentTenantId(), user.Id)),
            cancellationToken);
        return envelopes
            .Select(MapUserClaim)
            .Cast<Claim>()
            .ToArray();
    }

    public async Task AddClaimsAsync(AspNetCoreIdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var documents = claims.Select(claim => new IdentityUserClaimDocument(
                Normalize(user.TenantId),
                Normalize(user.Id),
                claim.Type,
                claim.Value,
                ClaimKey(user.TenantId, claim),
                UserLookupKey(user.TenantId, user.Id)))
            .ToArray();
        if (documents.Length == 0)
            return;
        var result = await _relationships.AddUserClaimsAsync(
            user.TenantId, user.Id, RequireExpectedVersion(user), documents, cancellationToken);
        ApplyRevisionStamp(user, RequireSavedEnvelope(result, "user-claim add"));
    }

    public async Task ReplaceClaimAsync(AspNetCoreIdentityUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var replacement = new IdentityUserClaimDocument(
            Normalize(user.TenantId), Normalize(user.Id), newClaim.Type, newClaim.Value,
            ClaimKey(user.TenantId, newClaim), UserLookupKey(user.TenantId, user.Id));
        var result = await _relationships.ReplaceUserClaimAsync(
            user.TenantId, user.Id, RequireExpectedVersion(user), claim.Type, claim.Value, replacement, cancellationToken);
        ApplyRevisionStamp(user, RequireSavedEnvelope(result, "user-claim replace"));
    }

    public async Task RemoveClaimsAsync(AspNetCoreIdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var removals = claims.Select(claim => (claim.Type, (string?)claim.Value)).ToArray();
        if (removals.Length == 0)
            return;
        var result = await _relationships.RemoveUserClaimsAsync(
            user.TenantId, user.Id, RequireExpectedVersion(user), removals, cancellationToken);
        ApplyRevisionStamp(user, RequireSavedEnvelope(result, "user-claim remove"));
    }

    public async Task<IList<AspNetCoreIdentityUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
    {
        var claims = await QueryAsync(
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityStorageManifest.FindUsersByClaimQuery,
            (IdentityStorageManifest.ClaimKeyField, ClaimKey(CurrentTenantId(), claim)),
            cancellationToken);
        var userIds = claims
            .Select(Deserialize<IdentityUserClaimDocument>)
            .Select(document => document.UserId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var users = new List<AspNetCoreIdentityUser>();
        foreach (var userId in userIds)
        {
            if (await FindByIdAsync(userId, cancellationToken) is { } user)
                users.Add(user);
        }

        return users;
    }

    private static Claim MapUserClaim(DocumentEnvelope envelope)
    {
        var document = Deserialize<IdentityUserClaimDocument>(envelope);
        return new Claim(document.ClaimType, document.ClaimValue ?? string.Empty);
    }

    private static string ClaimKey(string tenantId, Claim claim) =>
        IdentityDocumentId.From(tenantId, claim.Type, claim.Value);

}
