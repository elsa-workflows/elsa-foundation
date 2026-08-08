using System.Text.Json;

namespace Elsa.Contracts.Generator;

/// <summary>
/// The per-assembly consumer contract fragment (spec 149 / RFC #1191, data-model.md). One fragment per
/// contributing assembly; every contribution entry carries the owning feature id so consumers can filter
/// the merged set against their own shell composition. Deliberately absent: assigned activity version ids
/// and availability — server state is never published as contract.
/// </summary>
public sealed record ContractFragment(
    string SchemaVersion,
    string Assembly,
    IReadOnlyList<FeatureContract> Features,
    IReadOnlyList<ActivityContract> Activities,
    IReadOnlyList<StructureContract> Structures,
    ExpressionSurface? Expressions,
    IReadOnlyList<IntrinsicContract> Intrinsics)
{
    public const string CurrentSchemaVersion = "1.0.0";

    public bool HasContributions =>
        Features.Count > 0 || Activities.Count > 0 || Structures.Count > 0 ||
        Intrinsics.Count > 0 || Expressions is not null;
}

public sealed record FeatureContract(
    string Id,
    string? DisplayName,
    string? Description,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<FeatureOptionContract> Options);

/// <summary>Mirrors the projection of Elsa.Modularity's ManifestHintReader (same code produces both).</summary>
public sealed record FeatureOptionContract(
    string Name,
    string? DisplayName,
    string? Description,
    string? Category,
    string? ClrType,
    string JsonType,
    bool Required,
    JsonElement? DefaultValue,
    bool Secret,
    bool RestartRequired,
    bool Advanced,
    bool Experimental);

/// <summary>
/// Content fields of the served catalog descriptor (<c>ActivityAuthoringDescriptorView</c>) minus the
/// server-state overlay (version id, availability, provenance) and server-generated template boilerplate.
/// <see cref="ContentHash"/> equals the persisted <c>ActivityDefinitionVersion.Hash</c> for identical content.
/// </summary>
public sealed record ActivityContract(
    string? FeatureId,
    string ActivityTypeKey,
    string Version,
    string ContentHash,
    string DisplayName,
    string? Category,
    string? Description,
    string ExecutionType,
    IReadOnlyList<InputContract> Inputs,
    IReadOnlyList<OutputContract> Outputs,
    IReadOnlyList<PortContract> Ports,
    JsonElement? ContainerStructure);

public sealed record InputContract(
    string ReferenceKey,
    string Name,
    string Type,
    string CollectionKind,
    string? DisplayName,
    string? Description,
    float Order,
    string? Category,
    bool IsBrowsable,
    bool IsRequired,
    bool IsNullable,
    string? UiHint,
    JsonElement? DefaultValue,
    bool HasStaticDefault,
    string? DefaultSyntax,
    JsonElement? UiSpecifications);

public sealed record OutputContract(
    string ReferenceKey,
    string Name,
    string Type,
    string CollectionKind,
    string? DisplayName,
    string? Description,
    string? Category,
    bool IsBrowsable,
    bool IsRequired);

public sealed record PortContract(
    string Name,
    string? DisplayName,
    string? Type,
    bool IsBrowsable,
    string ReferenceKey);

public sealed record StructureContract(
    string? FeatureId,
    string Kind,
    string SchemaVersion,
    bool SupportsScopedVariables,
    JsonElement? PayloadSchema);

public sealed record ExpressionSurface(
    IReadOnlyList<ExpressionDescriptorContract> Descriptors,
    IReadOnlyList<JsDeclarationContract> JavaScriptDeclarations,
    IReadOnlyList<SandboxGlobalContract> ScriptSandbox);

public sealed record ExpressionDescriptorContract(
    string? FeatureId,
    string Type,
    string? DisplayName,
    string EditingMode);

public sealed record JsDeclarationContract(
    string? FeatureId,
    string Contributor,
    IReadOnlyList<JsonElement> Variables,
    IReadOnlyList<JsonElement> Types,
    IReadOnlyList<JsonElement> Functions);

public sealed record SandboxGlobalContract(
    string Name,
    string Kind,
    string? Signature,
    string? Availability);

/// <summary>Engine intrinsics from the built-in authoring descriptor providers; ids are stable code-owned authoring ids, not store state.</summary>
public sealed record IntrinsicContract(
    string? FeatureId,
    string DescriptorId,
    string ActivityTypeKey,
    string Version,
    string DisplayName,
    string? Category,
    string? Description,
    string ExecutionType,
    IReadOnlyList<InputContract> Inputs,
    IReadOnlyList<OutputContract> Outputs,
    IReadOnlyList<PortContract> Ports,
    IntrinsicMapping Intrinsic);

public sealed record IntrinsicMapping(
    string Kind,
    string ValueInputKey,
    string? VariableInputKey,
    string? OutputNameInputKey);

/// <summary>docs/contracts/manifest.json — fingerprints are contract, and the check gate compares this file too.</summary>
public sealed record ContractsManifest(
    string SchemaVersion,
    string Generator,
    // A list, not a dictionary: assembly names are verbatim identifiers and must never be re-cased
    // by a serializer key policy.
    IReadOnlyList<FragmentFingerprint> Fragments,
    string SubmitSchema,
    ContractsManifestCounts Counts);

public sealed record FragmentFingerprint(string Assembly, string Fingerprint);

public sealed record ContractsManifestCounts(int Fragments, int Features, int Activities, int Structures, int Intrinsics);
