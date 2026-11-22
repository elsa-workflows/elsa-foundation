using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.UserTasks;

internal sealed class UserTaskRuntime(IUserTaskStore store, IUserTaskAccess eligibility, IWorkflowExecutionStateStore executions,
    IActivityExecutionStateStore activities, IBookmarkStateStore bookmarks, IRuntimeCheckpointCommitStore checkpoints,
    IBookmarkResumeDispatcher dispatcher, IPersistenceAccessContextAccessor access,
    IPersistenceOperationScopeFactory scopes, IEnumerable<IUserTaskFormProvider> forms, TimeProvider time,
    CoalescingInner<IWorkflowExecutionStateStore>? physicalExecutions = null,
    CoalescingInner<IActivityExecutionStateStore>? physicalActivities = null,
    IOptions<UserTaskOptions>? taskOptions = null,
    IExternalPayloadStore? payloads = null, IUserTaskPersistenceBoundary? taskBoundary = null) : IUserTaskRuntime
{
    public async ValueTask<UserTaskRecord> StageAsync(UserTaskCreation request, CancellationToken cancellationToken = default)
    {
        await using var operation = await scopes.CreateAsync(access.Current.RequireScope(), cancellationToken);
        return await operation.ServiceProvider.GetRequiredService<UserTaskRuntime>().StageCoreAsync(request, cancellationToken);
    }

    private async ValueTask<UserTaskRecord> StageCoreAsync(UserTaskCreation request, CancellationToken ct)
    {
        EnsureExactProvider();
        var execution = await executions.FindAsync(request.WorkflowExecutionId, ct) ?? throw Refuse("The owning execution is unavailable.");
        if (execution.Status.IsTerminal() || string.IsNullOrWhiteSpace(execution.TenantId)) throw Refuse("A task requires an active tenant execution.");
        var activity = await activities.FindAsync(request.WorkflowExecutionId, request.InvocationId, ct);
        if (activity is null || activity.Execution.ActivityType != UserTaskProtocol.ActivityType || activity.Execution.ExecutableNodeId != request.ExecutableNodeId)
            throw Refuse("The task does not belong to this activity invocation.");
        if (activity.InputSnapshot?.Values.Values.Any(value => value.Policy.RequiresEncryption || value.Presence == ValuePresence.Withheld) == true)
            throw new UserTaskException("validation", "This task journal cannot store withheld or encryption-required input values.");
        var id = UserTaskRules.Id(access.Current.RequireScope().Value, execution.TenantId, request.WorkflowExecutionId, request.InvocationId);
        if (await store.FindAsync(execution.TenantId, id, ct) is { } prior) return prior;
        UserTaskRules.ValidateCreation(request);
        var form = request.Form;
        if (request.FormId is not null)
            form = (await store.FindFormAsync(execution.TenantId, request.FormId, request.FormVersion ?? "", ct))?.Form
                ?? throw new UserTaskException("validation", "The exact form version is unavailable.");
        if (form is not null)
        {
            var owners = forms.Where(x => x.Provider == form.Provider).ToArray();
            if (owners.Length != 1) throw Refuse("The form provider is missing or ambiguous.");
            owners[0].ValidateDefinition(form);
            var errors = await owners[0].ValidateResponseAsync(form, request.Prefill, request.Prefill, false, ct);
            if (errors.Count != 0) throw new UserTaskException("validation", "The prefilled values do not match the form.", errors);
        }
        else if (request.Prefill.EnumerateObject().Any()) throw new UserTaskException("validation", "Prefilled fields require a form.");
        await eligibility.ValidateAssignmentAsync(execution.TenantId, request.AssigneeId, request.GroupId, ct);
        var now = time.GetUtcNow();
        var task = new UserTaskRecord(id, execution.TenantId, request.WorkflowExecutionId, request.InvocationId, request.ExecutableNodeId,
            request.OperationKey, request.Title, request.Description, request.AssigneeId, request.GroupId, null, request.Actions.ToArray(),
            request.OptionalFormActions.ToArray(), form is null ? null : UserTaskRules.Copy(form), request.Prefill.Clone(), UserTaskStatus.Staged,
            1, now, now, [new("created", "workflow", now, AssigneeId: request.AssigneeId, GroupId: request.GroupId)]);
        if (await store.TryWriteAsync(task, null, ct)) return task;
        return await store.FindAsync(execution.TenantId, id, ct) ?? throw Refuse("The task could not be staged.");
    }

    public async ValueTask<UserTaskResponse> ReadAcceptedAsync(string tenantId, string taskId, string completionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var operation = await scopes.CreateAsync(access.Current.RequireScope(), cancellationToken);
            var journal = operation.ServiceProvider.GetRequiredService<IUserTaskStore>();
            var task = await journal.FindAsync(tenantId, taskId, cancellationToken);
            if (task?.Status is not (UserTaskStatus.CompletionPending or UserTaskStatus.Completed) || task.Decision?.Response.CompletionId != completionId)
                throw new UserTaskException("conflict", "The task has no matching accepted response.");
            return task.Decision.Response;
        }
        catch (Exception exception) when (UserTaskAvailability.IsUnavailable(exception, cancellationToken))
        {
            throw new RuntimeSchedulerWorkUnavailableException("user-task.unavailable", "The accepted response cannot be read now; it remains owed.", exception);
        }
    }

    public async ValueTask ReconcileAsync(UserTaskRecord task, CancellationToken cancellationToken = default)
    {
        await using var operation = await scopes.CreateAsync(access.Current.RequireScope(), cancellationToken);
        await operation.ServiceProvider.GetRequiredService<UserTaskRuntime>().ReconcileCoreAsync(task, cancellationToken);
    }

    private async ValueTask ReconcileCoreAsync(UserTaskRecord hint, CancellationToken ct)
    {
        var task = await store.FindAsync(hint.TenantId, hint.Id, ct);
        if (task is null || UserTaskRules.Terminal(task)) return;
        EnsureExactProvider();
        var rawExecutions = executions.GetType() == typeof(CoalescingWorkflowExecutionStateStore) ? physicalExecutions!.Value : executions;
        var rawActivities = activities.GetType() == typeof(CoalescingActivityExecutionStateStore) ? physicalActivities!.Value : activities;
        var execution = await rawExecutions.FindAsync(task.WorkflowExecutionId, ct);
        var activity = await rawActivities.FindAsync(task.WorkflowExecutionId, task.InvocationId, ct);
        if (execution is not null && execution.TenantId != task.TenantId) throw Refuse("The task's owning execution differs.");
        if (task.Decision is { } decision && activity?.Status == ActivityExecutionStatus.Completed && activity.Completion is { } completion &&
            completion.InvocationId == task.InvocationId && completion.OutcomeKey == decision.Response.Action)
        {
            var result = completion.Result.InlineValue ?? (completion.Result.ExternalReference is { } reference ? await (payloads ?? throw Refuse("The external task result reader is unavailable.")).ReadAsync(reference, ct) : (JsonElement?)null);
            if (result is null || !JsonElement.DeepEquals(result.Value, JsonSerializer.SerializeToElement(decision.Response)))
                throw Refuse("The committed task result differs from its accepted response.");
            await TransitionAsync(task, UserTaskStatus.Completed, "completed", ct);
            return;
        }
        if (execution?.Status.IsTerminal() == true || activity?.Status is ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded ||
            await rawExecutions.IsDeletionTombstonedAsync(task.WorkflowExecutionId, ct))
        {
            await TransitionAsync(task, UserTaskStatus.Cancelled, "cancelled", ct);
            return;
        }
        if (execution is null || activity is null) return; // Missing state is uncertainty, not retirement.
        if (task.Recipient is { } pinned)
        {
            var observed = await bookmarks.ObserveAsync(task.WorkflowExecutionId, pinned.BookmarkId, ct);
            if (observed is not null && observed.Generation != pinned.RegistrationCapture?.Generation)
            {
                await TransitionAsync(task, UserTaskStatus.Cancelled, "wait-replaced", ct);
                return;
            }
        }
        if (task.Status == UserTaskStatus.Staged)
        {
            // The owning invocation retains only a finite collection of current wait identities.
            foreach (var id in activity.BookmarkIds)
            {
                var observed = await bookmarks.ObserveAsync(task.WorkflowExecutionId, id, ct);
                if (observed is null || observed.State.ActivityExecutionId != task.InvocationId || observed.State.StimulusType != UserTaskProtocol.StimulusType ||
                    observed.State.StimulusHash != task.Id) continue;
                var recipient = StimulusResumeRecipient.From(observed, execution, access.Current.RequireScope());
                if (!recipient.MatchesAdmission(observed, execution, activity, access.Current.RequireScope(), time.GetUtcNow())) continue;
                await store.TryWriteAsync(task with { Recipient = recipient, Status = UserTaskStatus.Open, Revision = task.Revision + 1, UpdatedAt = time.GetUtcNow() }, task.Revision, ct);
                return;
            }
            return;
        }
        if (task.Status != UserTaskStatus.CompletionPending || task.Decision is null || task.Recipient is null) return;
        var request = new BookmarkResumeDispatchRequest(task.WorkflowExecutionId, UserTaskProtocol.StimulusType, task.Id,
            JsonSerializer.SerializeToElement(task.Decision.Response), task.Decision.Response.CompletionId, "user-tasks", null, null, null,
            task.Recipient.BookmarkId, null) { ExpectedRecipient = task.Recipient };
        // Queue admission is deliberately not a journal terminal transition. Fresh recovery proves the canonical result.
        _ = await dispatcher.DispatchAsync(request, cancellationToken: ct);
    }

    private async ValueTask TransitionAsync(UserTaskRecord task, UserTaskStatus status, string operation, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await store.TryWriteAsync(task with { Status = status, Revision = task.Revision + 1, UpdatedAt = now,
            History = task.History.Append(new UserTaskHistory(operation, task.Decision?.ActorId ?? "workflow", now, task.Decision?.Response.Action)).ToArray() }, task.Revision, ct);
    }

    private void EnsureExactProvider()
    {
        var rawExecutions = executions.GetType() == typeof(CoalescingWorkflowExecutionStateStore) ? physicalExecutions?.Value : executions;
        if (!store.IsDurable && (rawExecutions?.GetType() != typeof(InMemoryWorkflowExecutionStateStore) || taskOptions?.Value.AllowVolatileTasksForDevelopment != true))
            throw Refuse("A durable task journal is required. Volatile tasks require explicit development opt-in with an in-memory Runtime.");
        var rawActivities = activities.GetType() == typeof(CoalescingActivityExecutionStateStore) ? physicalActivities?.Value : activities;
        if (store.IsDurable && (rawExecutions is null || taskBoundary?.CanRetainCompletionEvidence(store, rawExecutions) != true))
            throw Refuse("The task journal and execution provider have no qualified completion-retention boundary.");
        if (rawExecutions is null || rawActivities is null || !checkpoints.SupportsExactBookmarkResume || !checkpoints.CanCommitExactBookmarkResume(bookmarks, rawExecutions, rawActivities))
            throw Refuse("User tasks require a provider-qualified exact bookmark resume boundary.");
    }
    private static UserTaskException Refuse(string message) => new("unavailable", message);
}
