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
    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(1);
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
        var checkpoint = await storage.ReadUserTaskCheckpointAsync(tenant, recipient.Actor, cancellationToken);
        if (checkpoint is { Parked: true }) throw TemporarilyParked();
        if (checkpoint is not { Status: ActorStatus.Running } ||
            !NativeExecutionModel.InspectUserTask(checkpoint.State, recipient, nodeId).IsWaiting) return false;

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
        var checkpoint = await storage.ReadUserTaskCheckpointAsync(tenant, recipient.Actor, cancellationToken);
        if (checkpoint is { Parked: true }) throw TemporarilyParked();
        if (checkpoint is not { Status: ActorStatus.Running } ||
            NativeExecutionModel.InspectUserTask(checkpoint.State, recipient).AcceptedCompletionId != completionId)
            return null;

        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var journal = Journal(scope.ServiceProvider);
        var task = await journal.FindAsync(tenantId, recipient.TaskId, cancellationToken);
        if (task is null || !Matches(task, recipient) || task.Status is not (UserTaskStatus.CompletionPending or UserTaskStatus.Completed) ||
            task.Decision?.Response is not { } response || response.CompletionId != completionId)
            return null;

        return new(response.TaskId, response.CompletionId, response.Action,
            JsonObject.Create(response.Data) ?? throw new InvalidDataException("An accepted task response requires an object."));
    }

    public async ValueTask<bool> CanReleaseCompletionAsync(string tenantId, NativeUserTaskRecipient recipient, string nodeId,
        string completionId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var journal = Journal(scope.ServiceProvider);
        return await journal.FindAsync(tenantId, recipient.TaskId, cancellationToken) is
            { Status: UserTaskStatus.Completed, Decision.Response: { } response } task &&
            Matches(task, recipient) && task.ExecutableNodeId == nodeId && response.CompletionId == completionId;
    }

    public async ValueTask ReconcileAsync(UserTaskRecord task, CancellationToken cancellationToken = default)
    {
        if (!Owns(task) || !NeedsWork(task)) return;
        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var journal = Journal(scope.ServiceProvider);
        var latest = await journal.FindAsync(tenant.Tenant, task.Id, cancellationToken);
        if (latest is not null && Owns(latest) && NeedsWork(latest))
            await ReconcileAsync(journal, latest, cancellationToken);
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        string? afterAccepted = null;
        string? afterOpen = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            afterAccepted = await ScanAsync(accepted: true, afterAccepted, cancellationToken);
            afterOpen = await ScanAsync(accepted: false, afterOpen, cancellationToken);
            await Task.Delay(ScanDelay, clock, cancellationToken);
        }
    }

    private async Task<string?> ScanAsync(bool accepted, string? after, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
            var journal = Journal(scope.ServiceProvider);
            var page = await journal.RecoverOwnedAsync(tenant.Tenant, accepted, PageSize, after, cancellationToken);
            foreach (var task in page.Items)
            {
                try { await ReconcileAsync(journal, task, cancellationToken); }
                catch (Exception error) when (UserTaskAvailability.IsUnavailable(error, cancellationToken))
                {
                    logger.LogDebug("Actor user task {TaskId} remains queued for reconciliation.", task.Id);
                }
            }
            return page.Next;
        }
        catch (Exception error) when (UserTaskAvailability.IsUnavailable(error, cancellationToken))
        {
            logger.LogWarning("Actor user task reconciliation is temporarily unavailable. The journal will retry.");
            return after;
        }
    }

    private async ValueTask ReconcileAsync(IActorUserTaskJournal journal, UserTaskRecord task, CancellationToken cancellationToken)
    {
        if (!Owns(task) || task.ActorRecipient is not { } owner || !NeedsWork(task)) return;
        var recipient = new NativeUserTaskRecipient(new ActorRef(owner.ActorId), owner.FrameId, owner.WaitId, task.Id);
        var checkpoint = await storage.ReadUserTaskCheckpointAsync(tenant, recipient.Actor, cancellationToken);
        var proof = NativeExecutionModel.InspectUserTask(checkpoint?.State, recipient, task.ExecutableNodeId);
        if (task is { Status: UserTaskStatus.Completed, Decision: { } completed })
        {
            if (checkpoint is { Parked: true }) return;
            if (checkpoint is { Status: ActorStatus.Running } && proof.ConsumedCompletionId == completed.Response.CompletionId)
                await DeliverAsync(recipient, completed.Response.CompletionId, NativeKinds.UserTaskAcknowledged, cancellationToken);
            else
                await journal.TryWriteAsync(task with { Revision = task.Revision + 1, ActorAcknowledged = true },
                    task.Revision, cancellationToken);
            return;
        }

        if (task is { Status: UserTaskStatus.CompletionPending, Decision: { } accepted } &&
            proof.ConsumedCompletionId == accepted.Response.CompletionId)
        {
            await TransitionAsync(journal, task, UserTaskStatus.Completed, "completed", cancellationToken);
            return;
        }

        if (checkpoint is { Parked: true }) return;
        if (checkpoint is not { Status: ActorStatus.Running } || !proof.IsWaiting)
        {
            await TransitionAsync(journal, task, UserTaskStatus.Cancelled, "cancelled", cancellationToken);
            return;
        }

        if (task.Status != UserTaskStatus.CompletionPending || task.Decision is not { } decision) return;
        await DeliverAsync(recipient, decision.Response.CompletionId, NativeKinds.UserTaskAccepted, cancellationToken);
    }

    private async ValueTask DeliverAsync(NativeUserTaskRecipient recipient, string completionId, string kind, CancellationToken cancellationToken)
    {
        var ingress = Volatile.Read(ref _ingress) ?? throw new UserTaskException("unavailable", "The Actor task relay is unavailable.");
        var payload = JsonSerializer.Serialize(new NativeUserTaskAccepted(recipient, completionId));
        await ingress.SendAsync(tenant, new Target.Ref(recipient.Actor), $"{kind}/{recipient.TaskId}/{completionId}",
            kind, payload, cancellationToken: cancellationToken);
    }

    private static bool NeedsWork(UserTaskRecord task) =>
        !UserTaskRules.Terminal(task) || task is { Status: UserTaskStatus.Completed, ActorAcknowledged: false };

    private static UserTaskException TemporarilyParked() =>
        new("unavailable", "The owning Actor is parked. Its task remains pending recovery.");

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
        UserTaskRules.ValidateActorData(request.Prefill);
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
