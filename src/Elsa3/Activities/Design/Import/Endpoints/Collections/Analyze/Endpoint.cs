using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.Extensions.Options;

namespace Elsa3.Activities.Design.Import.Endpoints.Collections.Analyze;

/// <summary>The analysis page request. Unparseable paging values read as absent, like the mapper's helpers.</summary>
public sealed record AnalyzeReusableActivityCollectionRequest(string CollectionHandle, int? Offset = null, int? Limit = null);

[Get("migration/elsa3/reusable-activities/collections/{collectionHandle}/analysis")]
[RequirePermission(Elsa3ImportPermissions.Read)]
public sealed class Endpoint(IReusableActivityImportOperationService service, IOptions<ReusableActivityImportOptions> options)
    : ApiEndpoint<AnalyzeReusableActivityCollectionRequest, ReusableActivityImportAnalysisPage>
{
    public override void Configure(ApiEndpointOptions apiOptions) => apiOptions.Operation = "AnalyzeReusableActivityCollectionEndpoint";

    public override Task<ReusableActivityImportAnalysisPage> HandleAsync(AnalyzeReusableActivityCollectionRequest request, CancellationToken cancellationToken) =>
        service.AnalyzeAsync(
            request.CollectionHandle,
            request.Offset ?? 0,
            request.Limit ?? options.Value.DefaultPageSize,
            ReusableActivityImportHttp.Scope(HttpContext.User),
            cancellationToken).AsTask();
}
