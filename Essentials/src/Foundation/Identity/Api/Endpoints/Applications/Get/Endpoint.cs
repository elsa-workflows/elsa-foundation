using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Endpoints.Applications.Get;

[Get(IdentityRouteConstants.ApplicationRoute)]
[RequirePermission(DefaultIdentityPermissionKeys.IdentityApplicationsRead)]
internal sealed class Endpoint(ApplicationManager applications) : ApiEndpoint<ApplicationRoute, ApplicationResponse>
{
    public override void Configure(ApiEndpointOptions options) => options.BodyMode = EndpointBodyMode.None;

    public override async Task<ApplicationResponse> HandleAsync(ApplicationRoute request, CancellationToken cancellationToken) =>
        ApplicationResponse.From(await applications.GetAsync(ApplicationRequests.CallerTenant(HttpContext), request.ApplicationId, cancellationToken));
}
