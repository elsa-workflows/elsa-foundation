namespace Elsa.Agent.Core.Models;

/// <summary>One physical-send reservation. Missing usage never releases this conservative reservation.</summary>
public sealed record AgentModelRequestReservation(string Id, int MaximumOutputTokens, DateTimeOffset ReservedAt,
    AgentModelDispatchObservation Observation);

/// <summary>Configured resource observations at dispatch; these are not proof of a backing model's identity.</summary>
public sealed record AgentModelDispatchObservation(string ProviderKey, string ConfiguredModelId,
    string ConnectionIncarnation, int ConnectionVersion, string CapabilitiesFingerprint, string ProtocolProfile);

/// <summary>
/// Observable response evidence, distinct from configured capabilities. Missing usage/counts remain
/// unknown; the reported model ID is the provider's observation, not a guarantee of backing identity.
/// </summary>
public sealed record AgentModelResponseObservation(DateTimeOffset ObservedAt, bool UsageAvailable,
    long? InputTokens, long? OutputTokens, long? TotalTokens, string? ObservedModelId);
