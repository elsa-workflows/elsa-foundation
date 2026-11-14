namespace Elsa.Weaver.Core.Models;

/// <summary>Completeness of a bounded installed-support observation; incomplete/unavailable cannot establish absence.</summary>
public enum WeaverSupportCompleteness
{
    Unavailable,
    Incomplete,
    Complete
}

/// <summary>An observation only. AvailableHint is not target authorization; Unchecked means no such check was made.</summary>
public enum WeaverSupportStatus
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
public sealed record WeaverSupportView(
    WeaverSupportCompleteness Completeness,
    IReadOnlyList<WeaverToolSupportView> Tools,
    IReadOnlyList<WeaverContextSupportView> Contexts);

/// <summary>Installed tool identity; admission includes the supplied policy and descriptor-known host limits only.</summary>
/// <remarks>Prepared risk/classes, current context scopes and concrete targets require normal preparation and owner authorization.</remarks>
public sealed record WeaverToolSupportView(
    string Name,
    string ModuleId,
    string ContractVersion,
    WeaverExecutionLocation ExecutionLocation,
    WeaverSupportStatus Admission,
    WeaverSupportStatus Authority);

/// <summary>Installed source metadata; admission observes selected-feature support only, not disclosure policy or authority.</summary>
public sealed record WeaverContextSupportView(
    string Id,
    string ModuleId,
    string ScopeKind,
    IReadOnlyList<string> ContextKinds,
    IReadOnlyList<string> SchemaVersions,
    IReadOnlyList<string> ResourceTypes,
    WeaverSupportStatus Admission,
    WeaverSupportStatus Authority);
