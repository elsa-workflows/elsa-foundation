using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using NativeEndpoints;

namespace Elsa3.Activities.Design.Import.Endpoints.Collections.Selection;

[Post("migration/elsa3/reusable-activities/collections/{collectionHandle}/selection")]
[RequirePermission(Elsa3ImportPermissions.Read)]
public sealed class Endpoint(IReusableActivityImportOperationService service) : ApiEndpoint<ReusableActivityImportSelectionRequest, ReusableActivityImportSelectionReadiness>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ExpandReusableActivityImportSelectionEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.Required;
    }

    public override Task<ReusableActivityImportSelectionReadiness> HandleAsync(ReusableActivityImportSelectionRequest request, CancellationToken cancellationToken) =>
        service.ExpandSelectionAsync(
            RouteValue("collectionHandle"), request.PlanId, request.SelectedSourceVersionIds,
            ReusableActivityImportHttp.Scope(HttpContext.User), cancellationToken).AsTask();

    private string RouteValue(string key) =>
        HttpContext.Request.RouteValues.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
}
