using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Replaces a child-routing value on parent-completion delivery while preserving the committed child state.</summary>
internal static class ParentCompletionCommandMetadataPoisoning
{
    public const string MetadataKey = "test.child-provenance";
    public const string CommittedValue = "from-committed-child";
    public const string PoisonedValue = "from-delivery-command";

    public static void Install(IServiceCollection services)
    {
        var descriptor = services.Single(candidate =>
            candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            candidate.ImplementationType == typeof(WorkflowParentActivityCompletionSchedulerWorkHandler));
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowSchedulerWorkHandler),
            serviceProvider => new PoisoningHandler(
                ActivatorUtilities.CreateInstance<WorkflowParentActivityCompletionSchedulerWorkHandler>(serviceProvider)),
            descriptor.Lifetime));
    }

    private sealed class PoisoningHandler(IWorkflowSchedulerWorkHandler inner) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            var metadata = workItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            metadata[MetadataKey] = PoisonedValue;
            var poisonedWorkItem = new RuntimeSchedulerWorkItem(
                workItemId: workItem.WorkItemId,
                workflowExecutionId: workItem.WorkflowExecutionId,
                commandId: workItem.CommandId,
                commandKind: workItem.CommandKind,
                envelopeId: workItem.EnvelopeId,
                idempotencyKey: workItem.IdempotencyKey,
                enqueuedAt: workItem.EnqueuedAt,
                recordedAt: workItem.RecordedAt,
                sequence: workItem.Sequence,
                payload: workItem.Payload,
                commandMetadata: metadata,
                envelopeMetadata: workItem.EnvelopeMetadata,
                executionScopeId: workItem.ExecutionScopeId,
                attempt: workItem.Attempt);
            return inner.HandleAsync(poisonedWorkItem, cancellationToken);
        }
    }
}
