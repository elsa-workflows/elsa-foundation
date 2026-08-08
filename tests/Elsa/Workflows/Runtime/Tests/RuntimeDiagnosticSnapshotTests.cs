using Elsa.ConsumerGuide.Testing;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDiagnosticSnapshotTests
{
    [Fact]
    public void Capture_ProducesBoundedObjectShape()
    {
        var snapshot = DefaultDiagnosticSnapshotFactory.Capture(new
        {
            id = "customer-1",
            tags = new[] { "one", "two", "three" }
        }, limits: new DiagnosticSnapshotLimits(MaxDepth: 4, MaxObjectProperties: 4, MaxArrayItems: 2, MaxStringLength: 10, MaxTotalBytes: 4096));

        Assert.Equal("object", snapshot.GetProperty("kind").GetString());
        var properties = snapshot.GetProperty("properties").EnumerateArray().ToArray();
        Assert.Contains(properties, property => property.GetProperty("name").GetString() == "id");
        var tags = properties.Single(property => property.GetProperty("name").GetString() == "tags").GetProperty("value");
        Assert.Equal("array", tags.GetProperty("kind").GetString());
        Assert.True(tags.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void Capture_RedactsSensitiveNames()
    {
        var snapshot = DefaultDiagnosticSnapshotFactory.Capture(new { password = "secret-value" });

        var password = snapshot
            .GetProperty("properties")
            .EnumerateArray()
            .Single(property => property.GetProperty("name").GetString() == "password")
            .GetProperty("value");

        Assert.Equal("redacted", password.GetProperty("kind").GetString());
        Assert.Equal("sensitive-name", password.GetProperty("reason").GetString());
        Assert.DoesNotContain("secret-value", snapshot.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_TruncatesLongStrings()
    {
        var snapshot = DefaultDiagnosticSnapshotFactory.Capture("abcdef", limits: new DiagnosticSnapshotLimits(MaxStringLength: 3));

        Assert.Equal("string", snapshot.GetProperty("kind").GetString());
        Assert.Equal("abc", snapshot.GetProperty("preview").GetString());
        Assert.True(snapshot.GetProperty("truncated").GetBoolean());
        Assert.Equal(6, snapshot.GetProperty("length").GetInt32());
    }

    /// <summary>
    /// Pins the published claim <c>evidence.value-preview-is-capped-and-flags-truncation</c> at the DEFAULT
    /// limit — the sibling test above proves the mechanism with an explicit limit, which does not tell a
    /// consumer what it will actually receive. An equality assertion against a captured value is only sound
    /// once <c>truncated</c> is false, and a benchmarked session lost a run to exactly that.
    /// </summary>
    [Fact]
    [ConsumerContract("evidence.value-preview-is-capped-and-flags-truncation")]
    public void Capture_caps_a_preview_at_the_default_limit_and_declares_the_truncation()
    {
        var snapshot = DefaultDiagnosticSnapshotFactory.Capture(new string('x', 300));

        Assert.Equal(DiagnosticSnapshotLimits.Default.MaxStringLength, snapshot.GetProperty("preview").GetString()!.Length);
        Assert.True(snapshot.GetProperty("truncated").GetBoolean());
        // The full length is still reported, so a consumer can tell "long value" from "value of 256 chars".
        Assert.Equal(300, snapshot.GetProperty("length").GetInt32());
    }

    [Fact]
    [ConsumerContract("evidence.value-preview-is-capped-and-flags-truncation")]
    public void Capture_of_a_value_within_the_limit_carries_it_whole()
    {
        var snapshot = DefaultDiagnosticSnapshotFactory.Capture("short value");

        Assert.Equal("short value", snapshot.GetProperty("preview").GetString());
        Assert.False(snapshot.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean());
    }

    [Fact]
    public void Capture_DoesNotEmitPayloadReferencesFromGenericSnapshotter()
    {
        var snapshot = DefaultDiagnosticSnapshotFactory.Capture(new { file = new byte[] { 1, 2, 3 } });

        Assert.DoesNotContain("payloadReference", snapshot.GetRawText(), StringComparison.Ordinal);
    }
}
