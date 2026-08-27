using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;
using PublishWorkflowCommand = Elsa.Workflows.Publishing.Core.Requests.PublishWorkflow;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Workflows.Publish;

[Post("/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpointWithResult<PublishWorkflowRequest, PublishedWorkflowView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "PublishWorkflowEndpoint";
        options.Accepts = ["application/json"];
        // A literal null body publishes the route-addressed version alone, as the published contract allows.
        options.BodyMode = EndpointBodyMode.OptionalWithContentType;
        options.Convention(builder => builder.WithMetadata(
            new WorkflowPublicationProblemEndpointMetadata(expressionValidation: true)));
    }

    public override async Task<EndpointResult<PublishedWorkflowView>> HandleAsync(PublishWorkflowRequest request, CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new PublishWorkflowCommand(request.VersionId,
                request.Action is { } action ? PublicationIntentContract.ToModel(action) : null,
                request.SlotName, request.ExpectedPublicationId, request.PreflightToken,
                PublicationRequestTenant.Resolve(HttpContext.User)), cancellationToken);
        return EndpointResult.Status(
            response.WasCreated ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            response);
    }
}
