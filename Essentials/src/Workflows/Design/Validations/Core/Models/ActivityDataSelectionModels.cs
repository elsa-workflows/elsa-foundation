using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>A request-only view of an actual workflow or closed provider graph, never a persisted draft.</summary>
/// <param name="ResourceKind">The resource owner's kind, not an authorization claim.</param>
/// <param name="ResourceId">The exact owner-resolved resource identity.</param>
/// <param name="ContractRevision">A server-derived identity of the exact canonical candidate.</param>
/// <param name="RootActivity">The actual canonical root.</param>
/// <param name="Inputs">The boundary's declared inputs.</param>
/// <param name="Variables">The boundary's declared variables.</param>
/// <param name="Outputs">The boundary's declared public outputs.</param>
public sealed record ActivityDataSelectionDocument(
    string ResourceKind,
    string ResourceId,
    string ContractRevision,
    Elsa.Workflows.Design.Core.Models.ActivityNode? RootActivity,
    ImmutableArray<InputDefinition> Inputs,
    ImmutableArray<VariableDefinition> Variables,
    ImmutableArray<OutputDefinition> Outputs)
{
    /// <summary>
    /// Complete input states selected by Publishing for each exact resolved closed boundary, including
    /// authored values and selected public defaults. Presence, including an empty array, makes the
    /// invocation opaque to caller-side structure projection. Resource APIs do not accept these facts.
    /// </summary>
    [JsonIgnore]
    public ImmutableDictionary<string, ImmutableArray<Elsa.Workflows.Design.Core.Models.ArgumentState>> CompiledBoundaryInputs { get; init; } =
        ImmutableDictionary.Create<string, ImmutableArray<Elsa.Workflows.Design.Core.Models.ArgumentState>>(StringComparer.Ordinal);
}

/// <summary>A consuming location; projected inputs retain their structure-owned stable reference key.</summary>
public sealed record ActivityDataSelectionTarget(string NodeId, string InputKey);

/// <summary>Bounded metadata acquisition within one resolved candidate and consuming location.</summary>
/// <remarks>Source is an existing canonical role binding. It is re-resolved, not trusted as metadata.</remarks>
public sealed record ActivityDataSelectionQuery
{
    /// <summary>False resolves only the actual target and restrictions, before any source metadata is acquired.</summary>
    public bool IncludeSources { get; init; } = true;
    public string? Search { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 100;
    public ArgumentValue? Source { get; init; }
    public ImmutableArray<string> MemberPath { get; init; } = [];
    public ActivitySerializedShapeLimits ShapeLimits { get; init; } = new();
}

/// <summary>Actual target metadata. It intentionally contains no authored literal or runtime business value.</summary>
public sealed record ActivityDataSelectionTargetFacts(
    ActivityDataSelectionTarget Target,
    string DisplayName,
    TypeReference Type,
    bool IsNullable,
    bool IsRequired,
    bool IsStructureOwned,
    ActivityDataInputConstraint? Constraint,
    bool? IsSensitive = null,
    bool? IsCredential = null);

/// <summary>Authorized source metadata; a null Binding and its ReasonCode mean no safe direct runtime binding is available.</summary>
public sealed record ActivityDataSelectionSourceFacts(
    string Id,
    string DisplayName,
    string? ScopeId,
    ArgumentValue? Binding,
    Elsa.Workflows.Design.Core.Models.ActivityChildResultAvailability Availability,
    ActivitySerializedShape Shape,
    string? ReasonCode = null)
{
    /// <summary>Authorized producer identity for the existing portable-expression contract, independent of direct runtime scope binding.</summary>
    public ActivityResultExpressionParameterBinding? ActivityResultSource { get; init; }
}

/// <summary>One source analysis, used independently by discovery, diagnostics and publication.</summary>
public sealed record ActivityDataSelectionAnalysis(
    string ContractRevision,
    ActivityDataSelectionTargetFacts? Target,
    ImmutableArray<ActivityDataSelectionSourceFacts> Sources,
    ImmutableArray<ValidationError> Findings,
    bool HasMoreSources = false)
{
    /// <summary>
    /// What the analysis established about each admitted member read of an activity result in the document, so
    /// publication can pin the selected member's contract. A read the analysis could not type is listed as not known.
    /// </summary>
    public ImmutableArray<ActivityDataMemberReadFacts> MemberReads { get; init; } = [];

    /// <summary>
    /// Whether the analysis that produced this result offers and admits member reads. A client offers member
    /// selection only when it is true; an answer that does not carry it reads as false.
    /// </summary>
    public bool MemberReadsEnabled { get; init; }
}

/// <summary>
/// The selected member's contract for one admitted member read, at the consuming input <paramref name="Path"/>
/// (<c>{nodeId}/inputs/{inputKey}</c>, the path findings use). When <paramref name="IsKnown"/> is false every other
/// fact is unknown and the runtime checks the member's fit when it reads it.
/// </summary>
/// <param name="Path">The consuming input location.</param>
/// <param name="ProducerNodeId">The producer the read names.</param>
/// <param name="ProjectionKey">The producer projection the read names.</param>
/// <param name="MemberPath">The exact member segments.</param>
/// <param name="IsKnown">Whether the selected member's type is established.</param>
/// <param name="SelectedType">The selected member's type, when known.</param>
/// <param name="MayBeAbsent">Whether the member, or a member before it, may be missing, when known.</param>
/// <param name="MayBeNull">Whether the member, or a member before it, may be null, when known.</param>
/// <param name="Representation">The selected member's value representation, when known.</param>
/// <param name="ProfileRevision">The serialization profile revision the facts were read under, when known.</param>
public sealed record ActivityDataMemberReadFacts(
    string Path,
    string ProducerNodeId,
    string ProjectionKey,
    ImmutableArray<string> MemberPath,
    bool IsKnown,
    TypeReference? SelectedType = null,
    bool? MayBeAbsent = null,
    bool? MayBeNull = null,
    ValueRepresentation? Representation = null,
    string? ProfileRevision = null);

/// <summary>An owner-supplied restriction. Multiple contributions intersect; none may weaken another.</summary>
public sealed record ActivityDataInputConstraint(
    string Owner,
    ImmutableArray<string> AllowedExpressionTypes,
    string ReasonCode,
    string Message);
