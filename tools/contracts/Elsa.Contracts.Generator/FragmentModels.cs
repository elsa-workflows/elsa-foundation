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
    IReadOnlyList<IntrinsicContract> Intrinsics,
    IReadOnlyList<EndpointContract> Endpoints)
{
    // The schema version bumps on ANY published-shape change, additive included (spec 150 FR-D1): three
    // builds moved the submit-schema fingerprint while the version stood still at "1", so a consumer could
    // not tell a stale cache from a current one. Minor = additive and backward-compatible; major = a removal
    // or a reinterpretation of an existing field.
    //
    //   1.1.0 — endpoints, input enumValues/authoredVia, host expressionTypes.
    //   1.2.0 — [ConsumerNote] notes on features, activities and inputs; vocabularies.json.
    public const string CurrentSchemaVersion = "1.2.0";

    public bool HasContributions =>
        Features.Count > 0 || Activities.Count > 0 || Structures.Count > 0 ||
        Intrinsics.Count > 0 || Endpoints.Count > 0 || Expressions is not null;
}

/// <summary>
/// One HTTP endpoint of the published API surface. <see cref="SuccessStatus"/> is absent unless the
/// endpoint declares it (<c>[SuccessStatus]</c>): a guessed 200 is what made every benchmarked session
/// assert the wrong status on publish.
/// </summary>
public sealed record EndpointContract(
    string? FeatureId,
    string Verb,
    string Route,
    // Absent unless the endpoint declares it; never defaulted to 200. More than one entry means the
    // endpoint chooses at runtime — SuccessStatusCondition states the rule.
    IReadOnlyList<int>? SuccessStatuses,
    string? SuccessStatusCondition,
    bool AllowsAnonymous,
    IReadOnlyList<string> Permissions,
    JsonElement? RequestSchema,
    JsonElement? ResponseSchema,
    // CLR full names of the body types. They name the OpenAPI components/schemas entries, so two
    // endpoints sharing a body type share one component instead of duplicating the schema.
    string? RequestType = null,
    string? ResponseType = null,
    // True when the endpoint's Configure() read host options: the route and auth published here are this
    // build's *defaults*, and a host that configures those options serves something else. Stated rather
    // than either omitting the endpoint (which lost the identity token endpoint entirely) or passing a
    // configurable route off as fixed.
    bool ConfigurationDependent = false);

public sealed record FeatureContract(
    string Id,
    string? DisplayName,
    string? Description,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<FeatureOptionContract> Options,
    // [ConsumerNote] declarations on the feature class — typically composition notes (a route prefix a
    // feature imposes, an interaction with another feature) that no option describes.
    IReadOnlyList<ConsumerNoteContract>? Notes = null);

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
    JsonElement? ContainerStructure,
    // [ConsumerNote] declarations on the activity type. Build-time enrichment only: notes are deliberately
    // excluded from ContentHash so editing documentation never forces a new activity version.
    IReadOnlyList<ConsumerNoteContract>? Notes = null);

/// <summary>
/// One <c>[ConsumerNote]</c>: behaviour the shape does not express, attached to the code it describes.
/// <see cref="Member"/> names the enum member or property the note is about when it is narrower than its
/// declaring type.
/// </summary>
public sealed record ConsumerNoteContract(string Kind, string Note, string? Member = null);

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
    JsonElement? UiSpecifications,
    // Accepted members of an enum-typed input in wire spelling; null for every other type.
    IReadOnlyList<string>? EnumValues = null,
    // Where the consumer authors this input. "argument" (the default) means an entry in the node's
    // `inputs`; "intrinsicBlock" means it is carried by the node's sibling `intrinsic` block instead —
    // an intrinsic's variable target is descriptor-only and has no argument binding. Publishing it as a
    // plain required input contradicted the submit schema with no tiebreaker.
    string AuthoredVia = InputAuthoringSites.Argument,
    // [ConsumerNote] declarations on the input's property. A note carrying `member` describes one enum
    // value (RFC finding F1) — for example what choosing `ResponseMode.async` actually does.
    IReadOnlyList<ConsumerNoteContract>? Notes = null);

/// <summary>Where an input is authored in a submission.</summary>
public static class InputAuthoringSites
{
    public const string Argument = "argument";
    public const string IntrinsicBlock = "intrinsicBlock";
}

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
    IReadOnlyList<SandboxGlobalContract> ScriptSandbox,
    // The aliases a VARIABLE may be declared as. Strictly smaller than the alias convention's reserved set
    // — that set governs the `type` of any published input or output, not what a variable can be.
    IReadOnlyList<VariableTypeContract>? VariableTypes = null);

/// <summary>
/// One selectable variable type. The runtime serves the same union at <c>GET /expressions/variable-types</c>;
/// a consumer declaring a variable must use one of these aliases, not any reserved bare alias.
/// </summary>
public sealed record VariableTypeContract(
    string? FeatureId,
    string Alias,
    string DisplayName,
    string Category,
    bool SupportsNull,
    IReadOnlyList<string> SupportedCollectionKinds);

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
    string Hosts,
    string OpenApi,
    string Vocabularies,
    ContractsManifestCounts Counts,
    // Fingerprint of index.json — the entry point. The index itself is a separate artifact rather than a
    // manifest field: the manifest answers "do these contracts match my pinned commit", the index answers
    // "which file do I open", and a consumer needs the second far more often.
    string Index = "");

public sealed record FragmentFingerprint(string Assembly, string Fingerprint);

/// <summary>
/// docs/contracts/hosts.json — which fragments each shipped host actually contains. A consumer's real
/// availability is <c>fragments ∩ shells.json ∩ hosts.json[host]</c>: a feature whose assembly the host
/// does not carry cannot be enabled, regardless of what its fragment describes.
/// </summary>
public sealed record HostsIndex(string SchemaVersion, IReadOnlyList<HostContractSet> Hosts);

/// <summary>
/// What a host actually ships. <see cref="Features"/> is the directly usable term — <c>shells.json</c> is
/// keyed by feature id, so a consumer intersects its enabled ids with this list without first joining
/// through fragments. <see cref="Fragments"/> is the assembly-level view of the same fact.
/// </summary>
public sealed record HostContractSet(
    string Host,
    IReadOnlyList<string> Features,
    // Expression types this host actually serves: the intrinsics that no feature gates
    // (Literal/Object/Input) plus the descriptors of the expression features it ships. Published
    // directly because the feature-keyed intersection silently drops un-gated entries — which made
    // Literal, used on nearly every authored input, unreachable through the documented rule.
    IReadOnlyList<string> ExpressionTypes,
    IReadOnlyList<string> Fragments);

public sealed record ContractsManifestCounts(int Fragments, int Features, int Activities, int Structures, int Intrinsics);
