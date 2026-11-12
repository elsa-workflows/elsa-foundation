using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal static class UserTaskRules
{
    internal static string Id(params string[] parts) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts))));
    internal static bool Terminal(UserTaskRecord task) => task.Status is UserTaskStatus.Completed or UserTaskStatus.Cancelled;
    internal static bool CanSee(UserTaskRecord task, string user, IReadOnlySet<string> groups, bool manage = false) => manage ||
        (task.GroupId is { } group ? groups.Contains(group) && (task.ClaimantId is null || task.ClaimantId == user) : task.AssigneeId == user);
    internal static bool Matches(UserTaskRecord task, UserTaskQuery query) => task.TenantId == query.TenantId && task.Status != UserTaskStatus.Staged &&
        (query.UserId is null || CanSee(task, query.UserId, query.GroupIds)) &&
        (query.View switch
        {
            "mine" => !Terminal(task) && (task.AssigneeId == query.UserId || task.ClaimantId == query.UserId),
            "queue" => task.Status == UserTaskStatus.Open && task.GroupId is { } group && query.GroupIds.Contains(group) && task.ClaimantId is null,
            "history" => Terminal(task),
            "all" => true,
            _ => false
        }) && (string.IsNullOrWhiteSpace(query.Search) || task.Title.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
    internal static void ValidateCreation(UserTaskCreation request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 500)
            throw new UserTaskException("validation", "Enter a task title of 1–500 characters.");
        if (request.Description?.Length > 10000)
            throw new UserTaskException("validation", "Shorten the task description to 10,000 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.AssigneeId) && string.IsNullOrWhiteSpace(request.GroupId))
            throw new UserTaskException("validation", "Select a task assignee or a group.");
        if (!string.IsNullOrWhiteSpace(request.AssigneeId) && !string.IsNullOrWhiteSpace(request.GroupId))
            throw new UserTaskException("validation", "Choose either an assignee or a group; clear the other assignment.");
        if (request.Actions is null || request.Actions.Count is < 1 or > 20 ||
            request.Actions.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || x != x.Trim() || x == "Unmatched") ||
            request.Actions.Distinct(StringComparer.Ordinal).Count() != request.Actions.Count)
            throw new UserTaskException("validation", "Enter 1–20 unique task actions of 1–100 characters, without surrounding spaces or the reserved name Unmatched.");
        if (request.OptionalFormActions is null || request.OptionalFormActions.Any(x => !request.Actions.Contains(x, StringComparer.Ordinal)))
            throw new UserTaskException("validation", "Optional form actions must match the task's action names.");
        if (request.Prefill.ValueKind != JsonValueKind.Object)
            throw new UserTaskException("validation", "Prefill must be a JSON object, for example {}. Remove quotes around JSON text.");
        if (request.FormId is not null && string.IsNullOrWhiteSpace(request.FormVersion))
            throw new UserTaskException("validation", "Select a published version for the task form.");
    }

    internal static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
