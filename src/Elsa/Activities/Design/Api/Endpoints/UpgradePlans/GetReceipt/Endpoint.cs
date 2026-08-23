using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.UpgradePlans.GetReceipt;

[Get("/design/activities/upgrade-plans/{planId}/receipts/{receiptId}")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityUpgradeApplyReceipt, ActivityUpgradeApplyReceiptView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UpgradePlansGetReceipt";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ActivityUpgradeApplyReceiptView> HandleAsync(GetActivityUpgradeApplyReceipt request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
