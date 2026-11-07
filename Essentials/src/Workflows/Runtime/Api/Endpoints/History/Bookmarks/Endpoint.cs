using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.History.Bookmarks;

[Get("runtime/workflows/instances/{workflowExecutionId}/history/bookmarks")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading run bookmark history")]
public sealed class Endpoint(RuntimeHistoryInspectionService history) : ApiEndpoint<GetRuntimeHistoryBookmarks, RuntimeHistoryBookmarkPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetRuntimeHistoryBookmarks";
    public override Task<RuntimeHistoryBookmarkPageView> HandleAsync(GetRuntimeHistoryBookmarks request, CancellationToken cancellationToken) =>
        history.ListBookmarksAsync(request, cancellationToken);
}
