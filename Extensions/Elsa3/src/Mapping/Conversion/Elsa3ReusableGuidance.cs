namespace Elsa3.Mapping.Conversion;

/// <summary>
/// What an engineer can do about a reusable workflow Elsa 4 graph activities cannot hold yet (spec 246 D-7, D-8). Each
/// text names what is unsupported, where it is tracked, and the two changes to the Elsa 3 workflow that make it import.
/// </summary>
internal static class Elsa3ReusableGuidance
{
    /// <summary>
    /// How a workflow that stops being reusable is still called. Every workflow that uses it as an activity is refused
    /// with ELS3-REUSABLE-UNSUPPORTED-REFERENCE until it calls it another way.
    /// </summary>
    private const string ImportAsRegularWorkflow =
        "or clear 'Usable as activity' on this workflow in Elsa 3 and re-export: it then imports as a regular workflow, " +
        "and every workflow that uses it as an activity must call it with Dispatch Workflow or Execute Workflow instead.";

    public const string Intrinsic =
        "Elsa 4 graph activities cannot run SetVariable, SetOutput, Correlate or SetName yet (tracked in elsa-foundation#3023). " +
        "Change the reusable workflow so its body uses none of them, " + ImportAsRegularWorkflow;

    public const string ValueRead =
        "Elsa 4 graph activities cannot read their own inputs, variables or other activities' results yet (tracked in elsa-foundation#3027). " +
        "Change the reusable workflow so its body works from literal values only, " + ImportAsRegularWorkflow;
}
