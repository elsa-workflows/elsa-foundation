namespace Elsa.Agent.Specialists.Services;

// Captured host policy; never part of a protected request fingerprint or persisted turn.
internal sealed record SpecialistAdmissionPolicy(bool AllowNewOperations);
