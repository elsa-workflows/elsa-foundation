using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Exact authored Event read by an installed host from its committed Actor Foundation source.</summary>
public sealed record RuntimeAuthoredStandaloneEvent(TriggerScope Scope, WorkflowExecutionPartition Partition,
    string Occurrence, string PublisherActor, string EventName, JsonElement Payload, DateTimeOffset CommittedAt,
    WorkflowExecutionAuthoritySnapshot Authority)
{
    internal const string BroadcastFamily = "Elsa.Event.Broadcast";
    internal const string BroadcastIntent = "Elsa.Activities.Primitives.PublishStimulus";
}

/// <summary>Host-only ingress for committed actor Events. Completion means Runtime durably accepted the exact standalone cohort.</summary>
public interface IRuntimeAuthoredStandaloneEventIngress
{
    /// <summary>Whether the scoped provider can physically accept and drain the cohort.</summary>
    bool SupportsAdmission => false;

    ValueTask AdmitAsync(RuntimeAuthoredStandaloneEvent source, CancellationToken cancellationToken = default);
}

/// <summary>Proof supplied by the installed Actor Foundation Event relay for its ordinary publisher scope.</summary>
public interface IRuntimeAuthoredStandaloneEventAvailability
{
    bool Supports(PersistenceScope scope);
}
