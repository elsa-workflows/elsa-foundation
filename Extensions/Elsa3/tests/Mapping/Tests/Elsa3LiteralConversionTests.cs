using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3LiteralConversionTests
{
    [Fact]
    public void Case_only_keys_are_rewritten_recursively_without_changing_dictionary_keys_or_carried_data()
    {
        var notes = new List<Elsa3LiteralConversion.Note>();
        var value = Parse("""{ "NAME":"Ada", "items":[{"CODE":"x"}], "byKey":{"OrderTotal":{"CODE":"y"}}, "extra":{ "n":1.0 } }""");
        var converted = Elsa3LiteralConversion.Rewrite(value, typeof(Order), new(), notes.Add);
        Assert.Equal("Ada", converted.GetProperty("name").GetString());
        Assert.Equal("x", converted.GetProperty("items")[0].GetProperty("code").GetString());
        Assert.Equal("y", converted.GetProperty("byKey").GetProperty("OrderTotal").GetProperty("code").GetString());
        Assert.Equal(value.GetProperty("extra").GetRawText(), converted.GetProperty("extra").GetRawText());
        Assert.Equal(3, notes.Count(note => note.Code == Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten));
        Assert.Equal("NAME", value.EnumerateObject().First().Name);
    }

    [Theory]
    [InlineData("{\"name\":\"first\",\"NAME\":\"second\"}")]
    [InlineData("{\"NAME\":\"first\",\"name\":\"second\"}")]
    public void A_rewrite_that_would_overwrite_an_exact_member_is_refused(string json)
    {
        var refusal = Assert.Throws<Elsa3LiteralConversion.LiteralRefusal>(() => Elsa3LiteralConversion.Rewrite(Parse(json), typeof(Order), new(), _ => { }));
        Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous, refusal.Code);
        Assert.Contains("exact JSON", refusal.Guidance);
    }

    [Fact]
    public void Scoped_names_and_explicit_json_names_define_the_rewrite_target()
    {
        var converted = Elsa3LiteralConversion.Rewrite(Parse("""{"ORDER_ID":"id","DISPLAY":"name"}"""), typeof(ScopedOrder), new(), _ => { });
        Assert.Equal("id", converted.GetProperty("order_id").GetString());
        Assert.Equal("name", converted.GetProperty("display").GetString());
    }

    [Theory]
    [InlineData("2026-01-15T09:15:00", Elsa3DateInterpretation.SourceTimeZone, "2026-01-15T09:15:00+01:00")]
    [InlineData("2026-07-15T09:15:00", Elsa3DateInterpretation.SourceTimeZone, "2026-07-15T09:15:00+02:00")]
    [InlineData("2026-01-15T09:15:00", Elsa3DateInterpretation.AssumeUtc, "2026-01-15T09:15:00Z")]
    [InlineData("2026-07-15T09:15:00", Elsa3DateInterpretation.AssumeUtc, "2026-07-15T09:15:00Z")]
    public void The_one_import_setting_establishes_the_date_specific_instant(string text, Elsa3DateInterpretation interpretation, string expected)
    {
        var notes = new List<Elsa3LiteralConversion.Note>();
        var options = Options(interpretation);
        var converted = Elsa3LiteralConversion.Rewrite(JsonSerializer.SerializeToElement(text, JsonBaseline.Options), typeof(DateTimeOffset), options, notes.Add);
        Assert.Equal(expected, converted.GetString());
        Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralDateInterpreted, Assert.Single(notes).Code);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, notes[0].Severity);
    }

    [Theory]
    [InlineData("2026-01-15T09:15:00", Elsa3DateInterpretation.Refuse)]
    [InlineData("2026-07-15T09:15:00", Elsa3DateInterpretation.Refuse)]
    [InlineData("2026-03-29T02:30:00", Elsa3DateInterpretation.SourceTimeZone)]
    [InlineData("2026-10-25T02:30:00", Elsa3DateInterpretation.SourceTimeZone)]
    public void Missing_or_ambiguous_instants_are_refused_with_import_guidance(string text, Elsa3DateInterpretation interpretation)
    {
        var refusal = Assert.Throws<Elsa3LiteralConversion.LiteralRefusal>(() => Elsa3LiteralConversion.Rewrite(
            JsonSerializer.SerializeToElement(text, JsonBaseline.Options), typeof(DateTime), Options(interpretation), _ => { }));
        Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralDateOffsetMissing, refusal.Code);
        Assert.Contains("SourceTimeZone", refusal.Guidance);
    }

    [Theory]
    [InlineData(typeof(DateTime), 1)]
    [InlineData(typeof(DateOnly), 1)]
    [InlineData(typeof(DateTimeOffset), 0)]
    public void An_explicit_offset_is_preserved_and_wall_clock_changes_are_visible(Type type, int warnings)
    {
        var notes = new List<Elsa3LiteralConversion.Note>();
        var value = Parse("\"2026-01-15T00:15:00+05:00\"");
        Assert.Equal(value.GetRawText(), Elsa3LiteralConversion.Rewrite(value, type, new(), notes.Add).GetRawText());
        Assert.Equal(warnings, notes.Count);
        Assert.All(notes, note => Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralDateWallClock, note.Code));
    }

    [Theory]
    [InlineData(Elsa3DateInterpretation.Refuse, false)]
    [InlineData(Elsa3DateInterpretation.SourceTimeZone, true)]
    [InlineData(Elsa3DateInterpretation.AssumeUtc, true)]
    public async Task Workflow_variable_defaults_use_the_feature_setting_and_record_it(Elsa3DateInterpretation interpretation, bool succeeds)
    {
        var definition = ConversionServices.Definition(Sequence("root"), variables:
            [new Elsa3Variable { Id = "at", Name = "at", TypeName = "DateTime", Value = "2026-07-15T09:15:00" }]);
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, feature: new Elsa3MappingFeature
        { DateInterpretation = interpretation, SourceTimeZone = interpretation == Elsa3DateInterpretation.SourceTimeZone ? "Europe/Amsterdam" : null });
        Assert.Equal(succeeds, result.Succeeded);
        if (!succeeds) Assert.Contains(result.Diagnostics, note => note.Code == Elsa3ActivityDiagnosticCodes.LiteralDateOffsetMissing);
        else
        {
            Assert.Single(result.Diagnostics.Where(note => note.Code == Elsa3.Mapping.Services.Elsa3ImportDiagnosticCodes.DateInterpretationApplied));
            var initial = Assert.IsType<JsonElement>(Assert.Single(result.Value!.State.Variables).Default!.Value);
            Assert.Equal(interpretation == Elsa3DateInterpretation.SourceTimeZone ? "2026-07-15T07:15:00Z" : "2026-07-15T09:15:00Z", initial.GetString());
        }
        Assert.Equal("2026-07-15T09:15:00", Assert.Single(definition.Variables).Value);
    }

    private static Elsa3ConversionOptions Options(Elsa3DateInterpretation interpretation) => new()
    { DateInterpretation = interpretation, SourceTimeZone = interpretation == Elsa3DateInterpretation.SourceTimeZone ? "Europe/Amsterdam" : null };
    private static JsonElement Parse(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private sealed record Item(string Code);
    private sealed record Order(string? Name, Item[]? Items, Dictionary<string, Item>? ByKey);
    [JsonConversion(MemberMappings = ["order_id=Id"])]
    private sealed record ScopedOrder(string Id, [property: JsonPropertyName("display")] string Name);
}
