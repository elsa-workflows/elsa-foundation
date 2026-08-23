using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Drafts.ProposeContract;

[Post("/design/activities/drafts/{draftId}/contract-proposals")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<ProposeReusableActivityContract, ActivityContractProposalView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsProposeContract";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityContractProposalView> HandleAsync(ProposeReusableActivityContract request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
