using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.ApplyContractProposal;

[Post("/design/activities/drafts/{draftId}/contract-proposals/apply")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IActivityContractProposalService service) : ApiEndpoint<ApplyReusableActivityContractProposal, ReusableActivityDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsApplyContractProposal";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ReusableActivityDraftView> HandleAsync(ApplyReusableActivityContractProposal command, CancellationToken cancellationToken) =>
        service.ApplyAsync(command, cancellationToken);
}
