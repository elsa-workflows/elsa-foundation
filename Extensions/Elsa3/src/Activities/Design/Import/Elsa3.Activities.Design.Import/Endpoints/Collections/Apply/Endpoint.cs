using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa3.Activities.Design.Import.Endpoints.Collections.Apply;

[Post("migration/elsa3/reusable-activities/collections/{collectionHandle}/apply")]
// Applying publishes the imported reusable activities, so it is never a way around the publish permission (spec 246 D-4).
[RequireAllPermissions(Elsa3ImportPermissions.Manage, Elsa3ImportPermissions.PublishingManage)]
public sealed class Endpoint(IReusableActivityImportOperationService service) : ApiEndpointWithResult<ReusableActivityImportApplyHttpRequest, ReusableActivityImportReceipt>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ApplyReusableActivityImportEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.Required;
    }

    public override async Task<EndpointResult<ReusableActivityImportReceipt>> HandleAsync(ReusableActivityImportApplyHttpRequest request, CancellationToken cancellationToken)
    {
        var collectionHandle = HttpContext.Request.RouteValues.TryGetValue("collectionHandle", out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;
        var result = await service.ApplyAsync(
            collectionHandle, request.PlanId, request.SelectedSourceVersionIds,
            request.IdempotencyKey, ReusableActivityImportHttp.Scope(HttpContext.User), cancellationToken);
        HttpContext.Response.Headers.Location = $"/migration/elsa3/reusable-activities/imports/{Uri.EscapeDataString(request.IdempotencyKey)}";
        return EndpointResult.Status(
            result.Status == ReusableActivityImportReceiptStatus.Applied
                ? StatusCodes.Status201Created
                : StatusCodes.Status200OK,
            result);
    }
}
