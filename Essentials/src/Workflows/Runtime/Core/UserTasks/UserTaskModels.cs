using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.UserTasks;

/// <summary>Stable protocol shared by task activities, journals and integrations.</summary>
public static class UserTaskProtocol
{
    public const string ActivityType = "Elsa.CreateUserTask";
    public const string StimulusType = "Elsa.UserTask";
    public const string ResumeTarget = "resume-target:user-task";
    public const string BasicForms = "elsa.basic-forms/1";
}

public enum UserTaskStatus { Staged, Open, Claimed, CompletionPending, Completed, Cancelled }

/// <summary>Frozen safe field definition; keys and choices use ordinal, case-sensitive identity.</summary>
public sealed record UserTaskField(string Key, string Label, string Kind, bool Required = false, bool ReadOnly = false,
    string? Help = null, decimal? Minimum = null, decimal? Maximum = null, int? MaxLength = null,
    IReadOnlyList<string>? Choices = null);

public sealed record UserTaskForm(string Id, string Version, string Name, string Provider, IReadOnlyList<UserTaskField> Fields);
public sealed record UserTaskFormPage(IReadOnlyList<UserTaskFormVersion> Items, string? Next);
public sealed record UserTaskFormVersion(string TenantId, UserTaskForm Form, DateTimeOffset CreatedAt, string CreatedBy);
public sealed record UserTaskHistory(string Operation, string ActorId, DateTimeOffset At, string? Action = null,
    string? AssigneeId = null, string? GroupId = null);
public sealed record UserTaskResponse(
    [property: JsonPropertyName("taskId"), Output(Key = "TaskId", Path = "taskId")] string TaskId,
    [property: JsonPropertyName("completionId")] string CompletionId,
    [property: JsonPropertyName("action"), Output(Key = "Action", Path = "action")] string Action,
    [property: JsonPropertyName("data"), Output(Key = "Data", Path = "data")] JsonElement Data);
public sealed record UserTaskDecision(string RequestId, string ActorId, UserTaskResponse Response, DateTimeOffset AcceptedAt);

/// <summary>Authoritative conditional journal aggregate, including its immutable accepted delivery responsibility.</summary>
public sealed record UserTaskRecord(string Id, string TenantId, string WorkflowExecutionId, string InvocationId,
    string ExecutableNodeId, string OperationKey, string Title, string? Description, string? AssigneeId, string? GroupId,
    string? ClaimantId, IReadOnlyList<string> Actions, IReadOnlyList<string> OptionalFormActions,
    UserTaskForm? Form, JsonElement Prefill, UserTaskStatus Status, long Revision, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, IReadOnlyList<UserTaskHistory> History, StimulusResumeRecipient? Recipient = null,
    UserTaskDecision? Decision = null);

public sealed record UserTaskCreation(string WorkflowExecutionId, string InvocationId, string ExecutableNodeId,
    string OperationKey, string Title, string? Description, string? AssigneeId, string? GroupId,
    IReadOnlyList<string> Actions, IReadOnlyList<string> OptionalFormActions, UserTaskForm? Form, JsonElement Prefill,
    string? FormId = null, string? FormVersion = null);

/// <summary>A trusted current actor produced by a host's eligibility adapter, never deserialized from a client.</summary>
public sealed record UserTaskActor(string TenantId, string UserId, IReadOnlySet<string> GroupIds, bool CanRead,
    bool CanAct, bool CanManage, bool CanManageForms, bool CanReadForms = false);

/// <summary>Bounded provider query. After is a provider scan position; participant operations protect public cursors and reauthorize every returned row.</summary>
public sealed record UserTaskQuery(string TenantId, string? UserId, IReadOnlySet<string> GroupIds, string View,
    int Limit = 25, string? After = null, string? Search = null);
public sealed record UserTaskPage(IReadOnlyList<UserTaskRecord> Items, string? Next);
public sealed record UserTaskMutation(long Revision, string RequestId);
public sealed record UserTaskCompletion(long Revision, string RequestId, string Action, JsonElement Data);
public sealed record UserTaskAssignment(long Revision, string RequestId, string? AssigneeId, string? GroupId);

/// <summary>Safe domain refusal. Errors are field-keyed only for form validation.</summary>
public sealed class UserTaskException(string code, string message, IReadOnlyDictionary<string, string>? errors = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string>? Errors { get; } = errors;
}

/// <summary>Task development policy and protected inbox paging configuration.</summary>
public sealed class UserTaskOptions
{
    public bool AllowVolatileTasksForDevelopment { get; set; }
    /// <summary>
    /// Optional Base64-encoded 32-byte key shared by API replicas. Otherwise a configured recovery signing key
    /// is domain-separated for inbox cursors; unconfigured development hosts use a process-local key.
    /// </summary>
    public string? CursorEncryptionKey { get; set; }
}
