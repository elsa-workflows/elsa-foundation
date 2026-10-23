using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The one definition of "the same post-commit intent", used by every store, enricher, and the checkpoint validator.
/// Payloads compare as JSON values because storage re-serializes them.
/// </summary>
public sealed class RuntimePostCommitIntentEquivalenceTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Payloads_that_differ_only_in_formatting_are_equivalent()
    {
        using var formatted = JsonDocument.Parse("{ \"name\": \"café\", \"count\": 1 }");
        var reserialized = JsonSerializer.SerializeToElement(formatted.RootElement);

        Assert.NotEqual(formatted.RootElement.GetRawText(), reserialized.GetRawText());
        Assert.True(Intent(formatted.RootElement).IsEquivalentTo(Intent(reserialized)));
    }

    [Fact]
    public void Payloads_with_different_values_are_not_equivalent()
    {
        using var first = JsonDocument.Parse("{\"count\":1}");
        using var second = JsonDocument.Parse("{\"count\":2}");

        Assert.False(Intent(first.RootElement).IsEquivalentTo(Intent(second.RootElement)));
    }

    [Fact]
    public void Absent_payloads_are_equivalent_but_an_absent_and_a_present_payload_are_not()
    {
        using var present = JsonDocument.Parse("{}");

        Assert.True(Intent(null).IsEquivalentTo(Intent(null)));
        Assert.False(Intent(null).IsEquivalentTo(Intent(present.RootElement)));
        Assert.False(Intent(present.RootElement).IsEquivalentTo(Intent(null)));
    }

    private static RuntimePostCommitIntent Intent(JsonElement? payload) =>
        new("intent-a", "workflow-a", "test.intent", RecordedAt, null, null, payload);
}
