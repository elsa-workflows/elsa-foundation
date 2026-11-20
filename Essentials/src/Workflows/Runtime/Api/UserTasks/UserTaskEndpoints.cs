using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Core.UserTasks;
using MinimalEndpoints;
using Elsa.Workflows.Runtime.Api.Endpoints;

namespace Elsa.Workflows.Runtime.Api.UserTasks;

internal sealed record TaskListRequest(string View = "mine", int Limit = 25, string? After = null, string? Search = null);
internal sealed record TaskIdRequest(string Id);
internal sealed record FormPageRequest(int Limit = 25, string? After = null);
internal sealed record FormVersionRequest(string Id, string Version);
internal sealed record TaskMutationRequest(long Revision, string RequestId);
internal sealed record TaskCompletionRequest(long Revision, string RequestId, string Action, JsonElement Data);
internal sealed record TaskAssignmentRequest(long Revision, string RequestId, string? AssigneeId, string? GroupId);
internal sealed record UserTaskView(string Id, string WorkflowExecutionId, string Title, string? Description,
    string? AssigneeId, string? GroupId, string? ClaimantId, IReadOnlyList<string> Actions, IReadOnlyList<string> OptionalFormActions,
    UserTaskForm? Form, JsonElement Prefill, string Status, long Revision, IReadOnlyList<UserTaskHistory> History, UserTaskDecision? Decision)
{
    internal static UserTaskView From(UserTaskRecord task) => new(task.Id, task.WorkflowExecutionId, task.Title, task.Description,
        task.AssigneeId, task.GroupId, task.ClaimantId, task.Actions, task.OptionalFormActions, task.Form, task.Prefill,
        task.Status.ToString(), task.Revision, task.History, task.Decision);
    internal static async Task<UserTaskView> Map(ValueTask<UserTaskRecord> task) => From(await task);
}
internal sealed record UserTaskPageView(IReadOnlyList<UserTaskView> Items, string? Next);
internal sealed record UserTaskProblem(string Code, string Message, JsonElement? Errors);

[Get("runtime/user-tasks")]
[RequireAnyPermission(UserTaskPermissions.Read, UserTaskPermissions.Manage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ListTasks(IUserTaskOperations tasks) : ApiEndpoint<TaskListRequest, UserTaskPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListUserTasks";
    public override async Task<UserTaskPageView> HandleAsync(TaskListRequest request, CancellationToken cancellationToken)
    {
        var page = await tasks.ListAsync(request.View, request.Limit, request.After, request.Search, cancellationToken);
        return new(page.Items.Select(UserTaskView.From).ToArray(), page.Next);
    }
}

[Get("runtime/user-tasks/{id}")]
[RequireAnyPermission(UserTaskPermissions.Read, UserTaskPermissions.Manage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReadTask(IUserTaskOperations tasks) : ApiEndpoint<TaskIdRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetUserTask";
    public override Task<UserTaskView> HandleAsync(TaskIdRequest request, CancellationToken cancellationToken) =>
        UserTaskView.Map(tasks.GetAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, cancellationToken));
}

[Post("runtime/user-tasks/{id}/claim")]
[RequirePermission(UserTaskPermissions.Act)]
[RuntimeProblems("processing user tasks")]
internal sealed class ClaimTask(IUserTaskOperations tasks) : ApiEndpoint<TaskMutationRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "ClaimUserTask"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskView> HandleAsync(TaskMutationRequest request, CancellationToken cancellationToken) =>
        UserTaskView.Map(tasks.ClaimAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, new(request.Revision, request.RequestId), cancellationToken));
}

[Post("runtime/user-tasks/{id}/release")]
[RequirePermission(UserTaskPermissions.Act)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReleaseTask(IUserTaskOperations tasks) : ApiEndpoint<TaskMutationRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "ReleaseUserTask"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskView> HandleAsync(TaskMutationRequest request, CancellationToken cancellationToken) =>
        UserTaskView.Map(tasks.ReleaseAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, new(request.Revision, request.RequestId), cancellationToken));
}

[Post("runtime/user-tasks/{id}/complete")]
[RequirePermission(UserTaskPermissions.Act)]
[RuntimeProblems("processing user tasks")]
internal sealed class CompleteTask(IUserTaskOperations tasks) : ApiEndpointWithResult<TaskCompletionRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "CompleteUserTask"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override async Task<EndpointResult<UserTaskView>> HandleAsync(TaskCompletionRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.CompleteAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, new(request.Revision, request.RequestId, request.Action, request.Data), cancellationToken);
        return EndpointResult.Status(result.Status == UserTaskStatus.CompletionPending ? 202 : 200, UserTaskView.From(result));
    }
}

[Post("runtime/user-tasks/{id}/reassign")]
[RequirePermission(UserTaskPermissions.Manage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReassignTask(IUserTaskOperations tasks) : ApiEndpoint<TaskAssignmentRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "ReassignUserTask"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskView> HandleAsync(TaskAssignmentRequest request, CancellationToken cancellationToken) =>
        UserTaskView.Map(tasks.ReassignAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, new(request.Revision, request.RequestId, request.AssigneeId, request.GroupId), cancellationToken));
}

[Get("runtime/user-task-forms")]
[RequireAnyPermission(UserTaskPermissions.FormsRead, UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ListTaskForms(IUserTaskOperations tasks) : ApiEndpoint<FormPageRequest, UserTaskFormPage>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListUserTaskForms";
    public override Task<UserTaskFormPage> HandleAsync(FormPageRequest request, CancellationToken cancellationToken) => tasks.ListFormsAsync(request.Limit, request.After, cancellationToken).AsTask();
}

[Post("runtime/user-task-forms")]
[RequirePermission(UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class CreateTaskForm(IUserTaskOperations tasks) : ApiEndpoint<UserTaskForm, UserTaskFormVersion>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "CreateUserTaskForm"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskFormVersion> HandleAsync(UserTaskForm request, CancellationToken cancellationToken) => tasks.CreateFormAsync(request, cancellationToken).AsTask();
}

[Get("runtime/user-task-identity")]
[RequireAnyPermission(UserTaskPermissions.Read, UserTaskPermissions.Manage, UserTaskPermissions.FormsRead, UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class TaskIdentity(IUserTaskAccess access) : ApiEndpointWithoutRequest<UserTaskActor>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "UserTaskIdentity";
    public override Task<UserTaskActor> HandleAsync(CancellationToken cancellationToken) => access.GetActorAsync(cancellationToken).AsTask();
}

[Get("runtime/user-task-forms/{id}/versions/{version}")]
[RequireAnyPermission(UserTaskPermissions.FormsRead, UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReadTaskForm(IUserTaskOperations tasks) : ApiEndpoint<FormVersionRequest, UserTaskFormVersion>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetUserTaskFormVersion";
    public override Task<UserTaskFormVersion> HandleAsync(FormVersionRequest request, CancellationToken cancellationToken) =>
        tasks.GetFormAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, Convert.ToString(HttpContext.Request.RouteValues["version"])!, cancellationToken).AsTask();
}
