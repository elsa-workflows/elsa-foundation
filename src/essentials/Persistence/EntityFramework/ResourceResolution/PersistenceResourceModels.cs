namespace Elsa.Persistence.EntityFramework.ResourceResolution;

// Detached, non-secret inputs for one resolution. Adapters retain the authored document and connection values.
internal interface IPersistenceResourceResolver
{
    PersistenceResolutionResult Resolve(PersistenceResolutionInput input);
}

internal sealed record PersistenceResolutionInput(
    PersistenceResourceCatalog RootCatalog,
    PersistenceShellSelection ShellSelection,
    IReadOnlyList<EnrolledPersistenceParticipant> Participants,
    IReadOnlyDictionary<string, PersistenceLegacyTargetPresence> LegacyTargets,
    PersistenceConfigurationContext SourceContext);

internal sealed record PersistenceResourceCatalog(
    IReadOnlyDictionary<string, PersistenceResourceDefinition> Resources,
    PersistenceAuthoredValue RootDefault);

internal sealed record PersistenceShellSelection(
    string ShellName,
    PersistenceAuthoredValue ShellDefault,
    IReadOnlyDictionary<string, PersistenceAuthoredValue> FeatureBindings);

internal sealed record PersistenceResourceDefinition(
    string Name,
    PersistenceAuthoredValue Provider,
    PersistenceAuthoredValue ConnectionName,
    PersistenceSourceProvenance Source);

internal sealed record PersistenceAuthoredValue(
    PersistencePresence Presence,
    string? Value,
    PersistenceSourceProvenance Source);

internal enum PersistencePresence
{
    Absent,
    Value,
    Blank,
    Null,
    WrongType
}

internal sealed record PersistenceSourceProvenance(
    string Scope,
    string Mode,
    bool IsExternal,
    bool IsAuthored);

internal enum PersistenceContextMode
{
    Runtime,
    Management,
    ToolingFile,
    ToolingFileEnvironment
}

// Legacy ConnectionString is presence-only; no connection value enters the resolver or its evidence.
internal sealed record PersistenceLegacyFieldPresence(
    PersistencePresence Presence,
    PersistenceSourceProvenance Source);

internal sealed record PersistenceLegacyTargetPresence(
    string FeatureId,
    bool IsEnabled,
    bool IsReset,
    PersistenceLegacyFieldPresence Provider,
    PersistenceLegacyFieldPresence ConnectionName,
    PersistenceLegacyFieldPresence ConnectionString);

internal sealed record PersistenceConfigurationContext(
    PersistenceContextMode Mode,
    string ShellName,
    string? EnvironmentName,
    IReadOnlyList<PersistenceSourceProvenance> CheckedSources,
    bool IncludesExternalEnvironment,
    bool IsFrozenSnapshot);

internal sealed record EnrolledPersistenceParticipant(
    string FeatureId,
    IReadOnlyList<string> ModuleNames,
    string ContextIdentity,
    bool DeclaresProvider,
    bool HasOpaqueConfigurator);

internal sealed record PersistenceResolutionResult(
    IReadOnlyList<PersistenceParticipantResolution> Participants,
    IReadOnlyList<PersistenceResolutionRefusal> Refusals,
    PersistenceSourceEvidence Evidence)
{
    public bool IsRefused => Refusals.Count != 0;
}

internal sealed record PersistenceParticipantResolution(
    EnrolledPersistenceParticipant Participant,
    PersistenceSelectionKind Selection,
    string? ResourceName,
    string? Provider,
    string? ConnectionName,
    PersistenceSourceProvenance Source,
    IReadOnlyList<string> UnresolvedPrerequisites);

internal enum PersistenceSelectionKind
{
    Legacy,
    ShellBinding,
    ShellDefault,
    RootDefault
}

internal sealed record PersistenceResolutionRefusal(
    string Code,
    string? FeatureId,
    string? ResourceName,
    string? FieldName);

internal sealed record PersistenceSourceEvidence(
    PersistenceConfigurationContext Context,
    IReadOnlyList<PersistenceSourceProvenance> Sources,
    IReadOnlyList<string> UnverifiedPrerequisites);
