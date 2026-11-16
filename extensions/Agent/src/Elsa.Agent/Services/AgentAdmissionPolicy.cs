namespace Elsa.Agent.Services;

// Captured host policy; never part of a protected request fingerprint or persisted turn.
internal sealed record AgentAdmissionPolicy(bool AllowNewOperations);
