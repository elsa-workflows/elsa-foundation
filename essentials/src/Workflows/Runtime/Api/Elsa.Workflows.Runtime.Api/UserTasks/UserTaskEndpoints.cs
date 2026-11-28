using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Core.UserTasks;
using MinimalEndpoints;
using Elsa.Workflows.Runtime.Api.Endpoints;

namespace Elsa.Workflows.Runtime.Api.UserTasks;

internal sealed record TaskAssignmentRequest(long Revision, string RequestId, string? AssigneeId, string? GroupId);

[Post("runtime/user-tasks/{id}/reassign")]
[RequirePermission(UserTaskPermissions.Manage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReassignTask(IUserTaskOperations tasks, UserTaskPeople people) : ApiEndpoint<TaskAssignmentRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "ReassignUserTask"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskView> HandleAsync(TaskAssignmentRequest request, CancellationToken cancellationToken) =>
        people.ViewAsync(tasks.ReassignAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, new(request.Revision, request.RequestId, request.AssigneeId, request.GroupId), cancellationToken), cancellationToken);
}
