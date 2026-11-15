namespace Elsa3.Mapping.ActivityMap;

/// <summary>
/// An Elsa 3 flowchart construct Elsa 4 expresses without a node of its own (spec 239 G-2): graph conversion turns it
/// into connections or the start node and removes it.
/// </summary>
public enum Elsa3GraphConstructKind
{
    /// <summary>FlowFork: becomes fan-out connections.</summary>
    Fork,

    /// <summary>FlowJoin: becomes an implicit join of the node that follows it.</summary>
    Join,

    /// <summary>Start: becomes the flowchart's start node.</summary>
    Start
}
