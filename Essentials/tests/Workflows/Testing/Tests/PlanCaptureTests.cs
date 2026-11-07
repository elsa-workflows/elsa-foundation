using Elsa.Workflows.Testing.Core.Models;
using Elsa.Workflows.Testing.Services;
using Xunit;

namespace Elsa.Workflows.Testing.Tests;

public sealed class PlanCaptureTests
{
    private static TestAssertion Check(string? expected = "42") => new()
    {
        Key = "amount", Profile = "elsa.workflow-test-assertion/1", Operator = TestAssertionOperator.Equals,
        Subject = new() { Kind = TestSubjectKind.Output, NodeId = "selected", MemberKey = "amount" },
        Expected = expected is null ? null : DeterministicAssertionTests.Json(expected)
    };

    private static TestScenarioStep Step(string data = "{}") => new()
    {
        Key = "approval", Action = new("user-action/1", DeterministicAssertionTests.Json(data)),
        Wait = new("approval-node", "approval-wait/1"), Assertions = [Check()]
    };

    private static TestCaseContent Content(IReadOnlyList<TestAssertion> checks, IReadOnlyList<TestScenarioStep> steps) => new()
    {
        Name = "Reject order", Target = new() { Kind = TestTargetKind.Workflow, DefinitionId = "orders", ModelProfile = "native/1" },
        Assertions = checks, Steps = steps
    };

    [Fact]
    public void Capture_uses_authored_content_once_before_results_and_copies_ordered_members()
    {
        var checks = new List<TestAssertion> { Check() };
        var steps = new List<TestScenarioStep> { Step() };
        var plan = WorkflowTestPlanCapture.Capture(Content(checks, steps));
        checks.Add(Check() with { Key = "later" });
        steps.Add(Step() with { Key = "later" });
        Assert.Single(plan.Checks);
        Assert.Single(plan.Steps);
        Assert.Equal(WorkflowTestPlanCapture.AssertionDigest(Check()), plan.Checks[0].DefinitionDigest);
        Assert.Equal(WorkflowTestPlanCapture.ActionDigest(Step()), plan.Steps[0].ActionDigest);
    }

    [Fact]
    public void Canonical_definition_sorts_object_keys_but_preserves_arrays_numeric_spelling_and_presence()
    {
        Assert.Equal(WorkflowTestPlanCapture.AssertionDigest(Check("{\"b\":[1,2],\"a\":true}")),
            WorkflowTestPlanCapture.AssertionDigest(Check("{\"a\":true,\"b\":[1,2]}")));
        Assert.NotEqual(WorkflowTestPlanCapture.AssertionDigest(Check("[1,2]")), WorkflowTestPlanCapture.AssertionDigest(Check("[2,1]")));
        Assert.NotEqual(WorkflowTestPlanCapture.AssertionDigest(Check("1")), WorkflowTestPlanCapture.AssertionDigest(Check("1.0")));
        Assert.NotEqual(WorkflowTestPlanCapture.AssertionDigest(Check(null)), WorkflowTestPlanCapture.AssertionDigest(Check("null")));
        Assert.NotEqual(WorkflowTestPlanCapture.AssertionDigest(Check()), WorkflowTestPlanCapture.AssertionDigest(Check() with { Operator = TestAssertionOperator.Contains }));
        Assert.NotEqual(WorkflowTestPlanCapture.AssertionDigest(Check()), WorkflowTestPlanCapture.AssertionDigest(Check() with { Subject = Check().Subject with { MemberKey = "other" } }));
    }

    [Fact]
    public void Action_digest_includes_profile_payload_wait_selector_and_timeout()
    {
        var step = Step("{\"b\":2,\"a\":1}");
        Assert.Equal(WorkflowTestPlanCapture.ActionDigest(step), WorkflowTestPlanCapture.ActionDigest(Step("{\"a\":1,\"b\":2}")));
        foreach (var different in new[]
        {
            step with { Action = step.Action with { Profile = "other/1" } },
            step with { Action = step.Action with { Data = DeterministicAssertionTests.Json("{\"a\":2,\"b\":2}") } },
            step with { Wait = step.Wait! with { NodeId = "another" } },
            step with { Timeout = step.Timeout.Add(TimeSpan.FromSeconds(1)) }
        }) Assert.NotEqual(WorkflowTestPlanCapture.ActionDigest(step), WorkflowTestPlanCapture.ActionDigest(different));
    }

    [Fact]
    public void Evaluator_stamps_the_same_definition_and_profile_used_at_admission()
    {
        var authored = Check();
        var plan = WorkflowTestPlanCapture.Capture(Content([authored], []));
        var result = new WorkflowTestAssertionEvaluator().Evaluate(authored,
            new(new(TestEvidenceAvailability.Available, DeterministicAssertionTests.Json("42")), true, true));
        Assert.Equal(plan.Checks[0].DefinitionDigest, result.DefinitionDigest);
        Assert.Equal(plan.Checks[0].Profile, result.Profile);
        Assert.Equal(authored.Operator, result.Operator);
        Assert.True(WorkflowTestPlanCapture.MatchesDefinition(result));
        Assert.False(WorkflowTestPlanCapture.MatchesDefinition(result with { Expected = DeterministicAssertionTests.Json("43") }));
    }

    [Fact]
    public void Capture_rejects_duplicate_assertions_steps_and_ambiguous_json_definitions()
    {
        Assert.Throws<ArgumentException>(() => WorkflowTestPlanCapture.Capture(Content([Check(), Check()], [])));
        Assert.Throws<ArgumentException>(() => WorkflowTestPlanCapture.Capture(Content([], [Step(), Step()])));
        Assert.Throws<ArgumentException>(() => WorkflowTestPlanCapture.Capture(Content([], [Step() with { Assertions = [Check(), Check()] }])));
        Assert.Throws<ArgumentException>(() => WorkflowTestPlanCapture.AssertionDigest(Check("{\"a\":1,\"a\":2}")));
    }
}
