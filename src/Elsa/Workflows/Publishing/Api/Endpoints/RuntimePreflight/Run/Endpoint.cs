using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Endpoints.RuntimePreflight.Run;

[Post("/publishing/preflight")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<RunRuntimeRequirementPreflight, RuntimeRequirementPreflightView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "RuntimeRequirementPreflightEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.Convention(builder => builder.WithMetadata(new RuntimePreflightProblemEndpointMetadata()));
    }

    public override Task<RuntimeRequirementPreflightView> HandleAsync(RunRuntimeRequirementPreflight request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
