using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Groundwork.Store;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;

public sealed partial class GroundworkIdentityUserStore
{
    public Task SetTokenAsync(AspNetCoreIdentityUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        var save = SaveTokenAsync(user, loginProvider, name, value, cancellationToken);
        return save;
    }

    private async Task SaveTokenAsync(AspNetCoreIdentityUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var document = new IdentityUserTokenDocument(
            Normalize(user.TenantId),
            Normalize(user.Id),
            Normalize(loginProvider),
            Normalize(name),
            TokenKey(user.TenantId, user.Id, loginProvider, name),
            value,
            UserLookupKey(user.TenantId, user.Id));
        var result = await _relationships.SaveUserTokenAsync(
            user.TenantId, user.Id, RequireExpectedVersion(user), document, cancellationToken);
        ApplyRevisionStamp(user, RequireSavedRow(result, "user-token save"));
    }

    public async Task RemoveTokenAsync(AspNetCoreIdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var result = await _relationships.DeleteUserTokenAsync(
            user.TenantId, user.Id, RequireExpectedVersion(user), loginProvider, name, cancellationToken);
        ApplyRevisionStamp(user, RequireSavedRow(result, "user-token remove"));
    }

    public async Task<string?> GetTokenAsync(AspNetCoreIdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var envelope = rows.Read(
            IdentityStorageManifest.UserTokenDocumentKind,
            TokenDocumentId(user.TenantId, user.Id, loginProvider, name),
            cancellationToken);
        return envelope is null ? null : Deserialize<IdentityUserTokenDocument>(envelope).Value;
    }

    public Task SetAuthenticatorKeyAsync(AspNetCoreIdentityUser user, string key, CancellationToken cancellationToken) =>
        SetTokenAsync(user, AuthenticatorStoreLoginProvider, AuthenticatorKeyTokenName, key, cancellationToken);

    public Task<string?> GetAuthenticatorKeyAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        GetTokenAsync(user, AuthenticatorStoreLoginProvider, AuthenticatorKeyTokenName, cancellationToken);

    public Task ReplaceCodesAsync(AspNetCoreIdentityUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken) =>
        SetTokenAsync(user, RecoveryCodeTokenProvider, RecoveryCodeTokenName, string.Join(';', recoveryCodes), cancellationToken);

    public async Task<bool> RedeemCodeAsync(AspNetCoreIdentityUser user, string code, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var result = await _relationships.RedeemRecoveryCodeAsync(
            user.TenantId, user.Id, RequireExpectedVersion(user),
            RecoveryCodeTokenProvider, RecoveryCodeTokenName, code, cancellationToken);
        if (result.Status is WriteOutcomeStatus.NotFound)
            return false;
        if (result.Status is WriteOutcomeStatus.ConcurrencyConflict)
        {
            var currentCodes = await GetRecoveryCodesAsync(user, cancellationToken);
            if (!currentCodes.Contains(code))
                return false;
        }

        ApplyRevisionStamp(user, RequireSavedRow(result, "recovery-code redeem"));
        return true;
    }

    public async Task<int> CountCodesAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        (await GetRecoveryCodesAsync(user, cancellationToken)).Count;

    private static string TokenKey(string tenantId, string userId, string loginProvider, string name) =>
        IdentityDocumentId.From(tenantId, userId, loginProvider, name);

}
