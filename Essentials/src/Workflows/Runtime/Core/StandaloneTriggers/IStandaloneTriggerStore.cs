using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Trusted server scope. Tenant identity is exact and is never inferred from an absent persistence partition.</summary>
public sealed record TriggerScope(PersistenceScope Persistence, string? TenantId);

/// <summary>Provider read projection of one resource and its canonical associations at one resource revision.</summary>
public sealed record TriggerAggregate(StandaloneTrigger Trigger, IReadOnlyList<TriggerSubscription> Subscriptions);

public sealed record TriggerResourceQuery(int Limit = 25, string? After = null, string? Search = null,
    string? TypeKey = null, TriggerDesiredState? DesiredState = null);
public sealed record TriggerResourcePage(IReadOnlyList<StandaloneTrigger> Items, string? Next);
public sealed record TriggerSubscriptionQuery(string? TriggerId = null, string? DefinitionId = null,
    string? SlotName = null, int Limit = 25, string? After = null);
public sealed record TriggerSubscriptionPage(IReadOnlyList<TriggerSubscription> Items, string? Next);

/// <summary>A trusted prepared aggregate replacement. The application checks authorization/impact; the provider compares and commits it atomically.</summary>
public sealed record TriggerStoreWrite(string RequestId, string RequestFingerprint, string ActorId,
    long? ExpectedRevision, StandaloneTrigger Trigger, IReadOnlyList<TriggerSubscription> Subscriptions)
{
    /// <summary>Server-observed target facts, including absence. These are compared by the protected source provider, never read from request JSON.</summary>
    public IReadOnlyList<TriggerTargetObservation>? ObservedTargets { get; init; }
}

/// <summary>The publication and declared input contract reviewed for one logical target.</summary>
public sealed record TriggerTargetObservation(string DefinitionId, string SlotName, long? SlotRevision,
    string? PublicationId, string? ContractFingerprint, string? ModelKey, TriggerTargetState State);

/// <summary>Commit confirmation, retained with the mutation key for identical retry reconciliation.</summary>
public sealed record TriggerMutationReceipt(string RequestId, string RequestFingerprint, string ActorId,
    string TriggerId, long Revision, DateTimeOffset CommittedAt);

/// <summary>Replacement contract: one scoped resource provider, selected coherently with the runtime admission backend.</summary>
/// <remarks>Provider infrastructure failures are reported as <see cref="TriggerOperationException"/> with the original cause and scoped identifiers.</remarks>
public interface IStandaloneTriggerStore
{
    ValueTask<TriggerAggregate?> FindAsync(TriggerScope scope, string triggerId, CancellationToken cancellationToken = default);
    ValueTask<TriggerSubscription?> FindSubscriptionAsync(TriggerScope scope, string subscriptionId, CancellationToken cancellationToken = default);
    ValueTask<TriggerResourcePage> ListAsync(TriggerScope scope, TriggerResourceQuery query, CancellationToken cancellationToken = default);
    ValueTask<TriggerSubscriptionPage> ListSubscriptionsAsync(TriggerScope scope, TriggerSubscriptionQuery query,
        CancellationToken cancellationToken = default);
    ValueTask<TriggerMutationReceipt?> FindMutationAsync(TriggerScope scope, string actorId, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a commit confirmation; identical request replay precedes revision comparison and conflicting content refuses.</summary>
    ValueTask<TriggerMutationReceipt> WriteAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken cancellationToken = default);
}

/// <summary>Safe domain refusal with optional field findings; values are never embedded in the failure.</summary>
public sealed class TriggerOperationException(string code, string message,
    IReadOnlyDictionary<string, string>? fields = null, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string>? Fields { get; } = fields;
}
