using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>Authored task values captured when a workflow first reaches the activity.</summary>
public sealed record NativeUserTaskRequest(
    string Title, string? Description, string? AssigneeId, string? GroupId,
    IReadOnlyList<string> Actions, IReadOnlyList<string> OptionalFormActions, JsonObject Prefill,
    string? FormId, string? FormVersion);

/// <summary>A private workflow wait whose materialization follows the committed actor turn.</summary>
public sealed record UserTaskWaitTrigger(NativeUserTaskRequest Request) : Trigger;
