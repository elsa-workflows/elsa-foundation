using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

internal sealed class PromotionPreflight(IRequestSender requestSender, ILogger<PromotionPreflight> logger)
    : ElsaRequestHandlerEndpoint<PreflightDraftPromotion, PromotionPreflightAssessmentView>(requestSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("drafts/{draftId}/promotion-preflight"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }
}
