using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Module-prepared exclusive stimulus claim. Metadata is a projection; the selected physical provider owns authority.</summary>
public sealed record RuntimeIngressReservationDescriptor(string StimulusType, string StimulusHash,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Actual protected reservation, shared by publication slots and independently managed trigger resources.</summary>
public sealed record RuntimeIngressReservation(string ReservationId, TriggerScope Scope, string StimulusType,
    string StimulusHash, string OwnerKind, string OwnerId, long OwnerRevision, long ConfigurationRevision,
    IReadOnlyDictionary<string, string> Metadata)
{
    public const string PublicationSlotOwner = "publication-slot";
    public const string StandaloneTriggerOwner = "standalone-trigger";

    /// <summary>The actual ingress namespace is a persistence scope. Tenant is retained on its owner and never inferred from scope.</summary>
    public static string CreateIdentity(PersistenceScope scope, string stimulusType, string stimulusHash) =>
        "ingress-reservation:v1:" + RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-reservation.v1",
            new { scope = scope.Value, stimulusType, stimulusHash });
}

/// <summary>Physical resource/source-projection replacement. The selected provider commits the whole plan with its mutation receipt.</summary>
public interface ITriggerSourceResourceCommitStore
{
    bool SupportsActivatedResources => false;
    ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write,
        IReadOnlyList<RuntimeIngressReservationDescriptor> reservations, CancellationToken cancellationToken = default);
}

public sealed record RuntimeIngressReservationQuery(string StimulusType, int Limit = 100, string? After = null, string? StimulusHash = null);
public sealed record RuntimeIngressReservationPage(IReadOnlyList<RuntimeIngressReservation> Items, string? Next);

/// <summary>Replacement contract: reads actual scoped reservation authority. Unsupported composition never supplies an empty successful lookup.</summary>
/// <remarks>Provider infrastructure failures are reported as <see cref="TriggerOperationException"/> with the original cause and scoped identifiers.</remarks>
public interface IRuntimeIngressReservationReader
{
    bool SupportsReservations => false;
    ValueTask<RuntimeIngressReservation?> FindAsync(TriggerScope scope, string stimulusType, string stimulusHash,
        CancellationToken cancellationToken = default);
    ValueTask<RuntimeIngressReservationPage> ListAsync(TriggerScope scope, RuntimeIngressReservationQuery query,
        CancellationToken cancellationToken = default);
    /// <summary>Trusted server route projection within one physical shell namespace, retaining exact tenant facts.</summary>
    ValueTask<RuntimeIngressReservationPage> ListInScopeAsync(Elsa.Workflows.Runtime.Core.Models.PersistenceScope scope,
        RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeIngressReservationPage>(new NotSupportedException("Scope-wide ingress route projection is unsupported."));
}

/// <summary>Scoped module contribution preparing source-owned claims for a protected management commit; no store mutation is permitted.</summary>
public interface ITriggerReservationPlanner
{
    string TypeKey { get; }
    ValueTask<IReadOnlyList<RuntimeIngressReservationDescriptor>> PlanAsync(TriggerScope scope, TriggerAggregate? current,
        TriggerStoreWrite proposed, CancellationToken cancellationToken = default);
}
