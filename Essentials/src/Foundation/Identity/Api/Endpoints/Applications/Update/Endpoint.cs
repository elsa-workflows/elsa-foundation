using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Endpoints.Applications.Update;

/// <summary>Replaces an application's display name, roles and enabled state. A disabled application cannot authenticate.</summary>
[Put(IdentityRouteConstants.ApplicationRoute)]
[RequirePermission(DefaultIdentityPermissionKeys.IdentityApplicationsManage)]
internal sealed class Endpoint(ApplicationManager applications) : ApiEndpoint<UpdateApplication, ApplicationResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.Required;
    }

    public override async Task<ApplicationResponse> HandleAsync(UpdateApplication request, CancellationToken cancellationToken) =>
        ApplicationResponse.From(await applications.UpdateAsync(
            ApplicationRequests.CallerTenant(HttpContext),
            request.ApplicationId,
            request.DisplayName,
            request.RoleIds,
            request.Enabled,
            cancellationToken));
}
