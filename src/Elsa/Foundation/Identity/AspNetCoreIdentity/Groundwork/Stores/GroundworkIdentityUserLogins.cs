using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;

public sealed partial class GroundworkIdentityUserStore
{
    public async Task AddLoginAsync(AspNetCoreIdentityUser user, UserLoginInfo login, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var document = new IdentityExternalLoginDocument(
            Normalize(user.TenantId),
            Normalize(user.Id),
            Normalize(login.LoginProvider),
            Normalize(login.ProviderKey),
            LoginKey(user.TenantId, login.LoginProvider, login.ProviderKey),
            login.ProviderDisplayName,
            new ExternalIdentityRecord(
                user.TenantId,
                login.LoginProvider,
                login.ProviderKey,
                user.Id,
                DateTimeOffset.UnixEpoch,
                LastSeenAt: null,
                ExternalIdentityLinkPolicy.Admin),
            UserLookupKey(user.TenantId, user.Id));
        var result = await _relationships.SaveExternalLoginAsync(
            document, RequireExpectedVersion(user), expectedLoginVersion: null,
            enforceLoginVersion: false,
            ownershipPolicy: GroundworkExternalLoginOwnershipPolicy.CreateOrSameOwner,
            returnOwnerResult: true,
            cancellationToken);
        ApplyRevisionStamp(user, RequireSavedRow(result, "external-login add"));
    }

    public async Task RemoveLoginAsync(AspNetCoreIdentityUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var result = await _relationships.DeleteExternalLoginAsync(
            user.TenantId, user.Id, loginProvider, providerKey, RequireExpectedVersion(user), cancellationToken);
        ApplyRevisionStamp(user, RequireSavedRow(result, "external-login remove"));
    }

    public async Task<IList<UserLoginInfo>> GetLoginsAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var envelopes = Query(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityStorageManifest.ListUserLoginsQuery,
            (IdentityStorageManifest.UserLookupKeyField, UserLookupKey(CurrentTenantId(), user.Id)),
            cancellationToken);
        return envelopes
            .Select(MapLogin)
            .Cast<UserLoginInfo>()
            .ToArray();
    }

    public async Task<AspNetCoreIdentityUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        var envelope = rows.Read(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityCompositeDocumentId.From(CurrentTenantId(), loginProvider, providerKey),
            cancellationToken);
        if (envelope is null)
            return null;
        var login = Deserialize<IdentityExternalLoginDocument>(envelope);
        return await FindByIdAsync(login.UserId, cancellationToken);
    }

    private static UserLoginInfo MapLogin(GroundworkIdentityRow envelope)
    {
        var document = Deserialize<IdentityExternalLoginDocument>(envelope);
        return new UserLoginInfo(
            document.ExternalIdentity.Provider,
            document.ExternalIdentity.ProviderSubject,
            document.ProviderDisplayName);
    }

    private static string LoginKey(string tenantId, string loginProvider, string providerKey) =>
        IdentityDocumentId.From(tenantId, loginProvider, providerKey);

}
