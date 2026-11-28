using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Core.UserTasks;
using MinimalEndpoints;
using Elsa.Workflows.Runtime.Api.Endpoints;

namespace Elsa.Workflows.Runtime.Api.UserTasks;

internal sealed record TaskMutationRequest(long Revision, string RequestId);
internal sealed record TaskCompletionRequest(long Revision, string RequestId, string Action, JsonElement Data);

[Post("runtime/user-tasks/{id}/claim")]
[RequirePermission(UserTaskPermissions.Act)]
[RuntimeProblems("processing user tasks")]
internal sealed class ClaimTask(IUserTaskOperations tasks, UserTaskPeople people) : ApiEndpoint<TaskMutationRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "ClaimUserTask"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskView> HandleAsync(TaskMutationRequest request, CancellationToken cancellationToken) =>
        people.ViewAsync(tasks.ClaimAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, new(request.Revision, request.RequestId), cancellationToken), cancellationToken);
}

[Post("runtime/user-tasks/{id}/release")]
[RequirePermission(UserTaskPermissions.Act)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReleaseTask(IUserTaskOperations tasks, UserTaskPeople people) : ApiEndpoint<TaskMutationRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "ReleaseUserTask"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskView> HandleAsync(TaskMutationRequest request, CancellationToken cancellationToken) =>
        people.ViewAsync(tasks.ReleaseAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, new(request.Revision, request.RequestId), cancellationToken), cancellationToken);
}

[Post("runtime/user-tasks/{id}/complete")]
[RequirePermission(UserTaskPermissions.Act)]
[RuntimeProblems("processing user tasks")]
internal sealed class CompleteTask(IUserTaskOperations tasks, UserTaskPeople people) : ApiEndpointWithResult<TaskCompletionRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "CompleteUserTask"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override async Task<EndpointResult<UserTaskView>> HandleAsync(TaskCompletionRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.CompleteAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, new(request.Revision, request.RequestId, request.Action, request.Data), cancellationToken);
        return EndpointResult.Status(result.Status == UserTaskStatus.CompletionPending ? 202 : 200, await people.ViewAsync(result, cancellationToken));
    }
}
