using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.AspNetCore.Http;
using MinimalEndpoints;

namespace Elsa3.Activities.Design.Import.Endpoints.Collections.ApplyDrafts;

[Post("migration/elsa3/reusable-activities/collections/{collectionHandle}/apply-drafts")]
// Both choices retain the import and publication authority required by the collection Apply contract (spec 246 D-4).
[RequireAllPermissions(Elsa3ImportPermissions.Manage, Elsa3ImportPermissions.PublishingManage)]
public sealed class Endpoint(IReusableActivityImportOperationService service) : ApiEndpointWithResult<ReusableActivityDraftApplyHttpRequest, ReusableActivityImportReceipt>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ApplyReusableActivityDraftImportEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.Required;
    }

    public override async Task<EndpointResult<ReusableActivityImportReceipt>> HandleAsync(ReusableActivityDraftApplyHttpRequest request, CancellationToken cancellationToken)
    {
        var collectionHandle = HttpContext.Request.RouteValues.TryGetValue("collectionHandle", out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;
        if (service is not IReusableActivityDraftImport draftImport)
            return EndpointResult.Status(StatusCodes.Status501NotImplemented, default(ReusableActivityImportReceipt)!);
        var result = await draftImport.ApplyDraftsAsync(
            collectionHandle, request.PlanId, request.SelectedSourceVersionIds,
            request.IdempotencyKey, ReusableActivityImportHttp.Scope(HttpContext.User), request.PublishImportedActivities ?? false, cancellationToken);
        HttpContext.Response.Headers.Location = $"/migration/elsa3/reusable-activities/imports/{Uri.EscapeDataString(request.IdempotencyKey)}";
        return EndpointResult.Status(
            result.Status == ReusableActivityImportReceiptStatus.Applied
                ? StatusCodes.Status201Created
                : StatusCodes.Status200OK,
            result);
    }
}
