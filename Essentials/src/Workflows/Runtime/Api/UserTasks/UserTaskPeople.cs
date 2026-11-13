using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.UserTasks;

/// <summary>
/// Puts display names beside the stored task user and group IDs for anyone who may read the task. Names come from
/// the same tenant directory the eligibility adapter trusts, so readers never need the manager-only target directory.
/// Each distinct ID is read once per request; an ID that is not in the task's tenant directory gets no name.
/// Names are display-only: a directory failure leaves the name out rather than failing a response whose change is already saved.
/// </summary>
internal sealed class UserTaskPeople(IUserStore? users = null, IRoleStore? roles = null, ILogger<UserTaskPeople>? logger = null)
{
    private readonly Dictionary<(string Tenant, string Id), string?> userNames = new();
    private readonly Dictionary<(string Tenant, string Id), string?> groupNames = new();

    public async Task<UserTaskView> ViewAsync(UserTaskRecord task, CancellationToken cancellationToken)
    {
        var history = new UserTaskHistoryView[task.History.Count];
        for (var index = 0; index < history.Length; index++)
        {
            var entry = task.History[index];
            history[index] = new(entry.Operation, entry.ActorId, await UserAsync(task.TenantId, entry.ActorId, cancellationToken), entry.At, entry.Action,
                entry.AssigneeId, entry.GroupId);
        }
        return new(task.Id, task.WorkflowExecutionId, task.Title, task.Description,
            task.AssigneeId, await UserAsync(task.TenantId, task.AssigneeId, cancellationToken),
            task.GroupId, await GroupAsync(task.TenantId, task.GroupId, cancellationToken),
            task.ClaimantId, await UserAsync(task.TenantId, task.ClaimantId, cancellationToken),
            task.Actions, task.OptionalFormActions, task.Form, task.Prefill, task.Status.ToString(), task.Revision, history, task.Decision);
    }

    public async Task<UserTaskView> ViewAsync(ValueTask<UserTaskRecord> task, CancellationToken cancellationToken) => await ViewAsync(await task, cancellationToken);

    public async Task<IReadOnlyList<UserTaskView>> ViewAsync(IReadOnlyList<UserTaskRecord> tasks, CancellationToken cancellationToken)
    {
        var views = new UserTaskView[tasks.Count];
        for (var index = 0; index < views.Length; index++) views[index] = await ViewAsync(tasks[index], cancellationToken);
        return views;
    }

    private async ValueTask<string?> UserAsync(string tenantId, string? id, CancellationToken cancellationToken)
    {
        if (users is null || string.IsNullOrWhiteSpace(id)) return null;
        if (userNames.TryGetValue((tenantId, id), out var known)) return known;
        UserRecord? user;
        try { user = await users.FindAsync(tenantId, id, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return Unnamed(exception); }
        // Disabled people keep their name so history stays readable; only the tenant and identity must match.
        var name = user is not null && user.Id == id && user.TenantId == tenantId ? Readable(user.DisplayName) ?? Readable(user.UserName) : null;
        return userNames[(tenantId, id)] = name;
    }

    private async ValueTask<string?> GroupAsync(string tenantId, string? id, CancellationToken cancellationToken)
    {
        if (roles is null || string.IsNullOrWhiteSpace(id)) return null;
        if (groupNames.TryGetValue((tenantId, id), out var known)) return known;
        RoleRecord? role;
        try { role = await roles.FindAsync(tenantId, id, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return Unnamed(exception); }
        var name = role is not null && role.Id == id && role.TenantId == tenantId ? Readable(role.Name) : null;
        return groupNames[(tenantId, id)] = name;
    }

    private string? Unnamed(Exception exception)
    {
        logger?.LogWarning(exception, "The task directory could not name a task person; the response shows the stored ID.");
        return null;
    }

    private static string? Readable(string? name) => string.IsNullOrWhiteSpace(name) ? null : name;
}
