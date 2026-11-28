using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>Opens one durable task with a frozen optional form, then waits for its exact accepted response.</summary>
public sealed class CreateUserTask : StatefulActivity<NativeUserTaskResponse, Unit, NativeUserTaskResponse>
{
    public const string ActivityType = "Elsa.CreateUserTask";

    [Input] public string Title { get; set; } = "User task";
    [Input] public string? Description { get; set; }
    [Input] public string? AssigneeId { get; set; }
    [Input] public string? GroupId { get; set; }
    [Input] public string[] Actions { get; set; } = ["Approve", "Reject"];
    [Input] public string[] OptionalFormActions { get; set; } = [];
    [Input] public string? FormId { get; set; }
    [Input] public string? FormVersion { get; set; }
    [Input] public JsonNode? Prefill { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(StartContext<NativeUserTaskResponse> context)
    {
        if (Prefill is not (null or JsonObject))
            return new(Fault("user-task.prefill-invalid", "Task prefill must be an object."));

        var request = new NativeUserTaskRequest(Title, Description, AssigneeId, GroupId, Actions, OptionalFormActions,
            (JsonObject?)Prefill ?? [], FormId, FormVersion);
        return new(Suspend(default, new UserTaskWaitTrigger(request)));
    }

    protected override ValueTask<Transition> ResumeAsync(ResumeContext<Unit, NativeUserTaskResponse> context) =>
        new(Complete(context.Trigger, context.Trigger.Action));
}
