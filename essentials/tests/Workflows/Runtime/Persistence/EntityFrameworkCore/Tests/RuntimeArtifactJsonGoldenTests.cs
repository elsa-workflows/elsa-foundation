using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// T021 / FR-023: the runtime store's byte-level golden fixture. The store writer moved from the Web defaults to the
/// JSON baseline before the first stable release. Fixed rows show both directions keep the same meaning, a carried
/// data value keeps its text, and a stored offset-less <see cref="DateTime"/> is refused when read into a typed value.
/// If these fail, regenerate the goldens and migrations in place: no upcaster, family-version bump or run-time check.
/// </summary>
public sealed class RuntimeArtifactJsonGoldenTests
{
    private const string Data = """{ "text" : "\u0061 <+>", "amount" : 1.0, "big" : 9007199254740993, "nil" : null, "at" : "2026-10-28T09:15:00" }""";

    // The Web-default writer re-emitted carried data: minified and escaped with the default encoder.
    private const string PreviousData = """{"text":"a \u003C\u002B\u003E","amount":1.0,"big":9007199254740993,"nil":null,"at":"2026-10-28T09:15:00"}""";

    // Identity strings are framed UTF-16 (base64) by the store's lossless converter in both formats.
    private const string RowStart = """{"durableValueId":"dgBhAGwAdQBlAC0AMQA=","workflowExecutionId":"dwBvAHIAawBmAGwAbwB3AC0AMQA=","valueId":"UgBlAG4A6QA=","type":{"kind":"agBzAG8AbgA=","id":null,"schema":null},"lifecycle":"Instance","storage":"Inline","inlineValue":""";
    private const string RowEnd = ""","externalReference":null,"sourceActivityExecutionId":null,"capturedAt":"2026-10-28T09:15:00+05:00","metadata":{"cwBvAHUAcgBjAGUA":"ZwBvAGwAZABlAG4A"}}""";

    // A row in today's (pre-S-9) format and the same row as the current writer stores it, byte for byte.
    private const string PreviousRow = RowStart + PreviousData + RowEnd;
    private const string CurrentRow = RowStart + Data + RowEnd;

    private static readonly JsonSerializerOptions PreviousReader = new(JsonSerializerDefaults.Web);

    private static DurableValueState Expected() => new("value-1", "workflow-1", "René", new("json", null, null),
        DurableValueLifecycle.Instance, DurableValueStorage.Inline, JsonSerializer.Deserialize<JsonElement>(Data),
        null, null, new DateTimeOffset(2026, 10, 28, 9, 15, 0, TimeSpan.FromHours(5)), new Dictionary<string, string> { ["source"] = "golden" });

    [Fact]
    public void The_current_writer_produces_the_golden_row_byte_for_byte() =>
        Assert.Equal(CurrentRow, RuntimeArtifactJson.Serialize(Expected()));

    [Fact]
    public void The_previous_writer_differs_only_by_re_emitting_the_carried_data()
    {
        Assert.Equal(PreviousData, JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(Data), PreviousReader));
        using var previous = JsonDocument.Parse(PreviousRow);
        using var current = JsonDocument.Parse(CurrentRow);
        Assert.True(JsonCanonicalForm.Equals(previous.RootElement, current.RootElement));
    }

    [Theory]
    [InlineData(PreviousRow, PreviousData)]
    [InlineData(CurrentRow, Data)]
    public void The_current_reader_reads_both_rows_with_the_same_meaning_and_keeps_their_data_text(string row, string data)
    {
        var read = RuntimeArtifactJson.Deserialize<DurableValueState>(row);
        var expected = Expected();
        Assert.Equal(expected.DurableValueId, read.DurableValueId);
        Assert.Equal(expected.WorkflowExecutionId, read.WorkflowExecutionId);
        Assert.Equal(expected.ValueId, read.ValueId);
        Assert.Equal(expected.Type, read.Type);
        Assert.Equal(expected.Lifecycle, read.Lifecycle);
        Assert.Equal(expected.Storage, read.Storage);
        Assert.Equal(expected.CapturedAt, read.CapturedAt);
        Assert.Equal(expected.CapturedAt.Offset, read.CapturedAt.Offset);
        Assert.Equal("golden", read.Metadata["source"]);
        Assert.Equal(data, read.InlineValue!.Value.GetRawText());
        Assert.True(JsonCanonicalForm.Equals(expected.InlineValue!.Value, read.InlineValue!.Value));
    }

    [Fact]
    public void The_previous_reader_options_read_the_current_row_with_the_same_meaning()
    {
        using var current = JsonDocument.Parse(CurrentRow);
        var root = current.RootElement;
        Assert.Equal("Inline", root.GetProperty("storage").Deserialize<string>(PreviousReader));
        Assert.Equal(Expected().CapturedAt, root.GetProperty("capturedAt").Deserialize<DateTimeOffset>(PreviousReader));
        Assert.Equal(TimeSpan.FromHours(5), root.GetProperty("capturedAt").Deserialize<DateTimeOffset>(PreviousReader).Offset);
        var data = root.GetProperty("inlineValue").Deserialize<JsonElement>(PreviousReader);
        Assert.True(JsonCanonicalForm.Equals(JsonSerializer.Deserialize<JsonElement>(Data), data));
    }

    [Fact]
    public void A_stored_offset_less_DateTime_is_kept_but_refused_when_read_into_a_typed_value()
    {
        var stored = RuntimeArtifactJson.Deserialize<DurableValueState>(CurrentRow).InlineValue!.Value.GetProperty("at");
        Assert.Equal("\"2026-10-28T09:15:00\"", stored.GetRawText());
        Assert.ThrowsAny<Exception>(() => JsonConversionContract.For(typeof(DateTime)).Read(stored, typeof(DateTime)));
        Assert.ThrowsAny<Exception>(() => JsonConversionContract.For(typeof(DateTimeOffset)).Read(stored, typeof(DateTimeOffset)));
    }
}
