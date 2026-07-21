using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

internal sealed class Validations(IRequestSender requestSender, ILogger<Validations> logger)
    : ElsaRequestHandlerEndpoint<GetDraftValidations, DraftValidationsView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("drafts/{draftId}/validations"));
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }
}
