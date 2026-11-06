using System.Collections.Immutable;
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
    ImmutableArray<OutputDefinition> Outputs);

/// <summary>A consuming location; projected inputs retain their structure-owned stable reference key.</summary>
public sealed record ActivityDataSelectionTarget(string NodeId, string InputKey);

/// <summary>Bounded metadata acquisition within one resolved candidate and consuming location.</summary>
/// <remarks>Source is an existing canonical role binding. It is re-resolved, not trusted as metadata.</remarks>
public sealed record ActivityDataSelectionQuery
{
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

/// <summary>Metadata for an existing role binding; Id and labels are presentation, not runtime identities.</summary>
public sealed record ActivityDataSelectionSourceFacts(
    string Id,
    string DisplayName,
    string? ScopeId,
    ArgumentValue Binding,
    Elsa.Workflows.Design.Core.Models.ActivityChildResultAvailability Availability,
    ActivitySerializedShape Shape,
    string? ReasonCode = null);

/// <summary>One source analysis, used independently by discovery, diagnostics and publication.</summary>
public sealed record ActivityDataSelectionAnalysis(
    string ContractRevision,
    ActivityDataSelectionTargetFacts? Target,
    ImmutableArray<ActivityDataSelectionSourceFacts> Sources,
    ImmutableArray<ValidationError> Findings,
    bool HasMoreSources = false);

/// <summary>An owner-supplied restriction. Multiple contributions intersect; none may weaken another.</summary>
public sealed record ActivityDataInputConstraint(
    string Owner,
    ImmutableArray<string> AllowedExpressionTypes,
    string ReasonCode,
    string Message);
