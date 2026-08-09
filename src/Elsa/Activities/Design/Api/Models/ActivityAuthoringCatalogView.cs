using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityAuthoringCatalogView(IReadOnlyCollection<ActivityAuthoringDescriptorView> Activities);

public sealed record ActivityAuthoringDescriptorView(
    string ActivityVersionId,
    string ActivityTypeKey,
    string Version,
    string DisplayName,
    string? Category,
    string? Description,
    string ExecutionType,
    bool Available,
    string? AvailabilityReason,
    IReadOnlyCollection<ActivityInputDescriptorView> Inputs,
    IReadOnlyCollection<ActivityOutputDescriptorView> Outputs,
    IReadOnlyCollection<ActivityPortDescriptorView> Ports,
    JsonElement? ContainerStructure,
    ActivityAuthoringTemplateView AuthoringTemplate,
    ActivityAuthoringIntrinsicView? Intrinsic = null,
    ActivityAuthoringProvenanceView? Provenance = null);

/// <summary>
/// Provenance of a persisted catalog entry: which reconciliation source produced the version
/// (<see cref="SourceKind"/>/<see cref="SourceId"/>, persisted on the version row) and — for CLR-provided
/// activities — which shell feature provides the activity type (<see cref="FeatureId"/>, resolved
/// best-effort; null when the consumer is not the CLR activity consumer or attribution is unresolvable).
/// The named feature may currently be disabled in the shell composition: the catalog is folder-scanned,
/// so the id is the "enable feature X to use this activity" signal. Built-in engine-intrinsic entries
/// carry no provenance.
/// </summary>
public sealed record ActivityAuthoringProvenanceView(
    string SourceKind,
    string SourceId,
    string? FeatureId);

/// <summary>
/// Present only on built-in engine-intrinsic catalog entries (e.g. Set Variable, Set Output). It tells
/// the authoring client that placing this descriptor must materialize an engine-owned intrinsic node —
/// an <c>ActivityNode</c> carrying an <c>AuthoredWorkflowIntrinsic</c> — rather than a catalog activity
/// reference. The intrinsic never activates a CLR activity at runtime (ADR 0045): the engine writes the
/// variable or workflow output directly. <see cref="Kind"/> is the authored intrinsic kind
/// (e.g. <c>Set</c>, <c>SetOutput</c>); the input-key fields name which descriptor inputs the client maps
/// onto the intrinsic's variable target, value, and (for Set Output) literal output name.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is emitted in the wire spelling of <c>AuthoredWorkflowIntrinsicKind</c> (camelCase,
/// e.g. <c>set</c>) so a client can echo it into a submission verbatim. It previously used the CLR casing
/// (<c>Set</c>), which disagreed with the submit schema's enum — two published spellings for one value,
/// with no tiebreaker (consumer merge-gate evaluation of `35363cebd`). The agreement is pinned by
/// <c>IntrinsicKindWireSpellingTests</c>.
/// </remarks>
public sealed record ActivityAuthoringIntrinsicView(
    string Kind,
    string ValueInputKey,
    string? VariableInputKey,
    string? OutputNameInputKey);

public sealed record ActivityInputDescriptorView(
    string ReferenceKey,
    string Name,
    string Type,
    // The collection shape of the input's CLR type (Single/Array/List/HashSet/Dictionary). Together with
    // <see cref="Type"/> (the element-type alias) this lets the editor render a list/dictionary item editor
    // for a collection-typed input instead of a scalar text box (#924).
    CollectionKind CollectionKind,
    string? DisplayName,
    string? Description,
    float Order,
    string? Category,
    bool IsBrowsable,
    bool IsRequired,
    bool IsNullable,
    string? UiHint,
    JsonElement? DefaultValue,
    string? DefaultSyntax,
    JsonElement? UiSpecifications,
    // G1 (spec 149 / RFC #1191): disambiguates DefaultValue — true with a value = concrete static default;
    // true with null = the default is null; false = no statically representable default exists.
    bool HasStaticDefault = false,
    // Accepted members of an enum-typed input, in wire spelling; null for every other type. Without it a
    // client knows the type name and the default but must guess the remaining accepted values.
    IReadOnlyList<string>? EnumValues = null);

public sealed record ActivityOutputDescriptorView(
    string Name,
    string Type,
    CollectionKind CollectionKind,
    string? DisplayName,
    string? Description,
    string? Category,
    bool IsBrowsable,
    // G2 (spec 149 / RFC #1191): a required output must have an authored target or the publish compile
    // rejects the definition (RuntimeOutputCaptureCompiler); ReferenceKey is the binding key to author it.
    bool IsRequired = false,
    string? ReferenceKey = null);

public sealed record ActivityPortDescriptorView(
    string Name,
    string? DisplayName,
    string? Type,
    bool IsBrowsable,
    string? ReferenceKey = null);

public sealed record ActivityAuthoringTemplateView(
    string NodeId,
    string ActivityVersionId,
    IReadOnlyDictionary<string, ActivityArgumentValue> Inputs,
    IReadOnlyDictionary<string, ActivityArgumentValue> Outputs,
    ActivityAuthoringStructureView? Structure);

public sealed record ActivityAuthoringStructureView(string Kind, string SchemaVersion, JsonElement Payload);
