using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions;

internal sealed class List : ElsaRequestHandlerEndpoint<ListDefinitions, IEnumerable<ActivityDefinitionView>>
{    
    public List(IRequestSender requestSender, ILogger<List> logger) : base(requestSender, logger)
    {
        Get(RouteConstants.Definitions);
        AllowAnonymous();        
    }
}
