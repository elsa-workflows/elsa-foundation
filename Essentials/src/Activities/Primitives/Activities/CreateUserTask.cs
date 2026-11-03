using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>Creates durable human work and waits for its accepted response through one exact invocation-local wait.</summary>
[Activity(Icon = "ClipboardCheck")]
[ActivityValueOutcomes(nameof(Actions))]
[RefusesSecretBinding(nameof(Title), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(Description), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(AssigneeId), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(GroupId), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(Actions), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(OptionalFormActions), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(FormId), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(FormVersion), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(Prefill), SecretBindingRefusalReason.PersistedByActivity)]
public sealed class CreateUserTask(IUserTaskRuntime tasks) : StatefulActivity<UserTaskResponse, UserTaskWait, UserTaskResponse>
{
    public const string ActivityType = UserTaskProtocol.ActivityType;

    [ActivityInput(DefaultValue = "User task", DefaultSyntax = "Literal"), Required] public string Title { get; set; } = "User task";
    [ActivityInput] public string? Description { get; set; }
    [ActivityInput(UIHint = "user-task-user")] public string? AssigneeId { get; set; }
    [ActivityInput(UIHint = "user-task-group")] public string? GroupId { get; set; }
    [ActivityInput(UIHint = "user-task-actions", DefaultValue = "[\"Approve\",\"Reject\"]", DefaultSyntax = "Object"), Required] public ICollection<string> Actions { get; set; } = ["Approve", "Reject"];
    [ActivityInput(DefaultValue = "[]", DefaultSyntax = "Object")] public ICollection<string> OptionalFormActions { get; set; } = [];
    [ActivityInput(UIHint = "user-task-form")] public string? FormId { get; set; }
    [ActivityInput] public string? FormVersion { get; set; }
    [ActivityInput(DefaultValue = "{}", DefaultSyntax = "Object")] public JsonElement Prefill { get; set; } = JsonSerializer.SerializeToElement(new { });

    protected override async ValueTask<ActivityTransition<UserTaskResponse, UserTaskWait>> ExecuteAsync(ActivityExecutionContext context)
    {
        var task = await tasks.StageAsync(new(context.WorkflowExecutionId, context.InvocationId, context.ExecutableNodeId, context.OperationKey,
            Title, Description, AssigneeId, GroupId, Actions.ToArray(), OptionalFormActions.ToArray(), null, Prefill,
            FormId, FormVersion), context.CancellationToken);
        return Suspend(new(task.TenantId, task.Id), [new(UserTaskProtocol.ResumeTarget, UserTaskProtocol.StimulusType, task.Id, ActivityTriggerDeduplicationMode.Once)]);
    }

    [ResumeTarget(UserTaskProtocol.ResumeTarget)]
    protected override async ValueTask<ActivityTransition<UserTaskResponse, UserTaskWait>> ResumeAsync(ActivityResumeContext<UserTaskWait, UserTaskResponse> context)
    {
        if (context.Trigger.TaskId != context.State.TaskId) throw new UserTaskException("conflict", "This response belongs to another task.");
        var response = await tasks.ReadAcceptedAsync(context.State.TenantId, context.State.TaskId, context.Trigger.CompletionId, context.CancellationToken);
        return Complete(response, response.Action);
    }
}

public sealed record UserTaskWait(string TenantId, string TaskId);
