using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Endpoints.Applications.ApiKey.Revoke;

/// <summary>Revokes the application's API key, which stops authenticating at once. Revoking a revoked key changes nothing.</summary>
[Post(IdentityRouteConstants.ApplicationApiKeyRoute + "/revoke")]
[RequirePermission(DefaultIdentityPermissionKeys.IdentityApplicationsManage)]
internal sealed class Endpoint(ApplicationManager applications) : ApiEndpoint<ApplicationRoute>
{
    public override void Configure(ApiEndpointOptions options) => options.BodyMode = EndpointBodyMode.None;

    public override Task HandleAsync(ApplicationRoute request, CancellationToken cancellationToken) =>
        applications.RevokeApiKeyAsync(ApplicationRequests.CallerTenant(HttpContext), request.ApplicationId, cancellationToken);
}
