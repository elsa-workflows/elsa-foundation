using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Core.Permissions;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Connections.Api.Services;

internal sealed class HttpContextConnectionPermissionGate(
    IPermissionAuthorizationService authorization,
    IHttpContextAccessor httpContextAccessor) : IConnectionPermissionGate
{
    public ValueTask EnsureManageAsync(string tenantId, CancellationToken cancellationToken = default) =>
        EnsureAsync(ConnectionsPermissions.Manage, tenantId, cancellationToken);

    public ValueTask EnsureUseAsync(string tenantId, CancellationToken cancellationToken = default) =>
        EnsureAsync(ConnectionsPermissions.Use, tenantId, cancellationToken);

    public ValueTask EnsureBindSecretAsync(string tenantId, CancellationToken cancellationToken = default) =>
        EnsureAsync(ConnectionsPermissions.BindSecret, tenantId, cancellationToken);

    private async ValueTask EnsureAsync(string permission, string tenantId, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            throw new ConnectionUnavailableException(
                "connection",
                ConnectionFaultCodes.Forbidden,
                $"An authenticated principal is required for permission '{permission}'.");
        }

        var result = await authorization.AuthorizeAsync(
            new PermissionEvaluationContext(principal, permission, tenantId),
            cancellationToken);
        if (result.Succeeded)
            return;

        throw new ConnectionUnavailableException(
            "connection",
            ConnectionFaultCodes.Forbidden,
            $"Permission '{permission}' is required.");
    }
}
