using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal sealed class UserTaskResumeGuard(IUserTaskStore tasks, IWorkflowExecutionStateStore executions) : IEventHandler<ActivityResumeValidating>
{
    public async Task Handle(ActivityResumeValidating @event, CancellationToken cancellationToken)
    {
        if (@event.Node.ActivityType != UserTaskProtocol.ActivityType) return;
        try
        {
            var payload = @event.Payload;
            var response = payload.Input?.Deserialize<UserTaskResponse>() ?? throw Invalid();
            var execution = await executions.FindAsync(@event.State.Execution.WorkflowExecutionId, cancellationToken);
            var task = execution?.TenantId is { } tenant ? await tasks.FindAsync(tenant, response.TaskId, cancellationToken) : null;
            if (task?.Status != UserTaskStatus.CompletionPending || task.Decision is null || task.Recipient is null ||
                task.WorkflowExecutionId != @event.State.Execution.WorkflowExecutionId ||
                task.InvocationId != @event.State.InvocationId || task.InvocationId != payload.ActivityExecutionId ||
                task.ExecutableNodeId != payload.ExecutableNodeId || task.Recipient.BookmarkId != payload.BookmarkId ||
                payload.StimulusType != UserTaskProtocol.StimulusType || payload.StimulusHash != task.Id ||
                payload.ExpectedBookmarkResume is null ||
                !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(task.Recipient), JsonSerializer.SerializeToElement(payload.ExpectedBookmarkResume)) ||
                !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(task.Decision.Response), JsonSerializer.SerializeToElement(response)))
                throw Invalid();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (UserTaskAvailability.IsUnavailable(exception, cancellationToken))
        {
            throw new RuntimeSchedulerWorkUnavailableException("user-task.unavailable", "The task journal cannot be validated now; the response remains owed.", exception);
        }
    }
    private static UserTaskException Invalid() => new("conflict", "The response does not match this task's accepted decision and exact wait.");
}
