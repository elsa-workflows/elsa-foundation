using System.Text.Json;
using System.Text.Json.Serialization;
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
internal sealed record FormDefinitionRequest(string Id);
internal sealed record FormHistoryRequest(string Id, int Limit = 25, string? After = null);
internal sealed record FormDraftRequest(long Revision, UserTaskForm Form);
internal sealed record FormRevisionRequest(long Revision);
internal sealed record FormPublishRequest(long Revision, UserTaskForm Form);
/// <summary>A task as its readers see it: each stored user or group ID travels with its directory display name, or null when the directory has none.</summary>
internal sealed record UserTaskView(string Id, string WorkflowExecutionId, string Title, string? Description,
    string? AssigneeId, string? AssigneeName, string? GroupId, string? GroupName, string? ClaimantId, string? ClaimantName,
    IReadOnlyList<string> Actions, IReadOnlyList<string> OptionalFormActions,
    UserTaskForm? Form, JsonElement Prefill, string Status, long Revision, IReadOnlyList<UserTaskHistoryView> History, UserTaskDecision? Decision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? SupportsActions = null);
internal sealed record UserTaskHistoryView(string Operation, string ActorId, string? ActorName, DateTimeOffset At, string? Action, string? AssigneeId, string? GroupId);
internal sealed record UserTaskPageView(IReadOnlyList<UserTaskView> Items, string? Next);
internal sealed record UserTaskProblem(string Code, string Message, JsonElement? Errors);

[Get("runtime/user-tasks")]
[RequireAnyPermission(UserTaskPermissions.Read, UserTaskPermissions.Manage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ListTasks(IUserTaskOperations tasks, UserTaskPeople people) : ApiEndpoint<TaskListRequest, UserTaskPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListUserTasks";
    public override async Task<UserTaskPageView> HandleAsync(TaskListRequest request, CancellationToken cancellationToken)
    {
        var page = await tasks.ListAsync(request.View, request.Limit, request.After, request.Search, cancellationToken);
        return new(await people.ViewAsync(page.Items, cancellationToken), page.Next);
    }
}

[Get("runtime/user-tasks/{id}")]
[RequireAnyPermission(UserTaskPermissions.Read, UserTaskPermissions.Manage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReadTask(IUserTaskOperations tasks, UserTaskPeople people) : ApiEndpoint<TaskIdRequest, UserTaskView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetUserTask";
    public override Task<UserTaskView> HandleAsync(TaskIdRequest request, CancellationToken cancellationToken) =>
        people.ViewAsync(tasks.GetAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, cancellationToken), cancellationToken);
}

[Get("runtime/user-task-forms")]
[RequireAnyPermission(UserTaskPermissions.FormsRead, UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ListTaskForms(IUserTaskOperations tasks) : ApiEndpoint<FormPageRequest, UserTaskFormPage>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListUserTaskForms";
    public override Task<UserTaskFormPage> HandleAsync(FormPageRequest request, CancellationToken cancellationToken) => tasks.ListFormsAsync(request.Limit, request.After, cancellationToken).AsTask();
}

[Get("runtime/user-task-forms/definitions")]
[RequireAnyPermission(UserTaskPermissions.FormsRead, UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ListTaskFormDefinitions(IUserTaskOperations tasks) : ApiEndpoint<FormPageRequest, UserTaskFormDefinitionPage>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListUserTaskFormDefinitions";
    public override Task<UserTaskFormDefinitionPage> HandleAsync(FormPageRequest request, CancellationToken cancellationToken) =>
        tasks.ListFormDefinitionsAsync(request.Limit, request.After, cancellationToken).AsTask();
}

[Get("runtime/user-task-forms/{id}/definition")]
[RequireAnyPermission(UserTaskPermissions.FormsRead, UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReadTaskFormDefinition(IUserTaskOperations tasks) : ApiEndpoint<FormDefinitionRequest, UserTaskFormDefinition>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetUserTaskFormDefinition";
    public override Task<UserTaskFormDefinition> HandleAsync(FormDefinitionRequest request, CancellationToken cancellationToken) =>
        tasks.GetFormDefinitionAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, cancellationToken).AsTask();
}

[Get("runtime/user-task-forms/{id}/versions")]
[RequireAnyPermission(UserTaskPermissions.FormsRead, UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ListTaskFormVersions(IUserTaskOperations tasks) : ApiEndpoint<FormHistoryRequest, UserTaskFormPage>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListUserTaskFormVersions";
    public override Task<UserTaskFormPage> HandleAsync(FormHistoryRequest request, CancellationToken cancellationToken) =>
        tasks.ListFormVersionsAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, request.Limit, request.After, cancellationToken).AsTask();
}

[Post("runtime/user-task-forms/{id}/draft")]
[RequirePermission(UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class SaveTaskFormDraft(IUserTaskOperations tasks) : ApiEndpoint<FormDraftRequest, UserTaskFormDefinition>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "SaveUserTaskFormDraft"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskFormDefinition> HandleAsync(FormDraftRequest request, CancellationToken cancellationToken) =>
        tasks.SaveFormDraftAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, request.Form, request.Revision, cancellationToken).AsTask();
}

[Post("runtime/user-task-forms/{id}/discard")]
[RequirePermission(UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class DiscardTaskFormDraft(IUserTaskOperations tasks) : ApiEndpoint<FormRevisionRequest, UserTaskFormDefinition>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "DiscardUserTaskFormDraft"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskFormDefinition> HandleAsync(FormRevisionRequest request, CancellationToken cancellationToken) =>
        tasks.DiscardFormDraftAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, request.Revision, cancellationToken).AsTask();
}

[Post("runtime/user-task-forms/{id}/delete")]
[RequirePermission(UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class DeleteTaskForm(IUserTaskOperations tasks) : ApiEndpoint<FormRevisionRequest, UserTaskFormDefinition>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "DeleteUserTaskForm"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskFormDefinition> HandleAsync(FormRevisionRequest request, CancellationToken cancellationToken) =>
        tasks.DeleteFormAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, request.Revision, cancellationToken).AsTask();
}

[Post("runtime/user-task-forms/{id}/publish")]
[RequirePermission(UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class PublishTaskForm(IUserTaskOperations tasks) : ApiEndpoint<FormPublishRequest, UserTaskFormVersion>
{
    public override void Configure(ApiEndpointOptions options) { options.Operation = "PublishUserTaskForm"; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; options.StrictTypedParsing = true; }
    public override Task<UserTaskFormVersion> HandleAsync(FormPublishRequest request, CancellationToken cancellationToken) =>
        tasks.PublishFormAsync(Convert.ToString(HttpContext.Request.RouteValues["id"])!, request.Form, request.Revision, cancellationToken).AsTask();
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
