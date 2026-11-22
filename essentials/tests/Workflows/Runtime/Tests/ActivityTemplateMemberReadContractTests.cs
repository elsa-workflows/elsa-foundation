using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ActivityTemplateMemberReadContractTests
{
    private static RuntimeReadSelection Selection(string[]? segments = null) =>
        new(1, segments ?? ["a.b", ""], RuntimeReadSelectionMode.Known, new("String"), true, false,
            ValueRepresentation.TextValue, "elsa.activity-value-json/1");

    private static ExecutableActivityTemplateMemberRead Read() => new(new("producer", "$result", null, true), Selection());

    [Fact]
    public void Legacy_overlay_bytes_and_its_reader_remain_unchanged()
    {
        var overlay = new ExecutableActivityTemplateOccurrenceOverlay("1", JsonSerializer.SerializeToElement(Array.Empty<object>()), null);
        var json = JsonSerializer.Serialize(overlay);
        Assert.Equal("{\"SchemaVersion\":\"1\",\"InputBindings\":[],\"Structure\":null}", json);
        Assert.Equal("1", JsonSerializer.Deserialize<LegacyOverlay>(json)!.SchemaVersion);
        Assert.Null(JsonSerializer.Deserialize<ExecutableActivityTemplateOccurrenceOverlay>(json)!.MemberReads);
    }

    [Fact]
    public void Actual_fact_bearing_wire_roundtrips_and_an_old_reader_refuses_its_schema()
    {
        var reads = new Dictionary<string, ExecutableActivityTemplateMemberRead> { ["input"] = Read() };
        var overlay = new ExecutableActivityTemplateOccurrenceOverlay("2", JsonSerializer.SerializeToElement(Array.Empty<object>()), null, null, reads);
        var json = JsonSerializer.Serialize(overlay);
        reads.Clear();
        var roundtrip = JsonSerializer.Deserialize<ExecutableActivityTemplateOccurrenceOverlay>(json)!;
        var read = Assert.Single(roundtrip.MemberReads!).Value;
        Assert.Equal("producer", read.Source.ProducerExecutableNodeId);
        Assert.True(read.Source.IsOptional);
        Assert.Equal(["a.b", ""], read.Selection.Segments);
        Assert.Equal(RuntimeReadSelectionMode.Known, read.Selection.Mode);
        Assert.True(read.Selection.MayBeAbsent);
        Assert.False(read.Selection.MayBeNull);
        Assert.Equal("elsa.activity-value-json/1", read.Selection.ProfileRevision);
        Assert.Single(overlay.MemberReads!);
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<LegacyOverlay>(json));
    }

    [Fact]
    public void Schema_fences_refuse_missing_empty_or_silently_ignorable_contracts()
    {
        var inputs = JsonSerializer.SerializeToElement(Array.Empty<object>());
        Assert.Throws<ArgumentException>(() => new ExecutableActivityTemplateOccurrenceOverlay("2", inputs, null));
        Assert.Throws<ArgumentException>(() => new ExecutableActivityTemplateOccurrenceOverlay("2", inputs, null, null, new Dictionary<string, ExecutableActivityTemplateMemberRead>()));
        Assert.Throws<ArgumentException>(() => new ExecutableActivityTemplateOccurrenceOverlay("1", inputs, null, null,
            new Dictionary<string, ExecutableActivityTemplateMemberRead> { ["input"] = Read() }));
        Assert.Throws<ArgumentException>(() => new ExecutableActivityTemplateMemberRead(new("producer", "$result", "scope"), Selection()));
        Assert.Throws<ArgumentException>(() => new ExecutableActivityTemplateMemberRead(new("producer", "$result", null),
            new(1, ["id"], RuntimeReadSelectionMode.DeferredFit)));
        Assert.Throws<ArgumentNullException>(() => new ExecutableActivityTemplateMemberRead(null!, Selection()));
        Assert.Throws<ArgumentNullException>(() => new ExecutableActivityTemplateMemberRead(new("producer", "$result", null), null!));
        Assert.Throws<ArgumentNullException>(() => new ExecutableActivityTemplateOccurrenceOverlay("2", inputs, null, null,
            new Dictionary<string, ExecutableActivityTemplateMemberRead> { ["input"] = null! }));
    }

    // The released four-argument schema-1 constructor: unknown JSON properties cannot bypass its fence.
    private sealed class LegacyOverlay
    {
        public LegacyOverlay(string schemaVersion, JsonElement inputBindings, ExecutableActivityStructure? structure, JsonElement? outputBindings = null)
        {
            if (schemaVersion != "1") throw new ArgumentException("Unsupported occurrence overlay schema.");
            SchemaVersion = schemaVersion;
            InputBindings = inputBindings;
            Structure = structure;
            OutputBindings = outputBindings;
        }
        public string SchemaVersion { get; }
        public JsonElement InputBindings { get; }
        public ExecutableActivityStructure? Structure { get; }
        public JsonElement? OutputBindings { get; }
    }
}
