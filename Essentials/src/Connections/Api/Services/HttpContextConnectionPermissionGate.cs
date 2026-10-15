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
    public ValueTask EnsureUseAsync(string tenantId, CancellationToken cancellationToken = default) =>
        EnsureAsync(ConnectionsPermissions.Use, tenantId, requirePrincipal: false, cancellationToken);

    public ValueTask EnsureBindSecretAsync(string tenantId, CancellationToken cancellationToken = default) =>
        EnsureAsync(ConnectionsPermissions.BindSecret, tenantId, requirePrincipal: false, cancellationToken);

    private async ValueTask EnsureAsync(string permission, string tenantId, bool requirePrincipal, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            if (!requirePrincipal)
                return;

            throw new ConnectionUnavailableException(
                "connection",
                ConnectionFaultCodes.Forbidden,
                "A principal is required to bind an existing secret or named credential to a connection.");
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
