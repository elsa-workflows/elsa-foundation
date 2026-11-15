using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Detached execution-model metadata for target discovery; it does not resolve a runtime interpreter.</summary>
public sealed record TriggerTargetModelDescriptor(string ModelKey, string? ProtocolKey, int? ProtocolVersion,
    IReadOnlyList<string> Profiles, bool SupportsWorkflowOwnedHttpResponse, bool AcceptsNewStarts = true);

/// <summary>Metadata fan-in through TriggerTargetModelsDiscovering; no runtime interpreter is resolved.</summary>
public interface ITriggerTargetModelSource
{
    IEnumerable<TriggerTargetModelDescriptor> GetModels();
}

/// <summary>Runtime delivery adapter, composed independently of authoring target discovery.</summary>
public interface IStandaloneTriggerRuntimeTarget
{
    string ModelKey { get; }
    bool Supports(WorkflowExecutable? nativeWorkflow, RuntimeModelMaterial? model);
    ValueTask<RuntimeDispatchSeal> PrepareDispatchAsync(RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, CancellationToken cancellationToken = default);
    ValueTask<RuntimeTargetAcceptanceReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, RuntimeDispatchSeal seal, CancellationToken cancellationToken = default);
    ValueTask DispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        RuntimeDispatchSeal seal, CancellationToken cancellationToken = default);
}

/// <summary>Sequential target metadata fan-in; one aggregator owns contribution collection.</summary>
public sealed class TriggerTargetModelsDiscovering : IEvent
{
    public ICollection<TriggerTargetModelDescriptor> Models { get; } = [];
}
