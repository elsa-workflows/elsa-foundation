using Elsa.Decisions.Core;
using Elsa.Decisions.Workflows.Runtime;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Decisions.Workflows.Design;

/// <summary>Host replacement contract for decision source access at workflow publication.</summary>
[ReplacementContract]
public interface IDecisionPublicationAuthorizer
{
    ValueTask EnsureAsync(string tenantId, string assetId, CancellationToken cancellationToken = default);
}

internal sealed class DecisionPublicationAuthorizer(IHttpContextAccessor context, IPermissionAuthorizationService authorization)
    : IDecisionPublicationAuthorizer
{
    public async ValueTask EnsureAsync(string tenantId, string assetId, CancellationToken cancellationToken = default)
    {
        var principal = context.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true) throw Refused();
        foreach (var permission in new[] { DecisionPermissions.Read, DecisionPermissions.Source })
        {
            var result = await authorization.AuthorizeAsync(new(principal, permission, tenantId, assetId), cancellationToken);
            if (!result.Succeeded) throw Refused();
        }
    }

    private static DecisionInvocationException Refused() => new("forbidden", "The publisher cannot access this decision source.");
}
