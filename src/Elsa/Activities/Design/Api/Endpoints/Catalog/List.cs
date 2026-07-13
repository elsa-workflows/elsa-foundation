using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Catalog;

internal sealed class List(IRequestSender requestSender, ILogger<List> logger)
    : ElsaRequestHandlerEndpoint<ListActivityAuthoringCatalog, ActivityAuthoringCatalogView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Catalog);
        ConfigurePermissions(PermissionNames.ActivityDesignRead);
    }
}
