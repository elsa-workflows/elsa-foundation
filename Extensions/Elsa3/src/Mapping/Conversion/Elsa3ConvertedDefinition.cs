using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// The Elsa 4 result of converting one Elsa 3 definition. For a reusable workflow it also carries what the graph
/// manifest and the activity contract need; for a plain workflow those lists are empty.
/// </summary>
/// <param name="State">The converted workflow state.</param>
/// <param name="OutputMappings">Each declared output and the synthesized graph variable that holds it.</param>
/// <param name="BoundaryOutcomes">The reusable activity's contract outcomes, in ordinal reference-key order.</param>
/// <param name="OutcomeMappings">The graph's outcome mappings: for a reusable workflow exactly <c>done → done</c>.</param>
public sealed record Elsa3ConvertedDefinition(
    WorkflowDefinitionState State,
    IReadOnlyList<Elsa3GraphOutputMapping> OutputMappings,
    IReadOnlyList<ActivityOutcomeContract> BoundaryOutcomes,
    IReadOnlyList<Elsa3GraphOutcomeMapping> OutcomeMappings);

/// <summary>A reusable workflow's declared output and the synthesized graph variable that holds it.</summary>
/// <param name="OutputReferenceKey">The declared output's reference key.</param>
/// <param name="VariableReferenceKey">The synthesized variable, <c>elsa3.output.{sha256(outputReferenceKey)[..16]}</c>.</param>
/// <param name="Type">The output's type alias.</param>
/// <param name="StorageDriverKey">The output's storage driver, or null for the default.</param>
public sealed record Elsa3GraphOutputMapping(
    string OutputReferenceKey,
    string VariableReferenceKey,
    Elsa.Primitives.Models.TypeReference Type,
    string? StorageDriverKey);

/// <summary>A graph outcome mapping: when the entry completes with the source outcome, the boundary completes with the target.</summary>
public sealed record Elsa3GraphOutcomeMapping(string SourceOutcomeReferenceKey, string BoundaryOutcomeReferenceKey);
