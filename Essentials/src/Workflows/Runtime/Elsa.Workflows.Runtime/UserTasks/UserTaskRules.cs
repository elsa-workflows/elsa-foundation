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
    internal static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
