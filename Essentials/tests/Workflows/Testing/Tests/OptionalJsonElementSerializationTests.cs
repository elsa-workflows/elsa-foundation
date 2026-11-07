using System.Text.Json;
using Elsa.Workflows.Testing.Core.Models;
using Elsa.Workflows.Testing.Services;
using Xunit;

namespace Elsa.Workflows.Testing.Tests;

public sealed class OptionalJsonElementSerializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Case_and_step_assertions_keep_expected_presence_and_definition_digest_after_ordinary_JSON_roundtrip(bool web)
    {
        var missing = Assertion("missing", null);
        var explicitNull = Assertion("null", DeterministicAssertionTests.Json("null"));
        var content = Content([missing, explicitNull], [Step([missing, explicitNull])]);
        var options = Options(web);
        var json = JsonSerializer.Serialize(content, options);
        using var document = JsonDocument.Parse(json);
        var assertions = document.RootElement.GetProperty(Name("Assertions", web));
        Assert.False(assertions[0].TryGetProperty(Name("Expected", web), out _));
        Assert.Equal(JsonValueKind.Null, assertions[1].GetProperty(Name("Expected", web)).ValueKind);

        var restored = JsonSerializer.Deserialize<TestCaseContent>(json, options)!;
        Assert.Null(restored.Assertions[0].Expected);
        Assert.Equal(JsonValueKind.Null, restored.Assertions[1].Expected!.Value.ValueKind);
        Assert.Null(restored.Steps[0].Assertions[0].Expected);
        Assert.Equal(JsonValueKind.Null, restored.Steps[0].Assertions[1].Expected!.Value.ValueKind);
        var before = WorkflowTestPlanCapture.Capture(content);
        var after = WorkflowTestPlanCapture.Capture(restored);
        Assert.Equal(before.Checks, after.Checks);
        Assert.Equal(before.Steps[0].Checks, after.Steps[0].Checks);
        Assert.Equal(before.Steps[0].ActionDigest, after.Steps[0].ActionDigest);
        Assert.NotEqual(after.Checks[0].DefinitionDigest, after.Checks[1].DefinitionDigest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Report_roundtrip_keeps_available_null_authoritative_and_missing_evidence_distinct(bool web)
    {
        var explicitNull = Assertion("null", DeterministicAssertionTests.Json("null"));
        var exists = Assertion("exists", null) with { Operator = TestAssertionOperator.Exists };
        var content = Content([explicitNull, exists], [Step([explicitNull])]);
        var evaluator = new WorkflowTestAssertionEvaluator();
        var observation = new TestSubjectObservation(new(TestEvidenceAvailability.Available, DeterministicAssertionTests.Json("null")), true, true);
        var checks = content.Assertions.Select(check => evaluator.Evaluate(check, observation)).ToArray();
        Assert.All(checks, check => Assert.Equal(TestCheckVerdict.Passed, check.Verdict));
        var plan = WorkflowTestPlanCapture.Capture(content);
        var reference = new TestRevisionReference("case", "revision", "fingerprint");
        var report = new TestRunReport
        {
            RunId = "run", RequestFingerprint = "request", MaterialFingerprint = "material",
            Lifecycle = TestRunLifecycle.Finalized, ObservationSequence = 1, ObservedAt = DateTimeOffset.UnixEpoch,
            Selections = [new("selection", reference, plan)],
            Cases = [new()
            {
                AttemptId = "attempt", SelectionKey = "selection", Case = reference,
                Candidate = new() { DefinitionId = "workflow", SourceId = "source", SourceFingerprint = "source-fingerprint",
                    ArtifactId = "artifact", ArtifactFingerprint = "artifact-fingerprint", ModelProfile = "native/1" },
                Target = content.Target, Mode = content.Mode, Time = content.Time, Verdict = TestVerdict.Passed,
                Execution = new("native/1", "Completed"), Closure = new(TestClosureState.Closed, true, []), Checks = checks,
                Steps = [new("step", content.Steps[0].Action.Profile, plan.Steps[0].ActionDigest, TestStepState.Settled, 1,
                    [evaluator.Evaluate(explicitNull, observation)])]
            }]
        };
        var policy = new WorkflowTestReportPolicy();
        Assert.Equal(0, policy.GetExitCode(report));
        var options = Options(web);
        var restored = JsonSerializer.Deserialize<TestRunReport>(JsonSerializer.Serialize(report, options), options)!;
        var restoredChecks = restored.Cases[0].Checks;
        Assert.Equal(JsonValueKind.Null, restoredChecks[0].Expected!.Value.ValueKind);
        Assert.Null(restoredChecks[1].Expected);
        Assert.All(restoredChecks, check =>
        {
            Assert.Equal(TestEvidenceAvailability.Available, check.Actual.Availability);
            Assert.Equal(JsonValueKind.Null, check.Actual.Value!.Value.ValueKind);
            Assert.True(WorkflowTestPlanCapture.MatchesDefinition(check));
        });
        Assert.Equal(JsonValueKind.Null, restored.Cases[0].Steps[0].Checks[0].Actual.Value!.Value.ValueKind);
        Assert.Equal(0, policy.GetExitCode(restored));

        var missing = new TestObservationValue(TestEvidenceAvailability.Missing, null, "No observation");
        var missingJson = JsonSerializer.Serialize(missing, options);
        using var document = JsonDocument.Parse(missingJson);
        Assert.False(document.RootElement.TryGetProperty(Name("Value", web), out _));
        var restoredMissing = JsonSerializer.Deserialize<TestObservationValue>(missingJson, options)!;
        Assert.Equal(missing, restoredMissing);
        Assert.Equal(TestCheckVerdict.Unavailable, evaluator.Evaluate(explicitNull, new(restoredMissing, false, true)).Verdict);
    }

    private static TestAssertion Assertion(string key, JsonElement? expected) => new()
    {
        Key = key, Subject = new() { Kind = TestSubjectKind.Output, NodeId = "selected", MemberKey = "value" },
        Operator = TestAssertionOperator.Equals, Expected = expected
    };

    private static TestScenarioStep Step(IReadOnlyList<TestAssertion> assertions) => new()
    {
        Key = "step", Action = new("test-action/1", DeterministicAssertionTests.Json("{}")), Assertions = assertions
    };

    private static TestCaseContent Content(IReadOnlyList<TestAssertion> assertions, IReadOnlyList<TestScenarioStep> steps) => new()
    {
        Name = "Null value", Target = new() { Kind = TestTargetKind.Workflow, DefinitionId = "workflow", ModelProfile = "native/1" },
        Assertions = assertions, Steps = steps
    };

    private static JsonSerializerOptions Options(bool web) => new(web ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General);
    private static string Name(string name, bool web) => web ? JsonNamingPolicy.CamelCase.ConvertName(name) : name;
}
