using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed record KernelOperatorIngress(
    Func<Func<BoundScope, ActorRef, string, bool>, CancellationToken, Task<IIngress>> Open);

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
        if (httpContext.HttpContext is not { } context || !principals.TryGetNormalizedPrincipal(context.User, out var principal))
            throw new RuntimeResourceMissingSignal();

        var claims = principal.FindAll(IdentityClaimTypes.TenantId).Take(2).ToArray();
        var tenant = claims switch { [] => null, [var single] when !string.IsNullOrWhiteSpace(single.Value) => single.Value, _ => "" };
        if (tenant == "") throw new RuntimeResourceMissingSignal();

        using var deadline = new CancellationTokenSource(DeletionBudget, clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            return await DeleteCoreAsync(workflowExecutionId, principal, tenant, budget.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status409Conflict,
                "The deletion did not settle in time. Refresh its status and retry deletion.");
        }
    }

    private async Task<WorkflowInstanceDeletionView> DeleteCoreAsync(string workflowExecutionId,
        ClaimsPrincipal principal, string? tenant, CancellationToken cancellationToken)
    {
        var binding = foundation.Tenant;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var run = await runs.GetAsync(workflowExecutionId, cancellationToken) ?? throw new RuntimeResourceMissingSignal();
            if (run.View is not { } view)
                return await services.GetRequiredService<WorkflowInstanceDeletionService>().DeleteAsync(workflowExecutionId, cancellationToken);

            if (!StringComparer.Ordinal.Equals(tenant ?? PersistenceScope.DefaultValue, binding.Tenant))
                throw new RuntimeResourceMissingSignal();
            var permission = new PermissionEvaluationContext(principal, WorkflowRuntimePermissions.WorkflowRuntimeManage, tenant, run.State);
            if (!(await authorization.AuthorizeAsync(permission, cancellationToken)).Succeeded)
                throw new RuntimeResourceMissingSignal();
            if (view.Selection is not { } selection) throw Changed();

            var actor = view.Header.Ref;
            var kind = view.Header.Status == ActorStatus.Running ? Kinds.Terminate : Kinds.Delete;
            var command = new OperatorCommand(selection, ThenDelete: kind == Kinds.Terminate, ExpectedTarget: actor);
            var ingress = await operators.Open((scope, target, operation) =>
                scope.Tenant == binding.Tenant && target == actor &&
                (operation == kind || command.ThenDelete && operation == Kinds.Delete), cancellationToken);
            var admission = await ingress.SendAsync(binding, new Target.Ref(actor), Guid.NewGuid().ToString("N"), kind,
                JsonSerializer.Serialize(command), cancellationToken: cancellationToken);
            if (admission.Outcome is AdmissionOutcome.Accepted or AdmissionOutcome.Duplicate)
            {
                while (!(await foundation.GetAsync(actor, cancellationToken)).Missing)
                    await Task.Delay(ReadDelay, clock, cancellationToken);
                return new WorkflowInstanceDeletionView(workflowExecutionId);
            }

            if (admission.Reason is RefusalReason.RateLimited or RefusalReason.NodeBusy or RefusalReason.InboxFull)
                throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status409Conflict,
                    "The run could not be stopped yet. Refresh its status and retry deletion.");
        }

        throw Changed();
    }

    private static RuntimeRunDeletionRejectedSignal Changed() => new(StatusCodes.Status409Conflict,
        "The run changed before deletion. Refresh its status and retry deletion.");
}
