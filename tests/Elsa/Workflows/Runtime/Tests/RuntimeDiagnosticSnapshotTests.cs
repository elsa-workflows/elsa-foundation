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

    [Fact]
    public void Capture_DoesNotEmitPayloadReferencesFromGenericSnapshotter()
    {
        var snapshot = DefaultDiagnosticSnapshotFactory.Capture(new { file = new byte[] { 1, 2, 3 } });

        Assert.DoesNotContain("payloadReference", snapshot.GetRawText(), StringComparison.Ordinal);
    }
}
