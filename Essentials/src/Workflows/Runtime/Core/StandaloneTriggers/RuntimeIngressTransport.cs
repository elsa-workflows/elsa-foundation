namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

public enum RuntimeIngressTransportState { AwaitingResponse, WriteCompleted, TimedOut, ClientDisconnected, WriteFailed, ReceiptUnknown }

/// <summary>One actual request exchange observation, separate from accepted source, target dispatch and execution outcome.</summary>
public sealed record RuntimeIngressTransportObservation(string SourceOutboxItemId, string ExchangeId,
    RuntimeIngressTransportState State, int? StatusCode, DateTimeOffset ObservedAt);

/// <summary>Replacement contract: records source-owned transport facts without a source delivery claim or target receipt.</summary>
public interface IRuntimeIngressTransportObserver
{
    bool SupportsObservations => false;
    ValueTask RecordAsync(TriggerScope scope, RuntimeIngressTransportObservation observation, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("Protected ingress transport observations are unsupported."));
}
