using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.UserTasks;
using Kernel.Foundation.Contracts;
using Kernel.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelUserTaskRuntime(KernelShellStorage storage, IServiceScopeFactory scopes, PersistenceScope persistence,
    BoundScope tenant, TimeProvider clock, ILogger<KernelUserTaskRuntime> logger) : IActorUserTaskRuntime, INativeUserTaskJobs
{
    private const int PageSize = 25;
    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(2);
    private IIngress? _ingress;

    internal async Task BindAsync(IIngress ingress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        _ = Journal(scope.ServiceProvider);
        _ = scope.ServiceProvider.GetRequiredService<IUserTaskAccess>();
        Volatile.Write(ref _ingress, ingress);
    }

    public bool Owns(UserTaskRecord task) => task.TenantId == tenant.Tenant &&
        task.ActorRecipient is { } recipient && recipient.ActorId == task.WorkflowExecutionId &&
        recipient.FrameId == task.InvocationId && !string.IsNullOrWhiteSpace(recipient.WaitId) &&
        task.OperationKey == task.Id && task.Recipient is null;

    public async ValueTask<bool> OpenAsync(string tenantId, NativeUserTaskRecipient recipient, string nodeId,
        NativeUserTaskRequest request, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        if (!await HasWaitAsync(recipient, cancellationToken, nodeId: nodeId)) return false;

        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var services = scope.ServiceProvider;
        var journal = Journal(services);
        if (await journal.FindAsync(tenantId, recipient.TaskId, cancellationToken) is { } prior)
            return Matches(prior, recipient) && prior.ExecutableNodeId == nodeId;

        var creation = new UserTaskCreation(recipient.Actor.Id, recipient.Frame, nodeId, recipient.TaskId,
            request.Title, request.Description, UserTaskRules.NormalizeAssignment(request.AssigneeId),
            UserTaskRules.NormalizeAssignment(request.GroupId), request.Actions, request.OptionalFormActions, null,
            JsonSerializer.SerializeToElement(request.Prefill), request.FormId, request.FormVersion);
        UserTaskForm? form;
        try
        {
            form = await ResolveFormAsync(journal, services, tenantId, creation, cancellationToken);
        }
        catch (UserTaskException error) when (error.Code == "validation")
        {
            if (await journal.FindAsync(tenantId, recipient.TaskId, cancellationToken) is { } replay)
                return Matches(replay, recipient) && replay.ExecutableNodeId == nodeId;
            return false;
        }

        var now = clock.GetUtcNow();
        var task = new UserTaskRecord(recipient.TaskId, tenantId, recipient.Actor.Id, recipient.Frame, nodeId, recipient.TaskId,
            creation.Title, creation.Description, creation.AssigneeId, creation.GroupId, null, creation.Actions.ToArray(),
            creation.OptionalFormActions.ToArray(), form, creation.Prefill, UserTaskStatus.Open, 1, now, now,
            [new("created", "workflow", now, AssigneeId: creation.AssigneeId, GroupId: creation.GroupId)])
        {
            ActorRecipient = new(recipient.Actor.Id, recipient.Frame, recipient.Wait)
        };
        if (await journal.TryWriteAsync(task, null, cancellationToken)) return true;
        return await journal.FindAsync(tenantId, task.Id, cancellationToken) is { } opened && Matches(opened, recipient) &&
            opened.ExecutableNodeId == nodeId;
    }

    public async ValueTask<NativeUserTaskResponse?> SettleAsync(string tenantId, NativeUserTaskRecipient recipient, string completionId,
        CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        if (!await HasWaitAsync(recipient, cancellationToken, completionId: completionId)) return null;

        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var journal = Journal(scope.ServiceProvider);
        var task = await journal.FindAsync(tenantId, recipient.TaskId, cancellationToken);
        if (task is null || !Matches(task, recipient) || task.Status is not (UserTaskStatus.CompletionPending or UserTaskStatus.Completed) ||
            task.Decision?.Response is not { } response || response.CompletionId != completionId)
            return null;

        if (task.Status == UserTaskStatus.CompletionPending &&
            !await TransitionAsync(journal, task, UserTaskStatus.Completed, "completed", cancellationToken))
        {
            task = await journal.FindAsync(tenantId, recipient.TaskId, cancellationToken);
            if (task is not { Status: UserTaskStatus.Completed } || !Matches(task, recipient) ||
                task.Decision?.Response.CompletionId != completionId)
                return null;
        }

        return new(response.TaskId, response.CompletionId, response.Action,
            JsonObject.Create(response.Data) ?? throw new InvalidDataException("An accepted task response requires an object."));
    }

    public async ValueTask ReconcileAsync(UserTaskRecord task, CancellationToken cancellationToken = default)
    {
        if (!Owns(task) || UserTaskRules.Terminal(task)) return;
        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var journal = Journal(scope.ServiceProvider);
        var latest = await journal.FindAsync(tenant.Tenant, task.Id, cancellationToken);
        if (latest is not null && Owns(latest) && !UserTaskRules.Terminal(latest))
            await ReconcileAsync(journal, latest, cancellationToken);
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        string? after = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
                var journal = Journal(scope.ServiceProvider);
                var page = await journal.RecoverOwnedAsync(tenant.Tenant, PageSize, after, cancellationToken);
                foreach (var task in page.Items)
                    await ReconcileAsync(journal, task, cancellationToken);
                after = page.Next;
            }
            catch (Exception error) when (UserTaskAvailability.IsUnavailable(error, cancellationToken))
            {
                logger.LogWarning("Actor user task reconciliation is temporarily unavailable. The journal will retry.");
            }

            await Task.Delay(ScanDelay, clock, cancellationToken);
        }
    }

    private async ValueTask ReconcileAsync(IActorUserTaskJournal journal, UserTaskRecord task, CancellationToken cancellationToken)
    {
        if (!Owns(task) || task.ActorRecipient is not { } owner || UserTaskRules.Terminal(task)) return;
        var recipient = new NativeUserTaskRecipient(new ActorRef(owner.ActorId), owner.FrameId, owner.WaitId, task.Id);
        if (!await HasWaitAsync(recipient, cancellationToken, nodeId: task.ExecutableNodeId))
        {
            await TransitionAsync(journal, task, UserTaskStatus.Cancelled, "cancelled", cancellationToken);
            return;
        }

        if (task.Status != UserTaskStatus.CompletionPending || task.Decision is not { } decision) return;
        var ingress = Volatile.Read(ref _ingress) ?? throw new UserTaskException("unavailable", "The Actor task relay is unavailable.");
        var accepted = new NativeUserTaskAccepted(recipient, decision.Response.CompletionId);
        await ingress.SendAsync(tenant, new Target.Ref(recipient.Actor), $"user-task/{task.Id}/{accepted.CompletionId}",
            NativeKinds.UserTaskAccepted, JsonSerializer.Serialize(accepted), cancellationToken: cancellationToken);
    }

    private ValueTask<bool> HasWaitAsync(NativeUserTaskRecipient recipient, CancellationToken cancellationToken,
        string? nodeId = null, string? completionId = null) =>
        storage.HasUserTaskWaitAsync(tenant, recipient.Actor, state =>
            JsonSerializer.Deserialize<NativeState>(state) is { } native &&
            native.Waits.TryGetValue(recipient.Wait, out var wait) && wait.Kind == "user-task" &&
            wait.Frame == recipient.Frame && wait.Key == recipient.TaskId &&
            (nodeId is null || native.Frames.TryGetValue(wait.Frame, out var frame) && frame.Node == nodeId) &&
            (completionId is null || wait.AcceptedTaskCompletionId == completionId), cancellationToken);

    private bool Matches(UserTaskRecord task, NativeUserTaskRecipient recipient) =>
        Owns(task) && task.Id == recipient.TaskId && task.ActorRecipient == new UserTaskActorRecipient(recipient.Actor.Id, recipient.Frame, recipient.Wait);

    private void RequireTenant(string requestTenant)
    {
        if (requestTenant != tenant.Tenant) throw new InvalidOperationException("This Actor task does not belong to the selected ordinary tenant.");
    }

    private ValueTask<bool> TransitionAsync(IUserTaskStore journal, UserTaskRecord current, UserTaskStatus status,
        string operation, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var author = status == UserTaskStatus.Completed ? current.Decision!.ActorId : "workflow";
        var next = current with { Status = status, Revision = current.Revision + 1, UpdatedAt = now,
            History = [.. current.History, new(operation, author, now, current.Decision?.Response.Action, current.AssigneeId, current.GroupId)] };
        return journal.TryWriteAsync(next, current.Revision, cancellationToken);
    }

    private static IActorUserTaskJournal Journal(IServiceProvider services) =>
        services.GetRequiredService<IUserTaskStore>() is IActorUserTaskJournal { IsDurable: true } journal
            ? journal
            : throw new InvalidOperationException("Actor user tasks require the selected durable Actor task journal.");

    private static async ValueTask<UserTaskForm?> ResolveFormAsync(IUserTaskStore journal, IServiceProvider services, string tenantId,
        UserTaskCreation request, CancellationToken cancellationToken)
    {
        UserTaskRules.ValidateCreation(request);
        UserTaskForm? form = null;
        if (request.FormId is not null)
        {
            form = (await journal.FindFormAsync(tenantId, request.FormId, request.FormVersion!, cancellationToken))?.Form ??
                throw new UserTaskException("validation", "The exact form version is unavailable.");
            var providers = services.GetServices<IUserTaskFormProvider>().Where(owner => owner.Provider == form.Provider).ToArray();
            if (providers is not [var provider])
                throw new UserTaskException("unavailable", "The form provider is missing or ambiguous.");
            provider.ValidateDefinition(form);
            var errors = await provider.ValidateResponseAsync(form, request.Prefill, request.Prefill, false, cancellationToken);
            if (errors.Count != 0)
                throw new UserTaskException("validation", "The prefilled values do not match the form.", errors);
        }
        else if (request.Prefill.EnumerateObject().Any())
            throw new UserTaskException("validation", "Prefilled fields require a form.");

        await services.GetRequiredService<IUserTaskAccess>().ValidateAssignmentAsync(tenantId, request.AssigneeId, request.GroupId, cancellationToken);
        return form;
    }
}
