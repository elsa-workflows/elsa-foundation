namespace Elsa.Agent.Core.Models;

/// <summary>Completeness of a bounded installed-support observation; incomplete/unavailable cannot establish absence.</summary>
public enum AgentSupportCompleteness
{
    Unavailable,
    Incomplete,
    Complete
}

/// <summary>An observation only. AvailableHint is not target authorization; Unchecked means no such check was made.</summary>
public enum AgentSupportStatus
{
    Unknown,
    AvailableHint,
    Withheld,
    Unchecked
}

/// <summary>
/// Safe registration metadata and current hints, without resource content, schemas, permissions, code declarations,
/// raw failure messages or provider connectivity. Context authority always remains unchecked in this inventory.
/// </summary>
public sealed record AgentSupportView(
    AgentSupportCompleteness Completeness,
    IReadOnlyList<AgentToolSupportView> Tools,
    IReadOnlyList<AgentContextSupportView> Contexts);

/// <summary>Installed tool identity; admission includes the supplied policy and descriptor-known host limits only.</summary>
/// <remarks>Prepared risk/classes, current context scopes and concrete targets require normal preparation and owner authorization.</remarks>
public sealed record AgentToolSupportView(
    string Name,
    string ModuleId,
    string ContractVersion,
    AgentExecutionLocation ExecutionLocation,
    AgentSupportStatus Admission,
    AgentSupportStatus Authority);

/// <summary>Installed source metadata; admission observes selected-feature support only, not disclosure policy or authority.</summary>
public sealed record AgentContextSupportView(
    string Id,
    string ModuleId,
    string ScopeKind,
    IReadOnlyList<string> ContextKinds,
    IReadOnlyList<string> SchemaVersions,
    IReadOnlyList<string> ResourceTypes,
    AgentSupportStatus Admission,
    AgentSupportStatus Authority);
