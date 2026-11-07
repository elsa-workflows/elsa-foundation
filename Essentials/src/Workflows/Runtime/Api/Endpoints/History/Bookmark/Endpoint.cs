using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.History.Bookmark;

[Get("runtime/workflows/instances/{workflowExecutionId}/history/bookmarks/{bookmarkId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading a bookmark lifecycle")]
public sealed class Endpoint(RuntimeHistoryInspectionService history) : ApiEndpoint<GetRuntimeHistoryBookmark, RuntimeHistoryBookmarkDetailsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetRuntimeHistoryBookmark";
    public override Task<RuntimeHistoryBookmarkDetailsView> HandleAsync(GetRuntimeHistoryBookmark request, CancellationToken cancellationToken) =>
        history.FindBookmarkAsync(request, cancellationToken);
}
