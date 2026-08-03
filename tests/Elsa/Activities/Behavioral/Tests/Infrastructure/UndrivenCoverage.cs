namespace Elsa.Activities.Behavioral.Infrastructure;

/// <summary>
/// The contract members this suite does <b>not</b> reach in-process, each with the reason. The list is
/// <b>empty</b>: every shipped activity is driven through a real workflow, and every declared outcome and output
/// is measured against what the engine committed.
/// </summary>
/// <remarks>
/// It is kept rather than deleted because the discipline it carries outlives its entries.
/// <c>ActivityDriveHealthTests.Undriven_coverage_is_empty</c> asserts it stays empty, so re-declaring a gap here
/// is a deliberate, reviewable act rather than a quiet way to make a failing assertion go away — an outcome that
/// turns out to be unreachable is a defect in the activity, not an entry in this list.
/// </remarks>
public static class UndrivenCoverage
{
    private static readonly Dictionary<(string Activity, string Member), string> Reasons = [];

    private static readonly Dictionary<string, string> ActivityReasons = new(StringComparer.Ordinal);

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
