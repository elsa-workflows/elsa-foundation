using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.UserTasks;

internal static class UserTaskHttpApi
{
    internal const string CapabilityId = "elsa.api.user-tasks";

    // Route discovery is also the contract for clients that connect to different execution backends.
    internal static readonly ApiCapabilityLink[] ArchiveLinks =
    [
        new("tasks", "runtime/user-tasks"), new("task", "runtime/user-tasks/{id}", templated: true),
        new("forms", "runtime/user-task-forms"), new("form-definitions", "runtime/user-task-forms/definitions"),
        new("form-definition", "runtime/user-task-forms/{id}/definition", templated: true),
        new("form-history", "runtime/user-task-forms/{id}/versions", templated: true),
        new("save-form-draft", "runtime/user-task-forms/{id}/draft", templated: true),
        new("discard-form-draft", "runtime/user-task-forms/{id}/discard", templated: true),
        new("publish-form", "runtime/user-task-forms/{id}/publish", templated: true),
        new("delete-form", "runtime/user-task-forms/{id}/delete", templated: true),
        new("form-version", "runtime/user-task-forms/{id}/versions/{version}", templated: true),
        new("identity", "runtime/user-task-identity")
    ];

    internal static readonly ApiCapabilityLink[] ParticipantLinks =
    [
        new("claim", "runtime/user-tasks/{id}/claim", templated: true),
        new("release", "runtime/user-tasks/{id}/release", templated: true),
        new("complete", "runtime/user-tasks/{id}/complete", templated: true),
        new("targets", "runtime/user-task-targets")
    ];

    internal static void MapEndpoints(EndpointGroup api, bool includeAssignmentTargets, bool includeParticipantActions)
    {
        api.MapEndpoint<ListTasks>();
        api.MapEndpoint<ReadTask>();
        api.MapEndpoint<ListTaskForms>();
        api.MapEndpoint<ListTaskFormDefinitions>();
        api.MapEndpoint<ReadTaskFormDefinition>();
        api.MapEndpoint<ListTaskFormVersions>();
        api.MapEndpoint<SaveTaskFormDraft>();
        api.MapEndpoint<DiscardTaskFormDraft>();
        api.MapEndpoint<DeleteTaskForm>();
        api.MapEndpoint<PublishTaskForm>();
        api.MapEndpoint<CreateTaskForm>();
        api.MapEndpoint<TaskIdentity>();
        api.MapEndpoint<ReadTaskForm>();
        if (includeAssignmentTargets)
            api.MapEndpoint<ReadAssignmentTargets>();
        if (includeParticipantActions)
        {
            api.MapEndpoint<ClaimTask>();
            api.MapEndpoint<ReleaseTask>();
            api.MapEndpoint<CompleteTask>();
        }
    }

    internal static IEnumerable<Permission> ReadAndFormPermissions() =>
    [
        new(UserTaskPermissions.Read, "Read my tasks", "User Tasks", "Read assigned tasks and eligible group work."),
        new(UserTaskPermissions.Manage, "Manage user tasks", "User Tasks", "Inspect tenant work and reassign it where supported."),
        new(UserTaskPermissions.FormsRead, "Read task forms", "User Tasks", "Use published reusable task form versions."),
        new(UserTaskPermissions.FormsManage, "Manage task forms", "User Tasks", "Manage drafts and publish immutable reusable task form versions.")
    ];
}
