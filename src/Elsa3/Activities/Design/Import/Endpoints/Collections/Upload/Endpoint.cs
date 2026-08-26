using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa3.Activities.Design.Import.Endpoints.Collections.Upload;

[Post("migration/elsa3/reusable-activities/collections")]
[RequirePermission(Elsa3ImportPermissions.Manage)]
public sealed class Endpoint(IReusableActivityImportOperationService service) : ApiEndpointWithoutRequest<ReusableActivityImportUploadResult>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UploadReusableActivityCollectionEndpoint";
        options.SuccessStatus = StatusCodes.Status201Created;
        // The published OpenAPI documents this route as 200, as the hand-written mapper always did.
        options.DocumentedStatus = StatusCodes.Status200OK;
    }

    public override async Task<ReusableActivityImportUploadResult> HandleAsync(CancellationToken cancellationToken)
    {
        // The upload payload is the raw request stream (multipart envelopes included), read verbatim.
        var result = await service.UploadAsync(
            HttpContext.Request.Body,
            HttpContext.Request.ContentLength,
            ReusableActivityImportHttp.Scope(HttpContext.User),
            cancellationToken);
        HttpContext.Response.Headers.Location = $"/migration/elsa3/reusable-activities/collections/{Uri.EscapeDataString(result.CollectionHandle)}/analysis";
        return result;
    }
}
