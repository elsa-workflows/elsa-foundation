using Elsa.Foundation.Identity.Abstractions.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

public sealed class AspNetCoreIdentityRoleManager(IRoleStore roles) : IRoleManager
{
    public async ValueTask<RoleRecord> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = new RoleRecord(
            Guid.NewGuid().ToString("n"),
            request.TenantId,
            request.Name,
            request.Description,
            request.Permissions,
            System: false);

        await roles.SaveAsync(role, cancellationToken);
        return role;
    }

    public async ValueTask AssignPermissionAsync(RolePermissionAssignment request, CancellationToken cancellationToken = default)
    {
        var role = await roles.FindAsync(request.TenantId, request.RoleId, cancellationToken) ??
                   throw new InvalidOperationException("Cannot assign a permission to a missing role.");

        var permissions = role.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        permissions.Add(request.Permission);

        await roles.SaveAsync(role with { Permissions = permissions }, cancellationToken);
    }
}
