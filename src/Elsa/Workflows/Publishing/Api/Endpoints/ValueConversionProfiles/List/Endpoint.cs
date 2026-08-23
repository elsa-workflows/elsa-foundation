using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ValueConversionProfiles.List;

[Get("/publishing/value-conversion/profiles")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ListValueConversionProfiles, ValueConversionProfilesResponse>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "ListValueConversionProfiles";

    public override Task<ValueConversionProfilesResponse> HandleAsync(ListValueConversionProfiles request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
