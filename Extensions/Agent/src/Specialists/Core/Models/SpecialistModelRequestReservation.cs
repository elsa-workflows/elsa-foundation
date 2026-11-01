namespace Elsa.Agent.Specialists.Core.Models;

/// <summary>One physical-send reservation. Missing usage never releases this conservative reservation.</summary>
public sealed record SpecialistModelRequestReservation(string Id, int MaximumOutputTokens, DateTimeOffset ReservedAt,
    SpecialistModelDispatchObservation Observation);

/// <summary>Configured resource observations at dispatch; these are not proof of a backing model's identity.</summary>
public sealed record SpecialistModelDispatchObservation(string ProviderKey, string ConfiguredModelId,
    string ConnectionIncarnation, int ConnectionVersion, string CapabilitiesFingerprint, string ProtocolProfile);
