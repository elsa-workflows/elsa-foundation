using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>Authorizes a run, stops it through its mailbox, then requests a provider-fenced history deletion.</summary>
public sealed class WorkflowInstanceDeletionService(
    IServiceProvider services,
    IHttpContextAccessor httpContextAccessor,
    IPermissionAuthorizationService authorization,
    NormalizedPrincipalValidator principalValidator,
    IPersistenceAccessContextAccessor persistenceAccess,
    IWorkflowExecutionActorProvider actors,
    IRuntimeExecutionIdGenerator runtimeIds,
    TimeProvider timeProvider)
{
    public async Task<WorkflowInstanceDeletionView> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var store = services.GetService<IWorkflowExecutionDeletionStore>();
        if (store is not { IsSupported: true })
            throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status503ServiceUnavailable,
                "Run deletion is unavailable for this runtime's persistence configuration.");

        var rawPrincipal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        if (!principalValidator.TryGetNormalizedPrincipal(rawPrincipal, out var principal))
            throw new RuntimeResourceMissingSignal();
        var tenantId = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? principal.FindFirst("tenant_id")?.Value;
        var candidate = await ReadAuthorizedAsync();
        if (!candidate.State.Status.IsTerminal())
        {
            await RequestCancellationAsync(candidate.State, cancellationToken);
            // Mailbox acceptance does not prove cancellation committed. Keep the run retained if it takes longer
            // than this bounded request; a later retry resumes from the newly authorized snapshot.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                candidate = await ReadAuthorizedAsync();
                if (candidate.State.Status.IsTerminal())
                    break;
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken);
            }
            if (!candidate.State.Status.IsTerminal())
                throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status409Conflict,
                    "The run is still stopping. Wait for it to stop, then retry deletion.");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = await store.TryDeleteTerminalAsync(candidate, cancellationToken);
            switch (result)
            {
                case WorkflowExecutionDeletionResult.Deleted:
                    return new WorkflowInstanceDeletionView(workflowExecutionId);
                case WorkflowExecutionDeletionResult.NotFound:
                    throw new RuntimeResourceMissingSignal();
                case WorkflowExecutionDeletionResult.Unsupported:
                    throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status503ServiceUnavailable,
                        "Run deletion is unavailable for this runtime's persistence configuration.");
                case WorkflowExecutionDeletionResult.DependencyRetained:
                    throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status409Conflict,
                        "Linked control delivery still needs this run's history. Finish delivery and delete the caller before its called run.");
                case WorkflowExecutionDeletionResult.NotTerminal:
                    throw Changed();
                case WorkflowExecutionDeletionResult.Changed:
                    candidate = await ReadAuthorizedAsync();
                    if (!candidate.State.Status.IsTerminal())
                        throw Changed();
                    break;
                default:
                    throw new InvalidOperationException("Unknown run deletion result.");
            }
        }
        throw Changed();

        async Task<WorkflowExecutionDeletionCandidate> ReadAuthorizedAsync()
        {
            var current = await store.ReadAsync(workflowExecutionId, cancellationToken);
            if (current is null ||
                (current.State.TenantId is not null && !StringComparer.Ordinal.Equals(current.State.TenantId, tenantId)) ||
                (persistenceAccess.Current.Scope is { } scope && current.State.TenantId is not null &&
                 !StringComparer.Ordinal.Equals(scope.Value, current.State.TenantId)))
                throw new RuntimeResourceMissingSignal();
            var partition = persistenceAccess.Current.Scope?.Value ?? WorkflowExecutionPartition.DefaultValue;
            if ((current.State.Partition is { } recordedPartition && !StringComparer.Ordinal.Equals(recordedPartition.Value, partition)) ||
                (tenantId is not null && current.State.TenantId is null && current.State.Partition is null))
                throw new RuntimeResourceMissingSignal();
            var decision = await authorization.AuthorizeAsync(
                new PermissionEvaluationContext(principal, WorkflowRuntimePermissions.WorkflowRuntimeManage, tenantId, current.State),
                cancellationToken);
            if (!decision.Succeeded)
                throw new RuntimeResourceMissingSignal();
            return current;
        }
    }

    private async Task RequestCancellationAsync(WorkflowExecutionState execution, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.cancellationSource"] = "workflow-run-deletion"
        };
        var partition = new WorkflowExecutionPartition(persistenceAccess.Current.Scope?.Value ?? WorkflowExecutionPartition.DefaultValue);
        var command = new WorkflowExecutionCommand(runtimeIds.NewWorkflowExecutionCommandId(), execution.WorkflowExecutionId,
            WorkflowExecutionCommandKind.Cancel, now, Payload: null, Metadata: metadata);
        var envelope = new WorkflowExecutionCommandEnvelope(runtimeIds.NewWorkflowExecutionCommandEnvelopeId(),
            execution.WorkflowExecutionId, command, $"workflow-run-deletion:{command.CommandId}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, metadata: metadata, partition: partition);
        var actor = await actors.GetAgentAsync(new(execution.WorkflowExecutionId,
            WorkflowExecutionActorActivationReason.ControlPlaneCommand, now, "workflow-run-deletion",
            WorkflowExecutionActorCapabilities.None, metadata, partition), cancellationToken);
        var result = await actor.EnqueueAsync(envelope, cancellationToken);
        if (result.IsShed || result.Status is WorkflowExecutionCommandDispatchStatus.Rejected)
            throw new RuntimeRunDeletionRejectedSignal(StatusCodes.Status409Conflict,
                "The run could not be stopped yet. Refresh its status and retry deletion.");
    }

    private static RuntimeRunDeletionRejectedSignal Changed() => new(StatusCodes.Status409Conflict,
        "The run changed while deletion was being prepared. Refresh its status and retry deletion.");
}
