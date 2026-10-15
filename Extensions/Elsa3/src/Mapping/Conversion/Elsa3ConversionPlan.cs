using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Resolution;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// What normalization decided for one definition: a plan per node, keyed by node reference, plus every diagnostic.
/// Normalization moves port children (<c>then</c>, <c>body</c>, <c>cases[].activity</c>, …) into
/// <see cref="Elsa3Activity.Activities"/> so value-flow lowering sees them; the plan remembers which slot each child
/// belongs to and the JSON path it came from.
/// </summary>
internal sealed class Elsa3ConversionPlan(Elsa3ConversionRequest request, Elsa3WorkflowDefinition definition)
{
    public Elsa3ConversionRequest Request { get; } = request;

    /// <summary>The private, normalized clone.</summary>
    public Elsa3WorkflowDefinition Definition { get; } = definition;

    public Dictionary<Elsa3Activity, Elsa3NodePlan> Nodes { get; } = new(ReferenceEqualityComparer.Instance);

    public List<Elsa3MigrationDiagnostic> Diagnostics { get; } = [];

    /// <summary>Literal outcomes named by converted Complete/End nodes of a reusable workflow, with their node ids.</summary>
    public List<(string Outcome, string NodeId, string JsonPath)> CompleteOutcomes { get; } = [];

    /// <summary>
    /// Elsa 3 memory slots that a synthesized workflow variable stands in for (plan D-13): a consumer of the slot reads
    /// the variable.
    /// </summary>
    public Dictionary<string, string> MemoryAliases { get; } = new(StringComparer.Ordinal);

    /// <summary>A reusable workflow's declared outputs and the synthesized graph variables that hold them.</summary>
    public List<Elsa3GraphOutputMapping> OutputMappings { get; } = [];

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.IsError);

    public Elsa3NodePlan? Find(Elsa3Activity node) => Nodes.GetValueOrDefault(node);

    /// <summary>The Elsa 3 name of a node's property, undoing normalization's rename to the Elsa 4 key.</summary>
    public string SourceName(Elsa3Activity node, string propertyName) =>
        Find(node)?.SourceNames.GetValueOrDefault(propertyName) ?? propertyName;

    /// <summary>The original JSON path of each node, for diagnostics raised after normalization moved port children.</summary>
    public IReadOnlyDictionary<Elsa3Activity, string> JsonPaths =>
        Nodes.ToDictionary(item => item.Key, item => item.Value.JsonPath, (IEqualityComparer<Elsa3Activity>)ReferenceEqualityComparer.Instance);
}

/// <summary>What normalization decided for one node.</summary>
internal sealed class Elsa3NodePlan(Elsa3Activity node, string jsonPath, Elsa3Activity? parent)
{
    public Elsa3Activity Node { get; } = node;

    public string JsonPath { get; } = jsonPath;

    public Elsa3Activity? Parent { get; } = parent;

    /// <summary>The Elsa 4 activity it becomes, or null when it could not be resolved (an error was reported).</summary>
    public Elsa3ResolvedActivity? Resolved { get; set; }

    /// <summary>Children by slot, in document order. A collection slot's items carry their label (branch name, match).</summary>
    public List<Elsa3SlotChild> Slots { get; } = [];

    /// <summary>The container's own variables, for a structure that supports scoped variables.</summary>
    public List<VariableDefinition> Variables { get; } = [];

    /// <summary>Extra top-level structure payload fields (for example Parallel <c>threshold</c>).</summary>
    public Dictionary<string, object?> StructureFields { get; } = new(StringComparer.Ordinal);

    /// <summary>The intrinsic contract when the node becomes an engine intrinsic.</summary>
    public AuthoredWorkflowIntrinsic? Intrinsic { get; set; }

    /// <summary>Elsa 3 output properties that are loop variables in Elsa 4 (For.CurrentValue → <c>index</c>, …).</summary>
    public Dictionary<string, string> LoopVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Elsa 3 property name each renamed Elsa 4 key came from, for diagnostics after normalization.</summary>
    public Dictionary<string, string> SourceNames { get; } = new(StringComparer.Ordinal);

    /// <summary>The Elsa 4 key of the node's script body, rewritten in script mode, when the node runs a script.</summary>
    public string? ScriptProperty { get; set; }

    /// <summary>Whether normalization reported an error on this node.</summary>
    public bool Failed { get; set; }
}

/// <summary>One child placed in a slot. <see cref="Label"/> is the collection item's label (branch name, case match).</summary>
internal sealed record Elsa3SlotChild(string SlotName, string? Label, Elsa3Activity? Child);
