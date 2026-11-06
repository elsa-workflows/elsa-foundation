using Elsa.Workflows.Testing.Core.Contracts;
using Elsa.Workflows.Testing.Core.Models;

namespace Elsa.Workflows.Testing.Services;

internal sealed class WorkflowTestReportPolicy : IWorkflowTestReportPolicy
{
    public int GetExitCode(TestRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.UserCancellationRequested) return 130;
        if (report.Lifecycle != TestRunLifecycle.Finalized || report.Cases.Count == 0) return 2;
        if (!HasExactSelectionCoverage(report)) return 2;
        var asserted = false;
        var failed = false;
        foreach (var item in report.Cases)
        {
            var selection = report.Selections.Single(x => x.Key == item.SelectionKey);
            if (item.Verdict == TestVerdict.Skipped)
            {
                if (!HasAuthorizedSkip(report, item, selection)) return 2;
                continue;
            }
            if (!HasAuthoritativeChecks(item, selection.Plan)) return 2;
            asserted = true;
            if (item.Verdict == TestVerdict.Failed) failed = true;
            else if (item.Verdict != TestVerdict.Passed) return 2;
        }
        return !asserted ? 2 : failed ? 1 : 0;
    }

    internal static bool HasSettledCleanup(TestCaseResult item) =>
        item.Closure.State == TestClosureState.Closed && item.Closure.CleanupSettled &&
        item.Closure.Remaining.Count == 0;

    internal static bool HasAuthoritativeChecks(TestCaseResult item, TestCasePlan plan)
    {
        if (!HasSettledCleanup(item) || !ValidPlan(plan) || !ChecksMatch(item.Checks, plan.Checks) ||
            item.Steps.Count != plan.Steps.Count || !UniqueKeys(item.Steps.Select(x => x.Key))) return false;
        for (var index = 0; index < item.Steps.Count; index++)
        {
            var actual = item.Steps[index];
            var expected = plan.Steps[index];
            if (actual.Key != expected.Key || actual.Profile != expected.ActionProfile || actual.ActionDigest != expected.ActionDigest ||
                !ChecksMatch(actual.Checks, expected.Checks)) return false;
        }
        var checks = item.Checks.Concat(item.Steps.SelectMany(x => x.Checks)).ToArray();
        if (checks.Length == 0) return false;
        if (item.Verdict == TestVerdict.Passed)
            return checks.All(x => x.Verdict == TestCheckVerdict.Passed && x.Actual.Availability == TestEvidenceAvailability.Available) &&
                   item.Steps.All(x => x.State == TestStepState.Settled);
        if (item.Verdict != TestVerdict.Failed) return false;

        var failed = false;
        var failFastTail = false;
        foreach (var step in item.Steps)
        {
            if (step.State == TestStepState.Settled)
            {
                if (failFastTail || !ExecutedChecks(step.Checks, ref failed)) return false;
            }
            else if (step.State is TestStepState.NotStarted or TestStepState.Skipped)
            {
                if (!failed || step.Checks.Any(x => x.Verdict != TestCheckVerdict.NotEvaluated)) return false;
                failFastTail = true;
            }
            else return false;
        }
        return ExecutedChecks(item.Checks, ref failed) && failed;
    }

    internal static TestRunSelection? SelectionFor(TestRunReport report, TestCaseResult item)
    {
        var matching = report.Selections.Where(x => x.Key == item.SelectionKey).Take(2).ToArray();
        return matching.Length == 1 && matching[0].Case == item.Case && matching[0].Required == item.Required &&
               ValidPlan(matching[0].Plan) ? matching[0] : null;
    }

    internal static bool HasAuthorizedSkip(TestRunReport report, TestCaseResult item, TestRunSelection selection)
    {
        if (item.Required || !report.OptionalSkipsAllowed || string.IsNullOrWhiteSpace(item.SkipReason) ||
            selection.SkipReason != item.SkipReason || !HasSettledCleanup(item) || !ValidPlan(selection.Plan) ||
            !ChecksMatch(item.Checks, selection.Plan.Checks, allowOmitted: true) ||
            item.Checks.Any(x => x.Verdict != TestCheckVerdict.NotEvaluated) || !UniqueKeys(item.Steps.Select(x => x.Key))) return false;
        var next = 0;
        foreach (var step in item.Steps)
        {
            while (next < selection.Plan.Steps.Count && selection.Plan.Steps[next].Key != step.Key) next++;
            if (next == selection.Plan.Steps.Count) return false;
            var expected = selection.Plan.Steps[next++];
            if (step.Profile != expected.ActionProfile || step.ActionDigest != expected.ActionDigest ||
                step.State is not (TestStepState.NotStarted or TestStepState.Skipped) ||
                !ChecksMatch(step.Checks, expected.Checks, allowOmitted: true) ||
                step.Checks.Any(x => x.Verdict != TestCheckVerdict.NotEvaluated)) return false;
        }
        return true;
    }

    private static bool ExecutedChecks(IReadOnlyList<TestCheckResult> checks, ref bool failed)
    {
        foreach (var check in checks)
        {
            if (check.Verdict is TestCheckVerdict.Passed or TestCheckVerdict.Failed)
            {
                if (check.Actual.Availability != TestEvidenceAvailability.Available) return false;
                failed |= check.Verdict == TestCheckVerdict.Failed;
            }
            else if (check.Verdict != TestCheckVerdict.NotEvaluated || !failed) return false;
        }
        return true;
    }

    private static bool ChecksMatch(IReadOnlyList<TestCheckResult> actual, IReadOnlyList<TestCheckPlan> expected, bool allowOmitted = false)
    {
        if (!UniqueKeys(actual.Select(x => x.Key)) || !allowOmitted && actual.Count != expected.Count) return false;
        var next = 0;
        foreach (var check in actual)
        {
            if (allowOmitted)
                while (next < expected.Count && expected[next].Key != check.Key) next++;
            if (next == expected.Count) return false;
            var planned = expected[next++];
            if (check.Key != planned.Key || check.Profile != planned.Profile || check.DefinitionDigest != planned.DefinitionDigest ||
                !WorkflowTestPlanCapture.MatchesDefinition(check)) return false;
        }
        return true;
    }

    private static bool ValidPlan(TestCasePlan? plan) => plan is not null &&
        UniqueKeys(plan.Checks.Select(x => x.Key)) && plan.Checks.All(ValidCheck) &&
        UniqueKeys(plan.Steps.Select(x => x.Key)) && plan.Steps.All(x =>
            !string.IsNullOrWhiteSpace(x.ActionProfile) && WorkflowTestPlanCapture.IsDigest(x.ActionDigest) &&
            UniqueKeys(x.Checks.Select(c => c.Key)) && x.Checks.All(ValidCheck));

    private static bool ValidCheck(TestCheckPlan check) => !string.IsNullOrWhiteSpace(check.Profile) &&
        WorkflowTestPlanCapture.IsDigest(check.DefinitionDigest);

    private static bool HasExactSelectionCoverage(TestRunReport report)
    {
        if (report.Selections.Count != report.Cases.Count || !UniqueKeys(report.Selections.Select(x => x.Key)) ||
            !UniqueKeys(report.Cases.Select(x => x.SelectionKey)) || !UniqueKeys(report.Cases.Select(x => x.AttemptId))) return false;
        var selections = report.Selections.ToDictionary(x => x.Key, StringComparer.Ordinal);
        foreach (var item in report.Cases)
        {
            if (!selections.TryGetValue(item.SelectionKey, out var expected) || expected.Case != item.Case || expected.Required != item.Required || !ValidPlan(expected.Plan) ||
                item.Verdict == TestVerdict.Skipped && (expected.SkipReason != item.SkipReason || string.IsNullOrWhiteSpace(expected.SkipReason)) ||
                expected.SkipReason is not null && item.Verdict != TestVerdict.Skipped) return false;
        }
        if (report.Suite is not { } suite) return true;
        return suite.Members.Count == report.Selections.Count && UniqueKeys(suite.Members.Select(x => x.Key)) &&
               suite.Members.All(x => selections.TryGetValue(x.Key, out var selected) && x.Case == selected.Case &&
                   x.Required == selected.Required && x.SkipReason == selected.SkipReason);
    }

    private static bool UniqueKeys(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return keys.All(x => !string.IsNullOrWhiteSpace(x) && seen.Add(x));
    }
}
