using System.Text.Json;
using Elsa.Primitives.Models;

namespace Elsa.Agent.Core.Models;

/// <summary>Agent-owned member contract; activity ports and model schemas are derived later.</summary>
public sealed record AgentSourceMember(string Key, string Name, TypeReference Type, bool Required = false, bool AllowNull = false, JsonElement? Default = null, JsonElement? Constraints = null);

/// <summary>Typed authored source, independent of activity Design DTOs and runtime SDK objects.</summary>
public sealed record AgentSourceContent(string Instructions, AgentModelReference Model,
    IReadOnlyList<AgentSourceMember> Inputs, IReadOnlyList<AgentSourceMember> Results,
    JsonElement Settings, AgentTurnLimits Limits, bool AllowContinuity = false);

public sealed record AgentSourceDraft(string AgentId, string DraftId, string Name, long Revision, AgentSourceContent Content);
public sealed record AgentSourceRevision(string Id, string OperationId, long Revision, string Fingerprint, AgentSourceContent Content)
{
    public string SourceCopyId { get; init; } = "";
}
public sealed record AgentRevisionPage(int Total, IReadOnlyList<AgentSourceRevision> Items);
public sealed record AgentSourcePage(int Total, IReadOnlyList<AgentSourceDraft> Items);

/// <summary>Source identity with its active draft or a retained history reference; no authored content is projected.</summary>
public sealed record AgentDefinitionView(string AgentId, string Name, string SourceCopyId, string? DraftId, long? Revision);
public sealed record AgentDefinitionPage(int Total, IReadOnlyList<AgentDefinitionView> Items);
