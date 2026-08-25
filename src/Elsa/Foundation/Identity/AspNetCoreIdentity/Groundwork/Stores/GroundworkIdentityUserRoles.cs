using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;

public sealed partial class GroundworkIdentityUserStore
{
    public async Task AddToRoleAsync(AspNetCoreIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        await EnsureUserExistsAsync(user, cancellationToken);
        var role = await FindRoleByNormalizedNameAsync(roleName, cancellationToken)
            ?? throw new InvalidOperationException("The requested role does not exist in the current persistence scope.");
        var document = new IdentityUserRoleDocument(
            Normalize(user.TenantId), Normalize(user.Id), Normalize(role.Id),
            UserLookupKey(user.TenantId, user.Id), RoleLookupKey(user.TenantId, role.Id));
        var result = await _relationships.AddUserRoleAsync(
            user.TenantId,
            user.Id,
            role.Id,
            RequireExpectedVersion(user),
            document,
            cancellationToken);
        ApplyRevisionStamp(user, RequireSavedRow(result, "user-role add"));
    }

    public async Task RemoveFromRoleAsync(AspNetCoreIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var role = await FindRoleByNormalizedNameAsync(roleName, cancellationToken);
        if (role is null)
            return;
        var result = await _relationships.DeleteUserRoleAsync(
            user.TenantId, user.Id, role.Id, RequireExpectedVersion(user), cancellationToken);
        ApplyRevisionStamp(user, RequireSavedRow(result, "user-role remove"));
    }

    public async Task<IList<string>> GetRolesAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var envelopes = Query(
            IdentityStorageManifest.UserRoleDocumentKind,
            IdentityStorageManifest.ListUserRolesQuery,
            (IdentityStorageManifest.UserLookupKeyField, UserLookupKey(CurrentTenantId(), user.Id)),
            cancellationToken);
        var roleNames = new List<string>();
        foreach (var link in envelopes.Select(Deserialize<IdentityUserRoleDocument>))
        {
            if (await FindRoleByIdAsync(link.RoleId, cancellationToken) is { } role)
                roleNames.Add(role.Name ?? role.NormalizedName ?? role.Id);
        }

        return roleNames;
    }

    public async Task<bool> IsInRoleAsync(AspNetCoreIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var role = await FindRoleByNormalizedNameAsync(roleName, cancellationToken);
        if (role is null)
            return false;
        var envelope = rows.Read(
            IdentityStorageManifest.UserRoleDocumentKind,
            IdentityDocumentId.From(user.TenantId, user.Id, role.Id),
            cancellationToken);
        return envelope is not null;
    }

    public async Task<IList<AspNetCoreIdentityUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var role = await FindRoleByNormalizedNameAsync(roleName, cancellationToken);
        if (role is null)
            return [];
        var envelopes = Query(
            IdentityStorageManifest.UserRoleDocumentKind,
            IdentityStorageManifest.ListRoleUsersQuery,
            (IdentityStorageManifest.RoleLookupKeyField, RoleLookupKey(CurrentTenantId(), role.Id)),
            cancellationToken);
        var users = new List<AspNetCoreIdentityUser>();
        foreach (var link in envelopes.Select(Deserialize<IdentityUserRoleDocument>))
        {
            if (await FindByIdAsync(link.UserId, cancellationToken) is { } user)
                users.Add(user);
        }

        return users;
    }

    private static string RoleLookupKey(string tenantId, string roleId) =>
        IdentityDocumentId.From(tenantId, roleId);
}
