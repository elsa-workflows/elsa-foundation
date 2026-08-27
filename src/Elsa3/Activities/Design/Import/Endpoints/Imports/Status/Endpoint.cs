using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using NativeEndpoints;

namespace Elsa3.Activities.Design.Import.Endpoints.Imports.Status;

/// <summary>The status lookup request, bound from the route.</summary>
public sealed record GetReusableActivityImportStatusRequest(string IdempotencyKey);

[Get("migration/elsa3/reusable-activities/imports/{idempotencyKey}")]
[RequirePermission(Elsa3ImportPermissions.Read)]
public sealed class Endpoint(IReusableActivityImportOperationService service) : ApiEndpoint<GetReusableActivityImportStatusRequest, ReusableActivityImportReceipt>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetReusableActivityImportStatusEndpoint";

    public override Task<ReusableActivityImportReceipt> HandleAsync(GetReusableActivityImportStatusRequest request, CancellationToken cancellationToken) =>
        service.GetStatusAsync(request.IdempotencyKey, ReusableActivityImportHttp.Scope(HttpContext.User), cancellationToken).AsTask();
}
