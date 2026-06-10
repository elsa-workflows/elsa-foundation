using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Microsoft.Extensions.Logging;
using PublishWorkflowRequest = Elsa.Workflows.Publishing.Api.Requests.PublishWorkflow;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class PublishWorkflowEndpoint(IRequestSender requestSender, ILogger<PublishWorkflowEndpoint> logger)
    : ElsaRequestHandlerEndpoint<PublishWorkflowRequest, PublishedWorkflowView>(requestSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("workflows/{versionId}/publish"));
        AllowAnonymous();
    }
}
