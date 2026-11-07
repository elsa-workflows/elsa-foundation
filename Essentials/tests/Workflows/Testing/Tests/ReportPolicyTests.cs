using System.Xml.Linq;
using System.Collections.Concurrent;
using Elsa.Workflows.Testing.Core.Contracts;
using Elsa.Workflows.Testing.Core.Models;
using Elsa.Workflows.Testing.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Testing.Tests;

public class ReportPolicyTests
{
    private readonly WorkflowTestReportPolicy _policy = new();
    private static int _nextIdentity;
    private static readonly ConcurrentDictionary<string, TestCasePlan> AdmittedPlans = new(StringComparer.Ordinal);
    private static TestAssertion Assertion(string key = "business-check") => new()
    {
        Key = key, Profile = "elsa.workflow-test-assertion/1", Operator = TestAssertionOperator.Equals,
        Subject = new() { Kind = TestSubjectKind.Output, NodeId = "selected", MemberKey = "total" },
        Expected = DeterministicAssertionTests.Json("42")
    };
    private static TestScenarioStep Step(string key, params TestAssertion[] checks) => new()
    {
        Key = key, Action = new("wait/1", DeterministicAssertionTests.Json("{\"payload\":{\"approved\":false}}")), Assertions = checks
    };
    private static TestCaseContent Content(IReadOnlyList<TestAssertion>? checks = null, IReadOnlyList<TestScenarioStep>? steps = null) => new()
    {
        Name = "Reject order",
        Target = new() { Kind = TestTargetKind.ActivityOccurrence, DefinitionId = "workflow", ModelProfile = "native/1", NodeId = "selected" },
        Assertions = checks ?? [Assertion()], Steps = steps ?? []
    };
    private static TestCaseResult Case(TestVerdict verdict = TestVerdict.Passed, TestCaseContent? content = null)
    {
        var identity = Interlocked.Increment(ref _nextIdentity).ToString(System.Globalization.CultureInfo.InvariantCulture);
        content ??= Content();
        // Capture admission expectations from authored content before evaluating or constructing any result.
        AdmittedPlans["selection-" + identity] = WorkflowTestPlanCapture.Capture(content);
        var evaluator = new WorkflowTestAssertionEvaluator();
        TestCheckResult Evaluate(TestAssertion check) => evaluator.Evaluate(check,
            new(new(TestEvidenceAvailability.Available, DeterministicAssertionTests.Json(verdict == TestVerdict.Failed ? "43" : "42")), true, true));
        return new()
        {
        AttemptId = "attempt-" + identity, SelectionKey = "selection-" + identity, Case = new("case", "revision-1", "case-hash"),
        Candidate = new() { DefinitionId = "workflow", SourceId = "snapshot", SourceFingerprint = "source-hash", ArtifactId = "artifact", ArtifactFingerprint = "artifact-hash", ModelProfile = "native/1", Dependencies = [new("http", "http/1", "connection-2", "dependency-hash")] },
        Target = new() { Kind = TestTargetKind.ActivityOccurrence, DefinitionId = "workflow", ModelProfile = "native/1", NodeId = "selected" },
        Mode = TestExecutionMode.Controlled, Time = new(), Verdict = verdict, Execution = new("native/1", "Completed", "execution"),
        Closure = new(TestClosureState.Closed, true, []),
        Checks = verdict == TestVerdict.Skipped ? [] : content.Assertions.Select(Evaluate).ToArray(),
        Steps = verdict == TestVerdict.Skipped ? [] : content.Steps.Select(step => new TestStepResult(step.Key,
            step.Action.Profile, WorkflowTestPlanCapture.ActionDigest(step), TestStepState.Settled, 3,
            step.Assertions.Select(Evaluate).ToArray())).ToArray()
        };
    }
    private static TestRunReport Report(params TestCaseResult[] cases) => new()
    {
        RunId = "run", RequestFingerprint = "request-hash", MaterialFingerprint = "material-hash", Lifecycle = TestRunLifecycle.Finalized,
        ObservationSequence = 7, ObservedAt = DateTimeOffset.UnixEpoch, Cases = cases,
        Selections = cases.Select(x => new TestRunSelection(x.SelectionKey, x.Case, AdmittedPlans[x.SelectionKey], x.Required, x.SkipReason)).ToArray()
    };

    [Fact]
    public void Execution_status_and_business_verdict_are_independent()
    {
        Assert.Equal(0, _policy.GetExitCode(Report(Case() with { Execution = new("native/1", "Faulted") })));
        Assert.Equal(1, _policy.GetExitCode(Report(Case(TestVerdict.Failed))));
    }

    [Theory]
    [InlineData(TestVerdict.NotEvaluated)]
    [InlineData(TestVerdict.Unavailable)]
    [InlineData(TestVerdict.Incomplete)]
    [InlineData(TestVerdict.Cancelled)]
    public void Non_assertion_inability_takes_precedence_over_a_business_failure(TestVerdict verdict) =>
        Assert.Equal(2, _policy.GetExitCode(Report(Case(TestVerdict.Failed), Case(verdict))));

    [Fact]
    public void Cleanup_and_authoritative_checks_are_required_for_success()
    {
        Assert.Equal(2, _policy.GetExitCode(Report(Case() with { Checks = [] })));
        Assert.Equal(2, _policy.GetExitCode(Report(Case() with { Closure = new(TestClosureState.Reconciling, false, []) })));
        Assert.Equal(2, _policy.GetExitCode(Report(Case() with { Checks = [Case(TestVerdict.Failed).Checks[0]] })));
        Assert.Equal(2, _policy.GetExitCode(Report(Case()) with { Lifecycle = TestRunLifecycle.Executing }));
    }

    [Fact]
    public void Skips_are_explicit_optional_and_cannot_make_an_empty_selection_pass()
    {
        var skipped = Case(TestVerdict.Skipped) with { Required = false, SkipReason = "Not applicable to this test environment." };
        Assert.Equal(2, _policy.GetExitCode(Report()));
        Assert.Equal(2, _policy.GetExitCode(Report(skipped) with { OptionalSkipsAllowed = true }));
        Assert.Equal(2, _policy.GetExitCode(Report(Case(), skipped)));
        Assert.Equal(0, _policy.GetExitCode(Report(Case(), skipped) with { OptionalSkipsAllowed = true }));
        Assert.Equal(2, _policy.GetExitCode(Report(Case(), skipped with { Required = true }) with { OptionalSkipsAllowed = true }));
        Assert.Equal(2, _policy.GetExitCode(Report(Case(), skipped with { SkipReason = "" }) with { OptionalSkipsAllowed = true }));
        Assert.Equal(2, _policy.GetExitCode(Report(Case(), skipped with { Closure = new(TestClosureState.Reconciling, false, []) }) with { OptionalSkipsAllowed = true }));
    }

    [Fact]
    public void User_cancellation_has_its_own_exit_without_claiming_settlement() =>
        Assert.Equal(130, _policy.GetExitCode(Report(Case(TestVerdict.Failed)) with { UserCancellationRequested = true, Lifecycle = TestRunLifecycle.Closing }));

    [Fact]
    public void Junit_retains_exact_identity_modes_and_distinct_nonpass_states()
    {
        var report = Report(Case(), Case(TestVerdict.Failed), Case(TestVerdict.Unavailable), Case(TestVerdict.Cancelled), Case(TestVerdict.Skipped));
        using var output = new StringWriter();
        new WorkflowTestReportWriter([_policy]).WriteJunit(report, output);
        var xml = XDocument.Parse(output.ToString());
        var suite = xml.Root!;
        Assert.Equal("5", (string?)suite.Attribute("tests"));
        Assert.Equal("1", (string?)suite.Attribute("failures"));
        Assert.Equal("2", (string?)suite.Attribute("errors"));
        Assert.Equal("1", (string?)suite.Attribute("skipped"));
        var properties = suite.Elements("testcase").First().Element("properties")!.Elements("property").ToDictionary(x => (string)x.Attribute("name")!, x => (string)x.Attribute("value")!);
        Assert.Equal("revision-1", properties["caseRevision"]);
        Assert.Equal("artifact-hash", properties["artifactFingerprint"]);
        Assert.Equal("Controlled", properties["mode"]);
        Assert.Equal("selected", properties["targetNode"]);
        Assert.Equal("http/1:connection-2:dependency-hash", properties["dependency:http"]);
        Assert.Equal(TestVerdict.Unavailable, report.Cases[2].Verdict);
    }

    [Fact]
    public void Junit_does_not_mark_a_pending_pass_as_success_and_escapes_diagnostics()
    {
        using var output = new StringWriter();
        var item = Case() with { Case = new("<case>\0", "revision", "hash") };
        new WorkflowTestReportWriter([_policy]).WriteJunit(Report(item) with { Lifecycle = TestRunLifecycle.Executing }, output);
        var xml = XDocument.Parse(output.ToString());
        Assert.NotNull(xml.Root!.Element("testcase")!.Element("error"));
        Assert.Equal(item.SelectionKey, (string?)xml.Root.Element("testcase")!.Attribute("name"));
        Assert.Contains(xml.Descendants("property"), x => (string?)x.Attribute("name") == "caseId" && (string?)x.Attribute("value") == "<case>\uFFFD");
    }

    [Fact]
    public void Composed_default_reports_use_the_registered_policy_and_reject_ambiguity()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowTestReportPolicy, FixedPolicy>();
        new WorkflowsTestingFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var output = new StringWriter();
        scope.ServiceProvider.GetRequiredService<IWorkflowTestReportWriter>().WriteJunit(Report(Case()), output);
        var xml = XDocument.Parse(output.ToString());
        Assert.Contains(xml.Descendants("property"), x => (string?)x.Attribute("name") == "exitCode" && (string?)x.Attribute("value") == "17");
        Assert.Throws<InvalidOperationException>(() => new WorkflowTestReportWriter([_policy, new FixedPolicy()]));
    }

    private sealed class FixedPolicy : IWorkflowTestReportPolicy { public int GetExitCode(TestRunReport report) => 17; }

    [Fact]
    public void Exact_selection_and_suite_coverage_cannot_omit_or_duplicate_required_members()
    {
        var report = Report(Case(), Case());
        report = report with { Suite = new(new("suite", "suite-revision", "suite-hash"), report.Selections.Select(x => new ResolvedTestSuiteMember(x.Key, x.Case, x.Required, x.SkipReason)).ToArray(), "members-hash") };
        Assert.Equal(0, _policy.GetExitCode(report));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0]] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0], report.Cases[0]] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [.. report.Cases, Case()] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0], report.Cases[1] with { Case = new("case", "different-revision", "different-hash") }] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Suite = report.Suite! with { Members = [report.Suite.Members[0]] } }));
        Assert.Equal(2, _policy.GetExitCode(Report(Case(), Case()) with { Cases = [report.Cases[0]] }));
    }

    [Fact]
    public void Step_checks_are_required_and_can_be_the_only_assertions()
    {
        var item = Case(content: Content([], [Step("approval", Assertion())]));
        var step = item.Steps[0];
        var check = step.Checks[0];
        Assert.Equal(0, _policy.GetExitCode(Report(item)));
        Assert.Equal(2, _policy.GetExitCode(Report(item with { Steps = [step with { Checks = [check with { Verdict = TestCheckVerdict.Unavailable }] }] })));
        Assert.Equal(2, _policy.GetExitCode(Report(item with { Steps = [step with { Checks = [check with { Verdict = TestCheckVerdict.Failed }] }] })));
        Assert.Equal(1, _policy.GetExitCode(Report(item with { Verdict = TestVerdict.Failed, Steps = [step with { Checks = [check with { Verdict = TestCheckVerdict.Failed }] }] })));
        Assert.Equal(2, _policy.GetExitCode(Report(item with { Steps = [step with { State = TestStepState.Executing }] })));
        Assert.Equal(2, _policy.GetExitCode(Report(item with { Steps = [step, step] })));
    }

    [Fact]
    public void Junit_exposes_aggregate_inability_and_unsettled_cancellation_as_standard_errors()
    {
        var skipped = Case(TestVerdict.Skipped) with { Required = false, SkipReason = "Not applicable." };
        foreach (var report in new[] { Report(), Report(skipped) with { OptionalSkipsAllowed = true }, Report(Case()) with { Selections = [] }, Report(Case(TestVerdict.Cancelled) with { Closure = new(TestClosureState.Reconciling, false, []) }) })
        {
            using var output = new StringWriter();
            new WorkflowTestReportWriter([_policy]).WriteJunit(report, output);
            var xml = XDocument.Parse(output.ToString());
            Assert.NotEmpty(xml.Descendants("error"));
            Assert.NotEqual("0", (string?)xml.Root!.Attribute("errors"));
        }
    }

    [Fact]
    public async Task Startup_rejects_duplicate_replacements_added_after_feature_configuration()
    {
        var services = new ServiceCollection();
        new WorkflowsTestingFeature().ConfigureServices(services);
        services.AddScoped<IWorkflowTestReportPolicy, FixedPolicy>();
        var validator = new WorkflowTestingCompositionValidator(new(services));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.InitializeAsync());
        Assert.Contains(nameof(IWorkflowTestReportPolicy), error.Message);
    }

    [Fact]
    public async Task Copied_shell_collection_checks_its_own_late_replacements()
    {
        var parent = new ServiceCollection();
        var feature = new WorkflowsTestingFeature();
        feature.ConfigureServices(parent);
        IServiceCollection child = new ServiceCollection();
        foreach (var descriptor in parent) child.Add(descriptor);
        feature.ConfigureServices(child);
        child.AddScoped<IWorkflowTestReportPolicy, FixedPolicy>();
        using var provider = child.BuildServiceProvider();
        var validator = new WorkflowTestingCompositionValidator(provider.GetRequiredService<WorkflowTestingComposition>());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.InitializeAsync());
        Assert.Contains(nameof(IWorkflowTestReportPolicy), error.Message);
        Assert.Single(parent, x => x.ServiceType == typeof(IWorkflowTestReportPolicy));
    }

    [Fact]
    public void Captured_plan_requires_all_case_checks_and_ordered_steps_without_extras()
    {
        var content = Content([Assertion("case-one"), Assertion("case-two")],
            [Step("approval", Assertion("step-one"), Assertion("step-two")), Step("settle", Assertion("settled"))]);
        var item = Case(content: content);
        var report = Report(item);
        Assert.Equal(0, _policy.GetExitCode(report));
        foreach (var changed in new[]
        {
            item with { Checks = [item.Checks[0]] },
            item with { Checks = [item.Checks[1], item.Checks[0]] },
            item with { Checks = [.. item.Checks, item.Checks[0]] },
            item with { Steps = [item.Steps[0]] },
            item with { Steps = [item.Steps[1], item.Steps[0]] },
            item with { Steps = [.. item.Steps, item.Steps[0]] },
            item with { Steps = [item.Steps[0] with { Checks = [item.Steps[0].Checks[0]] }, item.Steps[1]] }
        }) Assert.Equal(2, _policy.GetExitCode(report with { Cases = [changed] }));

        var stepOnly = Case(content: Content([], content.Steps));
        Assert.Equal(2, _policy.GetExitCode(Report(stepOnly) with { Cases = [stepOnly with { Steps = [stepOnly.Steps[0]] }] }));
    }

    [Fact]
    public void Report_recomputes_assertion_definition_and_matches_exact_action_profile_and_digest()
    {
        var item = Case(content: Content([Assertion()], [Step("approval", Assertion())]));
        var report = Report(item);
        var check = item.Checks[0];
        foreach (var changed in new[]
        {
            check with { Profile = "other/1" },
            check with { Operator = TestAssertionOperator.Contains },
            check with { Expected = DeterministicAssertionTests.Json("43") },
            check with { Subject = check.Subject with { MemberKey = "other" } },
            check with { DefinitionDigest = new string('a', 64) }
        }) Assert.Equal(2, _policy.GetExitCode(report with { Cases = [item with { Checks = [changed] }] }));

        var redefined = Assertion() with { Expected = DeterministicAssertionTests.Json("43") };
        var consistentButWrong = check with { Expected = redefined.Expected, DefinitionDigest = WorkflowTestPlanCapture.AssertionDigest(redefined) };
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [item with { Checks = [consistentButWrong] }] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [item with { Steps = [item.Steps[0] with { Profile = "other/1" }] }] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [item with { Steps = [item.Steps[0] with { ActionDigest = new string('a', 64) }] }] }));
    }

    [Fact]
    public void Failed_case_requires_an_executed_failure_and_truthful_failfast_placeholders()
    {
        var content = Content([Assertion("final")], [Step("approval", Assertion("approval-check")), Step("later", Assertion("later-check"))]);
        var item = Case(TestVerdict.Failed, content);
        var first = item.Steps[0];
        var later = item.Steps[1] with
        {
            State = TestStepState.NotStarted,
            Checks = [item.Steps[1].Checks[0] with { Verdict = TestCheckVerdict.NotEvaluated, Actual = new(TestEvidenceAvailability.Missing, null) }]
        };
        var report = Report(item with
        {
            Steps = [first, later],
            Checks = [item.Checks[0] with { Verdict = TestCheckVerdict.NotEvaluated, Actual = new(TestEvidenceAvailability.Missing, null) }]
        });
        Assert.Equal(1, _policy.GetExitCode(report));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0] with { Steps = [first, item.Steps[1] with { State = TestStepState.NotStarted }] }] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0] with { Steps = [first with { State = TestStepState.Skipped }, later] }] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0] with { Steps = [first with { State = TestStepState.Unavailable }, later] }] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0] with { Steps = [first with { Checks = [first.Checks[0] with { Actual = new(TestEvidenceAvailability.Unavailable, null) }] }, later] }] }));
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0] with { Steps = [first, later with { State = TestStepState.Executing }] }] }));
        Assert.Equal(1, _policy.GetExitCode(report with { Cases = [report.Cases[0] with { Steps = [first, later with { State = TestStepState.Settled }] }] }));
    }

    [Fact]
    public void Authorized_case_skip_can_omit_unevaluated_rows_but_cannot_hide_executed_checks()
    {
        var item = Case(TestVerdict.Skipped, Content([Assertion()], [Step("approval", Assertion())])) with
            { Required = false, SkipReason = "Explicit environment exclusion." };
        var report = Report(Case(), item) with { OptionalSkipsAllowed = true };
        Assert.Equal(0, _policy.GetExitCode(report));
        var executed = Case();
        Assert.Equal(2, _policy.GetExitCode(report with { Cases = [report.Cases[0], item with { Checks = executed.Checks }] }));
    }

    [Fact]
    public void Junit_keeps_repeated_case_selections_distinct_and_retains_original_case_id()
    {
        var report = Report(Case(), Case());
        using var output = new StringWriter();
        new WorkflowTestReportWriter([_policy]).WriteJunit(report, output);
        var cases = XDocument.Parse(output.ToString()).Root!.Elements("testcase").ToArray();
        Assert.Equal(report.Cases.Select(x => x.SelectionKey), cases.Select(x => (string)x.Attribute("name")!));
        Assert.Equal(2, cases.Select(x => (string)x.Attribute("name")!).Distinct(StringComparer.Ordinal).Count());
        Assert.All(cases, item => Assert.Contains(item.Descendants("property"), x =>
            (string?)x.Attribute("name") == "caseId" && (string?)x.Attribute("value") == "case"));
    }
}
