using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Testing.Core.Models;

namespace Elsa.Workflows.Testing.Services;

/// <summary>Captures expected coverage from immutable authored content. Result rows are never a plan source.</summary>
internal static class WorkflowTestPlanCapture
{
    internal static TestCasePlan Capture(TestCaseContent capturedContent)
    {
        ArgumentNullException.ThrowIfNull(capturedContent);
        RequireUnique(capturedContent.Steps.Select(x => x.Key));
        return new(CaptureChecks(capturedContent.Assertions), Array.AsReadOnly(capturedContent.Steps.Select(step =>
            new TestStepPlan(step.Key, step.Action.Profile, ActionDigest(step), CaptureChecks(step.Assertions))).ToArray()));
    }

    internal static string AssertionDigest(TestAssertion assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        ArgumentNullException.ThrowIfNull(assertion.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(assertion.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(assertion.Profile);
        return Digest(JsonSerializer.SerializeToElement(new
        {
            schema = "elsa.workflow-test-assertion-definition/1",
            assertion.Key,
            assertion.Profile,
            assertion.Subject,
            assertion.Operator,
            expectedPresent = assertion.Expected.HasValue,
            expected = assertion.Expected
        }));
    }

    internal static bool MatchesDefinition(TestCheckResult result)
    {
        try
        {
            return IsDigest(result.DefinitionDigest) && result.DefinitionDigest == AssertionDigest(new()
            {
                Key = result.Key, Profile = result.Profile, Operator = result.Operator,
                Subject = result.Subject, Expected = result.Expected
            });
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    internal static string ActionDigest(TestScenarioStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(step.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(step.Action.Profile);
        return Digest(JsonSerializer.SerializeToElement(new
        {
            schema = "elsa.workflow-test-step-action/1",
            step.Key,
            step.Action,
            step.Wait,
            timeoutTicks = step.Timeout.Ticks
        }));
    }

    internal static bool IsDigest(string? digest) => digest is { Length: 64 } &&
        digest.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static IReadOnlyList<TestCheckPlan> CaptureChecks(IReadOnlyList<TestAssertion> assertions)
    {
        RequireUnique(assertions.Select(x => x.Key));
        return Array.AsReadOnly(assertions.Select(x => new TestCheckPlan(x.Key, x.Profile, AssertionDigest(x))).ToArray());
    }

    private static void RequireUnique(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (!seen.Add(key)) throw new ArgumentException("The captured plan contains duplicate keys.");
        }
    }

    private static string Digest(JsonElement value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(writer, value, 0);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value, int depth)
    {
        if (depth > 64) throw new ArgumentException("The captured definition exceeds supported JSON depth.");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new ArgumentException("The captured definition contains ambiguous object members.");
                writer.WriteStartObject();
                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText()); // Preserve authored numeric spelling; do not round.
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new ArgumentException("An undefined JSON value cannot be captured.");
        }
    }
}
