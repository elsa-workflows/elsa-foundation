using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Endpoints.Applications.List;

[Get(IdentityRouteConstants.ApplicationsRoute)]
[RequirePermission(DefaultIdentityPermissionKeys.IdentityApplicationsRead)]
internal sealed class Endpoint(ApplicationManager applications) : ApiEndpoint<ListApplications, ApplicationPageResponse>
{
    public override void Configure(ApiEndpointOptions options) => options.BodyMode = EndpointBodyMode.None;

    public override async Task<ApplicationPageResponse> HandleAsync(ListApplications request, CancellationToken cancellationToken) =>
        ApplicationPageResponse.From(await applications.ListAsync(
            ApplicationRequests.CallerTenant(HttpContext),
            new IamPageRequest(request.Skip ?? 0, request.Take ?? 50),
            cancellationToken));
}
