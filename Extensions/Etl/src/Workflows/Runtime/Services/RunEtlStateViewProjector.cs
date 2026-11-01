using System.Text.Json;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Etl.Workflows.Runtime.Services;

/// <summary>Projects only a native invocation's ETL run reference after Runtime authorizes its activity context.</summary>
internal sealed class RunEtlStateViewProjector : IActivityStateViewProjector
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string StateTypeAlias => typeof(RunEtlState).FullName!;
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
        var address = invocation.ResultAddress;
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new
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
        }, Web));
    }
}
