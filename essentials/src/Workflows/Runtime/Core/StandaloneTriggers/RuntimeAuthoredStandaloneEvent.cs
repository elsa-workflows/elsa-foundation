using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Exact authored Event read by an installed host from its committed Actor Foundation source.</summary>
public sealed record RuntimeAuthoredStandaloneEvent(TriggerScope Scope, WorkflowExecutionPartition Partition,
    string Occurrence, string PublisherActor, string EventName, JsonElement Payload, DateTimeOffset CommittedAt,
    WorkflowExecutionAuthoritySnapshot Authority);

/// <summary>Host-only ingress for committed actor Events. Completion means Runtime durably accepted the exact standalone cohort.</summary>
public interface IRuntimeAuthoredStandaloneEventIngress
{
    ValueTask AdmitAsync(RuntimeAuthoredStandaloneEvent source, CancellationToken cancellationToken = default);
}
