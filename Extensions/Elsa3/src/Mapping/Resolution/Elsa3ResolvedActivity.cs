using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.ActivityMap;

namespace Elsa3.Mapping.Resolution;

/// <summary>Where a resolved node's Elsa 4 activity comes from.</summary>
internal enum Elsa3ResolvedSource
{
    /// <summary>The collection plan rewrites the node to an exact reusable activity version.</summary>
    Replacement,

    /// <summary>A built-in map entry targeting a catalog activity.</summary>
    BuiltInMap,

    /// <summary>A catalog activity that declares it replaces the Elsa 3 type.</summary>
    Alias,

    /// <summary>The Elsa 3 type used verbatim as a catalog type key.</summary>
    Verbatim,

    /// <summary>An engine intrinsic from the built-in map.</summary>
    Intrinsic,

    /// <summary>A flowchart construct (FlowFork, FlowJoin, Start) that graph conversion consumes; it has no Elsa 4 node.</summary>
    GraphConstruct
}

/// <summary>The Elsa 4 activity one Elsa 3 node resolves to, with the contract conversion needs.</summary>
/// <param name="Source">Where the resolution came from.</param>
/// <param name="Entry">The built-in map entry, or null for an alias, verbatim key or replacement.</param>
/// <param name="ActivityVersionId">The catalog version id, intrinsic version id or replacement version id.</param>
/// <param name="ActivityTypeKey">The catalog type key, when the target is a catalog activity.</param>
/// <param name="Inputs">The declared inputs, keyed by reference key.</param>
/// <param name="Outputs">The declared outputs.</param>
/// <param name="Structure">The native structure the target declares, or null for a leaf.</param>
/// <param name="IntrinsicKind">The intrinsic kind, when the target is an intrinsic.</param>
internal sealed record Elsa3ResolvedActivity(
    Elsa3ResolvedSource Source,
    Elsa3ActivityMapEntry? Entry,
    string ActivityVersionId,
    string? ActivityTypeKey,
    IReadOnlyList<InputDefinition> Inputs,
    IReadOnlyList<OutputDefinition> Outputs,
    Elsa3StructureContract? Structure,
    AuthoredWorkflowIntrinsicKind? IntrinsicKind = null)
{
    /// <summary>The outcomes the Elsa 4 activity emits, which graph conversion routes connections by (spec 239 R-3).</summary>
    public Elsa3ActivityOutcomes Outcomes { get; init; } = Elsa3ActivityOutcomes.DoneOnly;

    /// <summary>The input whose reference key is <paramref name="key"/> (ordinal), else whose name matches ignoring case.</summary>
    public InputDefinition? FindInput(string key) =>
        Inputs.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, key))
        ?? Inputs.FirstOrDefault(input => StringComparer.OrdinalIgnoreCase.Equals(input.Name, key))
        ?? Inputs.FirstOrDefault(input => StringComparer.OrdinalIgnoreCase.Equals(input.ReferenceKey, key));

    /// <summary>The output whose reference key is <paramref name="key"/> (ordinal), else whose name matches ignoring case.</summary>
    public OutputDefinition? FindOutput(string key) =>
        Outputs.FirstOrDefault(output => StringComparer.Ordinal.Equals(output.ReferenceKey, key))
        ?? Outputs.FirstOrDefault(output => StringComparer.OrdinalIgnoreCase.Equals(output.Name, key))
        ?? Outputs.FirstOrDefault(output => StringComparer.OrdinalIgnoreCase.Equals(output.ReferenceKey, key));
}

/// <summary>
/// The outcomes an Elsa 4 activity emits: its static ports, plus a rule for outcomes named by the node's own input
/// (one per authored item, for example SendHttpRequest status codes). Read from the catalog's <c>elsa.outcomes</c> facet;
/// an activity without the facet emits <c>Done</c>.
/// </summary>
internal sealed record Elsa3ActivityOutcomes(IReadOnlyList<string> Ports, Elsa3DynamicOutcomes? Dynamic = null)
{
    public const string Done = "Done";

    /// <summary>An activity that declares no outcomes, and every intrinsic: it completes with <c>Done</c>.</summary>
    public static Elsa3ActivityOutcomes DoneOnly { get; } = new([Done]);

    /// <summary>A graph construct: it has no node, so it emits nothing of its own.</summary>
    public static Elsa3ActivityOutcomes None { get; } = new([]);

    /// <summary>Whether <paramref name="port"/> is a static port (ordinal).</summary>
    public bool HasPort(string port) => Ports.Contains(port, StringComparer.Ordinal);
}

/// <summary>Outcomes named by the items of the node's <paramref name="SourceInputKey"/> input, plus an unmatched outcome.</summary>
internal sealed record Elsa3DynamicOutcomes(string SourceInputKey, string? UnmatchedOutcome);

/// <summary>A native structure a catalog activity version declares through its structure design facet.</summary>
internal sealed record Elsa3StructureContract(
    string Kind,
    string SchemaVersion,
    ActivityStructureDesignFacetPayload Payload);

/// <summary>Why a node could not be resolved: a stable code, a message and guidance.</summary>
internal sealed record Elsa3ResolutionFailure(string Code, string Message, string Guidance);

/// <summary>The outcome of resolving one node: exactly one of <see cref="Activity"/> or <see cref="Failure"/>.</summary>
internal sealed record Elsa3Resolution(Elsa3ResolvedActivity? Activity, Elsa3ResolutionFailure? Failure)
{
    public static Elsa3Resolution Resolved(Elsa3ResolvedActivity activity) => new(activity, null);

    public static Elsa3Resolution Failed(string code, string message, string guidance) =>
        new(null, new Elsa3ResolutionFailure(code, message, guidance));
}
