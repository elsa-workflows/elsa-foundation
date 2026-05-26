using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Versions;

internal sealed class Get(IRequestSender requestSender, ILogger<Get> logger) : ElsaRequestHandlerEndpoint<GetVersion, ActivityDefinitionVersionDetailsView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("versions/{versionId}"));
        AllowAnonymous();
    }
}
