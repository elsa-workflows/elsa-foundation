using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Etl.Workflows.Runtime.Services;

/// <summary>Projects only a native invocation's ETL run reference after Runtime authorizes its activity context.</summary>
internal sealed class RunEtlStateViewProjector(IEtlRuntimeStore? runs = null) : IActivityStateViewProjector
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string StateTypeAlias => typeof(RunEtlState).FullName!;
    public string? ActivityTypeWithoutState => RunEtl.ActivityType;
    public string ViewKind => "etl.run-reference";
    public int ViewVersion => 1;

    public ValueTask<JsonElement> ProjectAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var request = EtlNativeBoundary.ReadState(context.ActivityExecution);
        var workflow = context.WorkflowExecution;
        if (workflow.WorkflowExecutionId != request.WorkflowExecutionId || workflow.TenantId != request.Scope.TenantId ||
            workflow.Partition?.Value != request.Scope.PersistencePartition ||
            workflow.PinnedExecutable.ArtifactId != request.ExecutableId || workflow.PinnedExecutable.ArtifactHash != request.ExecutableHash ||
            context.ExecutableNode.ExecutableNodeId != request.NodeId || context.ExecutableNode.ActivityType != RunEtl.ActivityType)
            throw EtlNativeBoundary.Invalid();

        // The native boundary resolves the real registration from either the live wait or its
        // consumed typed receipt, including when the completion payload has already retired.
        var invocation = EtlNativeBoundary.Invocation(context.ActivityExecution);
        return ValueTask.FromResult(Reference(invocation));
    }

    public async ValueTask<JsonElement?> ProjectWithoutStateAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var activity = context.ActivityExecution;
        var workflow = context.WorkflowExecution;
        if (runs is null || activity.PrivateState is not null || workflow.Partition is null ||
            activity.Execution.ActivityType != RunEtl.ActivityType || context.ExecutableNode.ActivityType != RunEtl.ActivityType ||
            activity.Execution.WorkflowExecutionId != workflow.WorkflowExecutionId ||
            context.ExecutableNode.ExecutableNodeId != activity.Execution.ExecutableNodeId ||
            activity.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled))
            return null;

        var scope = new EtlScope(workflow.TenantId, workflow.Partition.Value);
        var runId = EtlNativeBoundary.RunId(scope, workflow.WorkflowExecutionId, activity.InvocationId);
        var retained = await runs.FindRunAsync(scope, runId, cancellationToken);
        if (retained?.TerminalResult is not { } terminal)
            return null;
        var invocation = retained.Invocation;
        var address = invocation.ResultAddress;
        var expectedStatus = terminal.Outcome switch
        {
            EtlTerminalOutcome.Succeeded or EtlTerminalOutcome.BusinessError => ActivityExecutionStatus.Completed,
            EtlTerminalOutcome.Faulted => ActivityExecutionStatus.Faulted,
            EtlTerminalOutcome.Cancelled => ActivityExecutionStatus.Cancelled,
            _ => (ActivityExecutionStatus?)null
        };
        if (activity.Status != expectedStatus || invocation.Scope != scope || invocation.RunId != runId ||
            address.WorkflowExecutionId != workflow.WorkflowExecutionId || address.ActivityExecutionId != activity.InvocationId ||
            address.NodeId != activity.Execution.ExecutableNodeId || address.ExecutableId != workflow.PinnedExecutable.ArtifactId ||
            address.ExecutableHash != workflow.PinnedExecutable.ArtifactHash ||
            !context.ExecutableNode.Metadata.TryGetValue(EtlNativeBoundary.PinKey, out var pin))
            return null;

        var receipts = activity.TriggerDeliveries?.Where(item => item.Status == ActivityTriggerDeliveryStatus.Consumed &&
            item.ProviderId == EtlNativeResultAddress.CompletionProviderId && item.PayloadType == EtlNativeBoundary.CompletionType).ToArray();
        if (receipts is not { Length: 1 } || receipts[0].RegistrationId != address.RegistrationId)
            return null;
        try
        {
            EtlNativeBoundary.EnsureInvocation(invocation);
            var selection = new EtlPublishedSelection(invocation.Artifact.DefinitionId, invocation.Artifact.DefinitionVersion, invocation.Artifact.ContentHash).ToToken();
            var pinned = EtlNativeBoundary.ReadPin(pin, selection);
            var fingerprint = EtlRuntimeIdentity.InvocationFingerprint(invocation);
            if (EtlRuntimeIdentity.InvocationFingerprint(invocation with { Artifact = pinned }) != fingerprint)
                return null;
            if (receipts[0].Payload.InlineValue is { } json)
            {
                var completion = json.Deserialize<RunEtlCompletion>();
                if (completion is null || completion.FormatVersion != EtlNativeBoundary.FormatVersion ||
                    EtlRuntimeIdentity.InvocationFingerprint(EtlRuntimeJson.Read<EtlInvocation>(completion.Invocation, EtlNativeBoundary.MaximumRequestBytes)) != fingerprint ||
                    EtlRuntimeJson.Read<EtlTerminalResult>(completion.Result, EtlNativeBoundary.MaximumResultBytes) != terminal)
                    return null;
            }
            return Reference(invocation);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static JsonElement Reference(EtlInvocation invocation)
    {
        var address = invocation.ResultAddress;
        return JsonSerializer.SerializeToElement(new
        {
            runId = invocation.RunId,
            resultAddress = new
            {
                workflowExecutionId = address.WorkflowExecutionId,
                activityExecutionId = address.ActivityExecutionId,
                nodeId = address.NodeId,
                registrationId = address.RegistrationId,
                resumeTargetId = address.ResumeTargetId
            }
        }, Web);
    }
}
