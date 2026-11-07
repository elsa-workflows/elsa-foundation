using System.Text.Json;
using System.Globalization;
using System.Numerics;
using Elsa.Workflows.Testing.Core.Contracts;
using Elsa.Workflows.Testing.Core.Models;

namespace Elsa.Workflows.Testing.Services;

internal sealed class WorkflowTestAssertionEvaluator : IWorkflowTestAssertionEvaluator
{
    public TestCheckResult Evaluate(TestAssertion assertion, TestSubjectObservation observation)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(assertion.Subject);
        string definitionDigest;
        try { definitionDigest = WorkflowTestPlanCapture.AssertionDigest(assertion); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or NotSupportedException)
        {
            return new(assertion.Key, assertion.Profile, assertion.Operator, "", TestCheckVerdict.Unavailable,
                assertion.Subject, assertion.Expected?.Clone(), observation.Evidence,
                "The assertion definition cannot be captured.");
        }
        if (assertion.Profile != "elsa.workflow-test-assertion/1" || !Enum.IsDefined(assertion.Operator) ||
            !Enum.IsDefined(assertion.Subject.Kind) || assertion.Subject.Kind == TestSubjectKind.Extension)
            return Result(TestCheckVerdict.Unavailable, "The assertion profile or operator is unavailable.");
        if (observation.Evidence.Availability != TestEvidenceAvailability.Available)
            return Result(TestCheckVerdict.Unavailable, "The required evidence is missing, withheld or unavailable.");
        if (assertion.Subject.Kind == TestSubjectKind.Interaction && !observation.Settled)
            return Result(TestCheckVerdict.Unavailable, "The interaction observation interval has not settled.");

        var interaction = assertion.Subject.Kind == TestSubjectKind.Interaction;
        if (interaction && observation.Evidence.Value is not { ValueKind: JsonValueKind.Array })
            return Result(TestCheckVerdict.Unavailable, "Interaction evidence requires the complete invocation collection.");
        var exists = interaction ? observation.Evidence.Value!.Value.GetArrayLength() != 0 : observation.Exists;

        if (assertion.Operator == TestAssertionOperator.Exists)
            return Decide(exists, "Expected the subject to exist.");
        if (assertion.Operator == TestAssertionOperator.Absent)
            return Decide(!exists, "Expected the subject to be absent.");
        if (!observation.Exists && !interaction)
            return Result(TestCheckVerdict.Failed, "The expected subject is absent.");
        if (assertion.Expected is not { ValueKind: not JsonValueKind.Undefined } expected)
            return Result(TestCheckVerdict.Unavailable, "This assertion requires an explicit expected value.");
        if (observation.Evidence.Value is not { ValueKind: not JsonValueKind.Undefined } actual)
            return Result(TestCheckVerdict.Unavailable, "The subject has no readable value.");
        if (assertion.Operator is TestAssertionOperator.Equals or TestAssertionOperator.Contains &&
            (!IsComparable(expected) || !IsComparable(actual)))
            return Result(TestCheckVerdict.Unavailable, "The comparison exceeds supported numeric/depth bounds or contains ambiguous object members.");

        return assertion.Operator switch
        {
            TestAssertionOperator.Equals => Decide(JsonEqual(expected, actual), "Expected and actual values differ."),
            TestAssertionOperator.Contains => Contains(expected, actual),
            TestAssertionOperator.CountEquals => Count(expected, actual),
            _ => Result(TestCheckVerdict.Unavailable, "The assertion operator is unavailable.")
        };

        TestCheckResult Decide(bool passed, string failure) => Result(passed ? TestCheckVerdict.Passed : TestCheckVerdict.Failed, passed ? null : failure);
        TestCheckResult Result(TestCheckVerdict verdict, string? message) =>
            new(assertion.Key, assertion.Profile, assertion.Operator, definitionDigest, verdict, assertion.Subject, assertion.Expected?.Clone(),
                observation.Evidence with { Value = observation.Evidence.Value?.Clone() }, message);

        TestCheckResult Contains(JsonElement wanted, JsonElement observed)
        {
            if (observed.ValueKind == JsonValueKind.Array)
                return Decide(observed.EnumerateArray().Any(value => JsonEqual(wanted, value)), "The expected array member was absent.");
            if (observed.ValueKind == JsonValueKind.String && wanted.ValueKind == JsonValueKind.String)
                return Decide(observed.GetString()!.Contains(wanted.GetString()!, StringComparison.Ordinal), "The expected text was absent.");
            return Result(TestCheckVerdict.Unavailable, "Contains requires an array or two text values.");
        }

        TestCheckResult Count(JsonElement wanted, JsonElement observed)
        {
            if (wanted.ValueKind != JsonValueKind.Number || !wanted.TryGetInt32(out var count) || count < 0)
                return Result(TestCheckVerdict.Unavailable, "CountEquals requires a nonnegative integer expectation.");
            var length = observed.ValueKind switch
            {
                JsonValueKind.Array => observed.GetArrayLength(),
                JsonValueKind.Object => observed.EnumerateObject().Count(),
                _ => -1
            };
            return length < 0
                ? Result(TestCheckVerdict.Unavailable, "CountEquals requires a collection observation.")
                : Decide(count == length, $"Expected {count} members; observed {length}.");
        }
    }

    private static bool JsonEqual(JsonElement left, JsonElement right, int depth = 0)
    {
        if (depth > 64 || left.ValueKind != right.ValueKind) return false;
        switch (left.ValueKind)
        {
            case JsonValueKind.Null: case JsonValueKind.True: case JsonValueKind.False: return true;
            case JsonValueKind.String: return StringComparer.Ordinal.Equals(left.GetString(), right.GetString());
            case JsonValueKind.Number:
                return NumberEqual(left.GetRawText(), right.GetRawText());
            case JsonValueKind.Array:
                return left.GetArrayLength() == right.GetArrayLength() &&
                       left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => JsonEqual(pair.First, pair.Second, depth + 1));
            case JsonValueKind.Object:
                var lp = left.EnumerateObject().ToArray();
                var rp = right.EnumerateObject().ToArray();
                if (lp.Length != rp.Length || lp.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != lp.Length ||
                    rp.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != rp.Length) return false;
                return lp.All(p => right.TryGetProperty(p.Name, out var value) && JsonEqual(p.Value, value, depth + 1));
            default: return false;
        }
    }

    private static bool NumberEqual(string left, string right)
    {
        // Normalize coefficient/exponent rather than rounding through decimal or double.
        return Normalize(left) == Normalize(right);

        static (bool Negative, string Digits, BigInteger Exponent) Normalize(string value)
        {
            var exponentIndex = value.IndexOfAny(['e', 'E']);
            var coefficient = exponentIndex < 0 ? value : value[..exponentIndex];
            var exponent = exponentIndex < 0 ? BigInteger.Zero : BigInteger.Parse(value[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var negative = coefficient[0] == '-';
            if (negative) coefficient = coefficient[1..];
            var point = coefficient.IndexOf('.');
            if (point >= 0)
            {
                exponent -= coefficient.Length - point - 1;
                coefficient = coefficient.Remove(point, 1);
            }
            coefficient = coefficient.TrimStart('0');
            if (coefficient.Length == 0) return (false, "0", BigInteger.Zero);
            var digits = coefficient.TrimEnd('0');
            exponent += coefficient.Length - digits.Length;
            return (negative, digits, exponent);
        }
    }

    private static bool IsComparable(JsonElement value, int depth = 0)
    {
        if (depth > 64) return false;
        if (value.ValueKind == JsonValueKind.Number) return value.GetRawText().Length <= 1024;
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(x => IsComparable(x, depth + 1));
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            return properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == properties.Length &&
                   properties.All(x => IsComparable(x.Value, depth + 1));
        }
        return value.ValueKind != JsonValueKind.Undefined;
    }
}
