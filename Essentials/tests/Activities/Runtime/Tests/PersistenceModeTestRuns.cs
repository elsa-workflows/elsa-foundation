using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Builds, drives and inspects the runs of the persistence-mode tests.</summary>
internal static class PersistenceModeTestRuns
{
    /// <summary>
    /// Delivers one command to the run's agent, the way an operator (Cancel) or the recovery sweep (RunSchedulerWork,
    /// through a recovery activation) reaches a run that is already started.
    /// </summary>
    public static async Task DeliverAsync(
        WorkflowExecutionHarness harness,
        WorkflowExecutionCommandKind kind,
        WorkflowExecutionActorActivationReason reason)
    {
        var agent = await harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
            new WorkflowExecutionActorActivationRequest(
                workflowExecutionId: harness.ExecutionId,
                reason: reason,
                requestedAt: WorkflowExecutionHarness.Timestamp,
                requestedBy: "persistence-mode-test",
                requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox));
        var envelopeId = $"envelope-{kind}";
        var dispatch = await agent.EnqueueAsync(new WorkflowExecutionCommandEnvelope(
            envelopeId: envelopeId,
            workflowExecutionId: harness.ExecutionId,
            command: new WorkflowExecutionCommand(
                CommandId: $"command-{kind}",
                WorkflowExecutionId: harness.ExecutionId,
                Kind: kind,
                EnqueuedAt: WorkflowExecutionHarness.Timestamp,
                Payload: null,
                Metadata: new Dictionary<string, string>()),
            idempotencyKey: $"{harness.ExecutionId}:{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: WorkflowExecutionHarness.Timestamp,
            metadata: new Dictionary<string, string>()));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, dispatch.Status);
    }

    public static Task<IReadOnlyCollection<RuntimeSchedulerWorkItem>> QueuedWorkAsync(WorkflowExecutionHarness harness) =>
        harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(harness.ExecutionId).AsTask();

    public static ExecutableNode NewClrNode(string nodeId, Type activityType) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
}
