using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Endpoints.Applications.Delete;

/// <summary>Deletes an application together with its API key.</summary>
[Delete(IdentityRouteConstants.ApplicationRoute)]
[RequirePermission(DefaultIdentityPermissionKeys.IdentityApplicationsManage)]
internal sealed class Endpoint(ApplicationManager applications) : ApiEndpoint<ApplicationRoute>
{
    public override void Configure(ApiEndpointOptions options) => options.BodyMode = EndpointBodyMode.None;

    public override Task HandleAsync(ApplicationRoute request, CancellationToken cancellationToken) =>
        applications.DeleteAsync(ApplicationRequests.CallerTenant(HttpContext), request.ApplicationId, cancellationToken);
}
