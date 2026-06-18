using System.Text.Json;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogEntrySerializerTests
{
    private readonly StructuredLogEntrySerializer _serializer = new();

    [Fact]
    public void SerializesCamelCaseWithStringLevel()
    {
        var entry = TestEntries.Create(sequence: 42, level: LogLevel.Warning) with { MessageTemplate = "tmpl" };

        using var doc = JsonDocument.Parse(_serializer.Serialize(entry));
        var root = doc.RootElement;

        Assert.Equal(42, root.GetProperty("sequence").GetInt64());
        Assert.Equal("Warning", root.GetProperty("level").GetString());
        Assert.Equal("tmpl", root.GetProperty("messageTemplate").GetString());
    }

    [Fact]
    public void EmptyPropertiesAndScopesSerializeAsEmptyArrays()
    {
        using var doc = JsonDocument.Parse(_serializer.Serialize(TestEntries.Create()));
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Array, root.GetProperty("properties").ValueKind);
        Assert.Empty(root.GetProperty("properties").EnumerateArray());
        Assert.Empty(root.GetProperty("scopes").EnumerateArray());
    }

    [Fact]
    public void PopulatedPropertiesScopesAndExceptionSerialize()
    {
        var entry = TestEntries.Create() with
        {
            Properties = [new LogProperty("Name", "Value")],
            Scopes = [new LogScope([new LogProperty("Key", "V")])],
            Exception = new LogExceptionInfo("System.Exception", "boom")
        };

        using var doc = JsonDocument.Parse(_serializer.Serialize(entry));
        var root = doc.RootElement;

        Assert.Equal("Name", root.GetProperty("properties")[0].GetProperty("name").GetString());
        Assert.Equal("Key", root.GetProperty("scopes")[0].GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal("boom", root.GetProperty("exception").GetProperty("message").GetString());
    }

    [Fact]
    public void AbsentExceptionSerializesAsNull()
    {
        using var doc = JsonDocument.Parse(_serializer.Serialize(TestEntries.Create()));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("exception").ValueKind);
    }

    [Fact]
    public void SerializeManyProducesArray()
    {
        var json = _serializer.SerializeMany([TestEntries.Create(sequence: 1), TestEntries.Create(sequence: 2)]);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void SerializeDroppedUsesCamelCase()
    {
        var json = _serializer.SerializeDropped(new DroppedEntriesSignal(5, DateTimeOffset.UnixEpoch));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(5, doc.RootElement.GetProperty("droppedCount").GetInt64());
        Assert.True(doc.RootElement.TryGetProperty("since", out _));
    }
}
