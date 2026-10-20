namespace Elsa3.Mapping.Conversion;

/// <summary>
/// What an engineer can do about a reusable workflow Elsa 4 graph activities cannot hold yet (spec 246 D-7, D-8). Each
/// text names what is unsupported, where it is tracked, and the change to the Elsa 3 workflow that makes it import, or
/// says plainly that the workflow has to wait.
/// </summary>
/// <remarks>
/// The advice rests on how Elsa 3 runs a reusable workflow: as a composite inside the execution context of the workflow
/// that uses it. Its variables are its own, but Correlate, SetName and Finish act on that calling workflow instance.
/// </remarks>
internal static class Elsa3ReusableGuidance
{
    private const string IntrinsicIssue = "elsa-foundation#3023";
    private const string ValueReadIssue = "elsa-foundation#3027";

    /// <summary>
    /// How a workflow that stops being reusable is still called, and what that costs: the import does not carry a captured
    /// Elsa 3 Execute Workflow result over, so a caller that uses an output is rewired after import, to the Elsa 4 Dispatch
    /// Workflow's <c>Result</c>, which holds the child's outputs when it waits for completion.
    /// </summary>
    internal const string ImportAsRegularWorkflow =
        "clear 'Usable as activity' on this workflow in Elsa 3 and re-export: it then imports as a regular workflow, " +
        "and every workflow that uses it as an activity must call it with Dispatch Workflow or Execute Workflow instead. " +
        ReadOutputsAfterImport;

    /// <summary>What a caller that switches to Dispatch or Execute Workflow loses in the import, and how it gets it back.</summary>
    internal const string ReadOutputsAfterImport =
        "The import does not carry the called workflow's outputs over: a captured Execute Workflow result is refused " +
        "(ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE). A caller that uses an output reads it after import from the Elsa 4 " +
        "Dispatch Workflow's Result, which holds the outputs when it waits for completion.";

    /// <summary>The advice for a reusable body node that becomes the Elsa 4 engine intrinsic for <paramref name="elsa3Type"/>.</summary>
    public static string Intrinsic(string elsa3Type) => ShortName(elsa3Type) switch
    {
        "SetVariable" =>
            $"Elsa 4 graph activities cannot set variables yet (tracked in {IntrinsicIssue}). A reusable workflow's variables " +
            "are its own, so only its body can read them. If nothing in the body reads this variable, remove the Set Variable: " +
            "nothing else changes. If the body reads it, that read is refused too (ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED): " +
            $"keep the workflow in Elsa 3 until {IntrinsicIssue} and {ValueReadIssue} are done, or " + ImportAsRegularWorkflow,
        "SetOutput" =>
            $"Elsa 4 graph activities cannot set their outputs yet (tracked in {IntrinsicIssue}). If no workflow that uses this " +
            $"activity reads the output, remove the Set Output. If one does, keep the workflow in Elsa 3 until {IntrinsicIssue} " +
            "is done, or " + ImportAsRegularWorkflow,
        "Correlate" =>
            $"Elsa 4 graph activities cannot run Correlate yet (tracked in {IntrinsicIssue}). In Elsa 3, Correlate in a reusable " +
            "workflow sets the correlation id of the workflow that uses it. Remove it from the reusable workflow and add a " +
            "Correlate with the same value to each workflow that uses this activity, directly before the activity.",
        "SetName" =>
            $"Elsa 4 graph activities cannot run Set Name yet (tracked in {IntrinsicIssue}). In Elsa 3, Set Name in a reusable " +
            "workflow names the workflow instance that uses it. Remove it from the reusable workflow and add a Set Name with " +
            "the same value to each workflow that uses this activity, directly before or after the activity.",
        "Finish" =>
            $"Elsa 4 graph activities cannot run Finish yet (tracked in {IntrinsicIssue}). In Elsa 3, Finish in a reusable " +
            "workflow ends the whole workflow that uses it, not only the activity. Replace the Finish with a Complete that names " +
            "a new outcome (for example 'Finished'), declare that outcome on the workflow, and in each workflow that uses this " +
            "activity connect that outcome to a Finish.",
        _ =>
            $"Elsa 4 graph activities cannot run SetVariable, SetOutput, Correlate, SetName or Finish yet (tracked in {IntrinsicIssue}). " +
            "Change the reusable workflow so its body uses none of them, or " + ImportAsRegularWorkflow
    };

    /// <summary>
    /// The advice for an HTTP Endpoint in a reusable body whose Headers or Query String output is captured: Elsa 4 rebuilds
    /// the Elsa 3 shape with a set step after the endpoint, which a graph activity cannot run.
    /// </summary>
    public static string HttpProjection(string outputName) =>
        $"Elsa 4 rebuilds the captured '{outputName}' value with a Set Variable step, which a graph activity cannot run yet " +
        $"(tracked in {IntrinsicIssue}), and nothing in a reusable body can read the variable yet ({ValueReadIssue}). Remove " +
        $"the capture of '{outputName}' from the HTTP Endpoint.";

    public const string ValueRead =
        $"Elsa 4 graph activities cannot read their own inputs, variables or other activities' results yet (tracked in {ValueReadIssue}). " +
        "Change the reusable workflow so its body works from literal values only, or " + ImportAsRegularWorkflow;

    public const string Capture =
        $"Nothing in an Elsa 4 graph activity body can read a captured value yet (tracked in {ValueReadIssue}), and a reusable " +
        "workflow's variables are its own, so no caller sees them. Remove the capture from this property. If the body reads " +
        $"the variable later, that read is refused too: keep the workflow in Elsa 3 until {ValueReadIssue} is done, or " +
        ImportAsRegularWorkflow;

    private static string ShortName(string elsa3Type) =>
        elsa3Type.StartsWith("Elsa.", StringComparison.Ordinal) ? elsa3Type["Elsa.".Length..] : elsa3Type;
}
