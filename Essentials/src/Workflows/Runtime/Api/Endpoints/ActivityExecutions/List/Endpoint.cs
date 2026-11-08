using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.List;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("browsing activity executions")]
public sealed class Endpoint(IWorkflowExecutionStateStore workflows, IActivityExecutionInspectionStore inspections,
    IActivityInspectionContextAsync authorization, IRuntimeRecoveryContinuationCodec codec)
    : ApiEndpoint<BrowseActivityExecutions, ActivityExecutionBrowsePageView>
{
    private const string Purpose = "elsa-runtime-activity-execution-browser-v1";
    public override void Configure(ApiEndpointOptions options) => options.Operation = "BrowseActivityExecutions";

    public override async Task<ActivityExecutionBrowsePageView> HandleAsync(BrowseActivityExecutions request, CancellationToken cancellationToken)
    {
        var workflow = await workflows.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (workflow is null || !await authorization.CanInspectStructureAsync(workflow, cancellationToken))
            throw new ActivityExecutionMissingSignal();
        if (!inspections.SupportsExecutionBrowsing) throw new NotSupportedException("This host does not support activity execution browsing.");
        RuntimeStorePageRequest.ValidateLimit(request.Limit, nameof(request.Limit));
        RuntimeStorePageRequest.ValidateContinuationToken(request.Cursor, nameof(request.Cursor));
        if (request.Cursor is not null && request.AnchorActivityExecutionId is not null)
            throw new ArgumentException("Choose either a continuation or an execution anchor.");
        var binding = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
        {
            request.WorkflowExecutionId, request.ExecutableNodeId, request.ParentActivityExecutionId,
            request.AncestorActivityExecutionId, request.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            authorization.TenantScope, authorization.AuditSubject,
            await authorization.GetAuthorizationProfileAsync(cancellationToken)
        }))));
        long? offset = null;
        if (request.Cursor is not null)
        {
            try
            {
                var cursor = JsonSerializer.Deserialize<BrowserCursor>(codec.Decode(Purpose, request.Cursor));
                if (cursor is null || cursor.Binding != binding || cursor.Offset < 0) throw new FormatException();
                offset = cursor.Offset;
            }
            catch (Exception error) when (error is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException)
            {
                throw new ArgumentException("The execution browsing continuation is invalid or belongs to another context.", nameof(request.Cursor));
            }
        }
        var page = await inspections.BrowseAsync(new ActivityExecutionBrowseQuery(request.WorkflowExecutionId,
            request.Limit, offset, request.ExecutableNodeId, request.ParentActivityExecutionId,
            request.AncestorActivityExecutionId, request.AnchorActivityExecutionId), cancellationToken);
        string Cursor(long position) => codec.Encode(Purpose, JsonSerializer.SerializeToUtf8Bytes(new BrowserCursor(binding, position)));
        return new ActivityExecutionBrowsePageView(page.Items.Select(item => new ActivityExecutionBrowseEntryView(
                ActivityExecutionInspectionSummaryView.From(item.Execution), item.Position,
                item.PreviousActivityExecutionId, item.NextActivityExecutionId, ActivityExecutionAttemptView.From(item.Execution.Attempt), item.IterationPosition)).ToArray(),
            page.TotalCount,
            page.Items.Count > 0 && page.Offset > 0 ? Cursor(Math.Max(0, page.Offset - request.Limit)) : null,
            page.Items.Count > 0 && page.Offset + page.Items.Count < page.TotalCount ? Cursor(page.Offset + page.Items.Count) : null);
    }

    private sealed record BrowserCursor(string Binding, long Offset);
}
