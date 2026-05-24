using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Versions;

internal sealed class Get : ElsaRequestHandlerEndpoint<GetVersion, ActivityDefinitionVersionDetailsView>
{
    public Get(IRequestSender requestSender, ILogger<Get> logger) : base(requestSender, logger)
    {
        Get(RouteConstants.GetRoute("versions/{versionId}"));
        AllowAnonymous();
    }
}
