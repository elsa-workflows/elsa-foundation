using System.Text.Json;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed record KernelOperatorIngress(
    Func<Func<BoundScope, ActorRef, string, bool>, CancellationToken, Task<IIngress>> Open,
    Func<CancellationToken, ValueTask<long>> ReadClock);

internal sealed class KernelWorkflowInstanceDeletionService(
    KernelRunReader runs,
    KernelRuns foundation,
    KernelOperatorIngress operators,
    IServiceProvider services,
    IHttpContextAccessor httpContext,
    NormalizedPrincipalValidator principals,
    IPermissionAuthorizationService authorization,
    TimeProvider clock) : IWorkflowInstanceDeletionService
{
    private static readonly TimeSpan DeletionBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReadDelay = TimeSpan.FromMilliseconds(100);

    public async Task<WorkflowInstanceDeletionView> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (httpContext.HttpContext is not { } context || KernelRunOperatorCaller.Read(context.User, principals) is not { } caller)
            throw new RuntimeResourceMissingSignal();

        using var deadline = new CancellationTokenSource(DeletionBudget, clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            return await DeleteCoreAsync(workflowExecutionId, caller, budget.Token);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            (error is TimeoutException || error is OperationCanceledException && deadline.IsCancellationRequested))
        {
            throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status409Conflict,
                "The deletion did not settle in time. Refresh its status and retry deletion.");
        }
    }

    private async Task<WorkflowInstanceDeletionView> DeleteCoreAsync(string workflowExecutionId,
        KernelRunOperatorCaller caller, CancellationToken cancellationToken)
    {
        var binding = foundation.Tenant;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var run = await runs.GetAsync(workflowExecutionId, cancellationToken) ?? throw new RuntimeResourceMissingSignal();
            if (run.IsolatedReads is not null) throw new RuntimeResourceMissingSignal();
            if (run.View is not { } view)
            {
                var legacy = services.GetService<WorkflowInstanceDeletionService>() ?? throw new RuntimeResourceMissingSignal();
                return await legacy.DeleteAsync(workflowExecutionId, cancellationToken);
            }

            if (!caller.Matches(binding)) throw new RuntimeResourceMissingSignal();
            var permission = new PermissionEvaluationContext(caller.Principal, WorkflowRuntimePermissions.WorkflowRuntimeManage, caller.Tenant, run.State);
            if (!(await authorization.AuthorizeAsync(permission, cancellationToken)).Succeeded)
                throw new RuntimeResourceMissingSignal();
            if (view.Selection is not { } selection) throw Changed();

            var actor = view.Header.Ref;
            var kind = view.Header.Status == ActorStatus.Running ? Kinds.Terminate : Kinds.Delete;
            var command = new OperatorCommand(selection, ExpectedTarget: actor);
            var ingress = await operators.Open((scope, target, operation) =>
                scope.Tenant == binding.Tenant && target == actor && operation == kind, cancellationToken);
            var now = await operators.ReadClock(cancellationToken);
            var deadline = checked(now + (long)DeletionBudget.TotalMilliseconds);
            var reply = await ingress.RequestAsync(binding, new Target.Ref(actor), Guid.NewGuid().ToString("N"), kind,
                JsonSerializer.Serialize(command), null, new(null, deadline), cancellationToken);

            if (kind == Kinds.Terminate && reply is { Error: null, Item: "terminated" or "terminated and kept" })
                continue; // Refresh the selected terminal state and authorize the purge separately.
            if (kind == Kinds.Delete && reply is { Error: null, Item: "deleted" or "started" })
            {
                while (!(await foundation.GetAsync(actor, cancellationToken)).Missing)
                    await Task.Delay(ReadDelay, clock, cancellationToken);
                return new WorkflowInstanceDeletionView(workflowExecutionId);
            }
            if (reply is { Error: "stale selection" or "parked" }) continue;
            if (reply is { Error: "protected" or "protection unavailable" or "park outstanding" })
                throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status409Conflict,
                    "The run is retained or its deletion protection could not be verified.");
            throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status409Conflict,
                "The run could not be stopped or deleted yet. Refresh its status and retry deletion.");
        }

        throw Changed();
    }

    private static RuntimeRunDeletionRejectedSignal Changed() => new(StatusCodes.Status409Conflict,
        "The run changed before deletion. Refresh its status and retry deletion.");
}
