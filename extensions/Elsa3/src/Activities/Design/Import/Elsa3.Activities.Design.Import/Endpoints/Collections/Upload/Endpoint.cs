using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.AspNetCore.Http;
using MinimalEndpoints;
using Microsoft.Net.Http.Headers;

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
        var request = HttpContext.Request;
        var scope = ReusableActivityImportHttp.Scope(HttpContext.User);
        ReusableActivityImportUploadResult result;
        // The host's typed headers can use a different copy of the header types than a feed-loaded feature.
        if (MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            && type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            if (service is not IReusableActivityExportFileUpload upload)
                throw new RawCollectionOnlyException();
            var boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value ?? "";
            result = await upload.UploadExportFilesAsync(request.Body, boundary, request.ContentLength, scope, cancellationToken);
        }
        else
            result = await service.UploadAsync(request.Body, request.ContentLength, scope, cancellationToken);

        HttpContext.Response.Headers.Location = $"/migration/elsa3/reusable-activities/collections/{Uri.EscapeDataString(result.CollectionHandle)}/analysis";
        return result;
    }
}
