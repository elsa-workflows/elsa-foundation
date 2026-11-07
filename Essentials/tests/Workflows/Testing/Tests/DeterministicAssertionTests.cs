using System.Text.Json;
using Elsa.Workflows.Testing.Core.Models;
using Elsa.Workflows.Testing.Services;
using Xunit;

namespace Elsa.Workflows.Testing.Tests;

public class DeterministicAssertionTests
{
    private readonly WorkflowTestAssertionEvaluator _evaluator = new();
    internal static JsonElement Json(string content) { using var doc = JsonDocument.Parse(content); return doc.RootElement.Clone(); }

    private static TestAssertion Check(string expected, TestAssertionOperator op = TestAssertionOperator.Equals, TestSubjectKind subject = TestSubjectKind.Output) => new()
    {
        Key = "expected", Subject = new() { Kind = subject, NodeId = "selected", MemberKey = "total" }, Operator = op, Expected = Json(expected)
    };
    private static TestSubjectObservation Observed(string value, bool settled = true, bool exists = true) =>
        new(new(TestEvidenceAvailability.Available, Json(value)), exists, settled);

    [Theory]
    [InlineData("42", "43")]
    [InlineData("\"approved\"", "\"rejected\"")]
    [InlineData("null", "0")]
    [InlineData("[1,2]", "[2,1]")]
    [InlineData("{\"total\":42}", "{\"total\":43}")]
    [InlineData("1e-40", "0")]
    [InlineData("1.00000000000000000000000000001", "1.00000000000000000000000000002")]
    public void Incorrect_business_value_fails_even_when_readable(string expected, string actual) =>
        Assert.Equal(TestCheckVerdict.Failed, _evaluator.Evaluate(Check(expected), Observed(actual)).Verdict);

    [Theory]
    [InlineData("42", "42.00")]
    [InlineData("1000", "1e3")]
    [InlineData("-0", "0.000")]
    [InlineData("0.00000000000000000000000000001", "1e-29")]
    [InlineData("{\"a\":1,\"b\":[true,null]}", "{\"b\":[true,null],\"a\":1}")]
    public void Equivalent_values_compare_without_property_order_or_numeric_rounding(string expected, string actual) =>
        Assert.Equal(TestCheckVerdict.Passed, _evaluator.Evaluate(Check(expected), Observed(actual)).Verdict);

    [Theory]
    [InlineData(TestEvidenceAvailability.Missing)]
    [InlineData(TestEvidenceAvailability.Withheld)]
    [InlineData(TestEvidenceAvailability.Unavailable)]
    public void Missing_or_withheld_evidence_never_satisfies_explicit_null(TestEvidenceAvailability availability)
    {
        var result = _evaluator.Evaluate(Check("null"), new(new(availability, null), false, true));
        Assert.Equal(TestCheckVerdict.Unavailable, result.Verdict);
        Assert.Equal(TestCheckVerdict.Passed, _evaluator.Evaluate(Check("null"), Observed("null")).Verdict);
    }

    [Fact]
    public void Non_invocation_requires_settlement_and_counts_attempted_calls()
    {
        var assertion = Check("0", TestAssertionOperator.CountEquals, TestSubjectKind.Interaction);
        Assert.Equal(TestCheckVerdict.Unavailable, _evaluator.Evaluate(assertion, Observed("[]", settled: false)).Verdict);
        Assert.Equal(TestCheckVerdict.Passed, _evaluator.Evaluate(assertion, Observed("[]")).Verdict);
        Assert.Equal(TestCheckVerdict.Failed, _evaluator.Evaluate(assertion, Observed("[{\"disposition\":\"refused\"}]")).Verdict);
    }

    [Fact]
    public void Known_absence_can_pass_but_lost_evidence_cannot()
    {
        var absent = Check("null", TestAssertionOperator.Absent, TestSubjectKind.Interaction);
        Assert.Equal(TestCheckVerdict.Passed, _evaluator.Evaluate(absent, Observed("[]", exists: false)).Verdict);
        Assert.Equal(TestCheckVerdict.Failed, _evaluator.Evaluate(absent, Observed("[1]")).Verdict);
        Assert.Equal(TestCheckVerdict.Unavailable, _evaluator.Evaluate(absent, Observed("[]", settled: false, exists: false)).Verdict);
    }

    [Fact]
    public void Empty_interaction_collection_has_consistent_absence_count_and_equality()
    {
        var noCalls = Observed("[]", exists: false);
        Assert.Equal(TestCheckVerdict.Passed, _evaluator.Evaluate(Check("null", TestAssertionOperator.Absent, TestSubjectKind.Interaction), noCalls).Verdict);
        Assert.Equal(TestCheckVerdict.Passed, _evaluator.Evaluate(Check("0", TestAssertionOperator.CountEquals, TestSubjectKind.Interaction), noCalls).Verdict);
        Assert.Equal(TestCheckVerdict.Passed, _evaluator.Evaluate(Check("[]", subject: TestSubjectKind.Interaction), noCalls).Verdict);
        Assert.Equal(TestCheckVerdict.Failed, _evaluator.Evaluate(Check("null", TestAssertionOperator.Absent, TestSubjectKind.Interaction), Observed("[{\"disposition\":\"refused\"}]", exists: false)).Verdict);
        Assert.Equal(TestCheckVerdict.Unavailable, _evaluator.Evaluate(Check("null", TestAssertionOperator.Absent, TestSubjectKind.Interaction), Observed("null", exists: false)).Verdict);
    }

    [Fact]
    public void Unknown_profiles_and_missing_expected_values_are_unavailable()
    {
        Assert.Equal(TestCheckVerdict.Unavailable, _evaluator.Evaluate(Check("42") with { Profile = "unknown/1" }, Observed("42")).Verdict);
        Assert.Equal(TestCheckVerdict.Unavailable, _evaluator.Evaluate(Check("42") with { Expected = null }, Observed("42")).Verdict);
        Assert.Equal(TestCheckVerdict.Unavailable, _evaluator.Evaluate(Check("42", subject: TestSubjectKind.Extension), Observed("42")).Verdict);
    }

    [Fact]
    public void Contains_is_ordinal_and_preserves_json_types()
    {
        Assert.Equal(TestCheckVerdict.Passed, _evaluator.Evaluate(Check("\"Reject\"", TestAssertionOperator.Contains), Observed("\"Reject order\"")).Verdict);
        Assert.Equal(TestCheckVerdict.Failed, _evaluator.Evaluate(Check("\"reject\"", TestAssertionOperator.Contains), Observed("\"Reject order\"")).Verdict);
        Assert.Equal(TestCheckVerdict.Failed, _evaluator.Evaluate(Check("\"42\"", TestAssertionOperator.Contains), Observed("[42]")).Verdict);
    }

    [Fact]
    public void Unsupported_numeric_precision_is_unavailable_instead_of_a_false_regression()
    {
        var longOne = "1." + new string('0', 1100);
        Assert.Equal(TestCheckVerdict.Unavailable, _evaluator.Evaluate(Check(longOne), Observed("1")).Verdict);
        Assert.Equal(TestCheckVerdict.Unavailable, _evaluator.Evaluate(Check("1"), Observed("[" + longOne + "]") with { Exists = true }).Verdict);
    }
}
