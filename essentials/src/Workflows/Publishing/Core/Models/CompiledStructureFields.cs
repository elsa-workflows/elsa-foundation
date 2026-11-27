namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>
/// Fields Publishing owns inside a compiled executable structure payload, next to the activity-owned fields. The
/// structure compilers replace whatever an activity's own structure handler put under these names.
/// </summary>
public static class CompiledStructureFields
{
    /// <summary>
    /// The keys of the iteration values a loop node declares for its body (spec 1222 FR-004a), such as For's
    /// <c>index</c>. The loop supplies their values on each pass, so they are not variable declarations; read admission
    /// uses them to accept a body read pinned to the loop node, including inside a placed reusable body (#5849).
    /// </summary>
    public const string IterationValues = "iterationValues";
}
