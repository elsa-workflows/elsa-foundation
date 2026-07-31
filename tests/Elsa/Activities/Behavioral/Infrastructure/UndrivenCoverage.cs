namespace Elsa.Activities.Behavioral.Infrastructure;

/// <summary>
/// The contract members this suite does <b>not</b> yet reach in-process, each with the reason. Everything listed
/// here reports as a <em>skipped</em> test, never a passing one: the point of this suite is that unverified is
/// visibly different from verified, so an outstanding gap must not be able to hide inside a green run.
/// </summary>
/// <remarks>
/// Deleting an entry is the definition of done for that member. Nothing may be added here to make a failing
/// assertion go away — an outcome that turns out to be genuinely unreachable is a defect in the activity, not an
/// entry in this list.
/// </remarks>
public static class UndrivenCoverage
{
    private const string DispatchWorkflowActivity = "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow";
    private const string GraphActivity = "Elsa.Activities.Graph.Runtime.Activities.GraphActivity";

    private const string DispatchLifecycleReason =
        "DispatchWorkflow refuses to execute against a hand-built node: it requires the exact child-executable pin the " +
        "publish compiler stamps into node metadata, plus the outbox sweep, child admission and parent-resume path. " +
        "None of that is stood up by the plain WorkflowExecutionHarness. It is covered in-process by " +
        "tests/Elsa/Activities/DispatchWorkflow/Tests (DispatchWorkflowEndToEndTests, ParentResumeExecutorTests) on that " +
        "module's own runtime fixture, and end-to-end by e2e-tests; folding that fixture into this suite is the " +
        "remaining work.";

    private const string GraphInliningReason =
        "GraphActivity is an inlining boundary: driving it needs a published reusable-activity definition version and " +
        "its provider, not a hand-built node. It is covered in-process by tests/Elsa/Activities/Graph/Tests; standing " +
        "the publish path up inside this suite is the remaining work.";

    private static readonly Dictionary<(string Activity, string Member), string> Reasons = new()
    {
        [(DispatchWorkflowActivity, "Dispatched")] = DispatchLifecycleReason,
        [(DispatchWorkflowActivity, "ChildWorkflowExecutionId")] = DispatchLifecycleReason,
        [(DispatchWorkflowActivity, "Completed")] = DispatchLifecycleReason,
        [(DispatchWorkflowActivity, "Faulted")] = DispatchLifecycleReason,
        [(DispatchWorkflowActivity, "Cancelled")] = DispatchLifecycleReason,
        [(DispatchWorkflowActivity, "DispatchFailed")] = DispatchLifecycleReason,
        [(DispatchWorkflowActivity, "Result")] = DispatchLifecycleReason,
        [(GraphActivity, "Done")] = GraphInliningReason
    };

    private static readonly Dictionary<string, string> ActivityReasons = new(StringComparer.Ordinal)
    {
        [GraphActivity] = GraphInliningReason,
        [DispatchWorkflowActivity] = DispatchLifecycleReason
    };

    /// <summary>The reason a specific outcome or output is not driven here, or null when it is expected to be.</summary>
    public static string? ReasonFor(string activityTypeName, string member) =>
        Reasons.GetValueOrDefault((activityTypeName, member));

    /// <summary>Every declared gap as "activity.member: reason", for reporting in the test output.</summary>
    public static IReadOnlyList<string> Describe() => Reasons
        .Select(entry => $"{entry.Key.Activity}.{entry.Key.Member}: {entry.Value}")
        .OrderBy(line => line, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The reason a whole activity has no drive here, or null when one is expected.</summary>
    public static string? ReasonForActivity(string activityTypeName) =>
        ActivityReasons.GetValueOrDefault(activityTypeName);
}
