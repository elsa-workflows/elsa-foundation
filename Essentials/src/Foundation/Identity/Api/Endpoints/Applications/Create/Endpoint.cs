using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Endpoints.Applications.Create;

/// <summary>Creates an application and shows its API key, the only time the key is returned.</summary>
[Post(IdentityRouteConstants.ApplicationsRoute)]
[RequirePermission(DefaultIdentityPermissionKeys.IdentityApplicationsManage)]
internal sealed class Endpoint(ApplicationManager applications) : ApiEndpoint<CreateApplication, IssuedApplicationResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.Required;
        options.SuccessStatus = StatusCodes.Status201Created;
    }

    public override async Task<IssuedApplicationResponse> HandleAsync(CreateApplication request, CancellationToken cancellationToken)
    {
        var (application, apiKey) = await applications.CreateAsync(
            ApplicationRequests.CallerTenant(HttpContext), request.DisplayName, request.RoleIds, cancellationToken);
        ApplicationRequests.DoNotStore(HttpContext);
        return new IssuedApplicationResponse(ApplicationResponse.From(application), apiKey);
    }
}
