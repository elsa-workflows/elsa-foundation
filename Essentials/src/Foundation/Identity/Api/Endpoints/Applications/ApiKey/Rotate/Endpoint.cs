using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Endpoints.Applications.ApiKey.Rotate;

/// <summary>
/// Issues a new API key and shows it, the only time it is returned. The previous key stops authenticating at once, and
/// a revoked key is replaced the same way.
/// </summary>
[Post(IdentityRouteConstants.ApplicationApiKeyRoute + "/rotate")]
[RequirePermission(DefaultIdentityPermissionKeys.IdentityApplicationsManage)]
internal sealed class Endpoint(ApplicationManager applications) : ApiEndpoint<ApplicationRoute, ApiKeyResponse>
{
    public override void Configure(ApiEndpointOptions options) => options.BodyMode = EndpointBodyMode.None;

    public override async Task<ApiKeyResponse> HandleAsync(ApplicationRoute request, CancellationToken cancellationToken)
    {
        var apiKey = await applications.RotateApiKeyAsync(ApplicationRequests.CallerTenant(HttpContext), request.ApplicationId, cancellationToken);
        ApplicationRequests.DoNotStore(HttpContext);
        return new ApiKeyResponse(apiKey);
    }
}
