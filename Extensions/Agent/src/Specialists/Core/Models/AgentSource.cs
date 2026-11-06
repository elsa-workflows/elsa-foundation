using System.Text.Json;
using Elsa.Primitives.Models;

namespace Elsa.Agent.Specialists.Core.Models;

/// <summary>Agent-owned member contract; activity ports and model schemas are derived later.</summary>
public sealed record AgentSourceMember(string Key, string Name, TypeReference Type, bool Required = false, bool AllowNull = false, JsonElement? Default = null, JsonElement? Constraints = null);

/// <summary>Typed authored source, independent of activity Design DTOs and runtime SDK objects.</summary>
public sealed record AgentSourceContent(string Instructions, SpecialistModelReference Model,
    IReadOnlyList<AgentSourceMember> Inputs, IReadOnlyList<AgentSourceMember> Results,
    JsonElement Settings, SpecialistTurnLimits Limits, bool AllowContinuity = false);

public sealed record AgentSourceDraft(string AgentId, string DraftId, string Name, long Revision, AgentSourceContent Content);
public sealed record AgentSourceRevision(string Id, string OperationId, long Revision, string Fingerprint, AgentSourceContent Content);
public sealed record AgentSourcePage(int Total, IReadOnlyList<AgentSourceDraft> Items);
