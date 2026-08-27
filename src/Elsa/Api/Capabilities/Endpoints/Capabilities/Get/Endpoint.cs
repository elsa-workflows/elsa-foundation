using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Authorization;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Api.Capabilities.Endpoints.Capabilities.Get;

[Get("/capabilities")]
[RequirePermission(ApiCapabilitiesPermissions.Read)]
public sealed class Endpoint(IApiCapabilityCatalog catalog) : ApiEndpointWithoutRequest<ApiCapabilitiesDocument>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetCapabilities";

    public override Task<ApiCapabilitiesDocument> HandleAsync(CancellationToken cancellationToken) =>
        catalog.GetAsync(cancellationToken);
}
