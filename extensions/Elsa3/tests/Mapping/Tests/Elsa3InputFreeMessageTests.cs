using System.Text.Json;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 1237 FR-019 (T028): a converter message or guidance names the cause and identities (definition, type, function
/// and member names) and positions, never input text: no literal value, script body, variable default or setting value.
/// Each input carries <see cref="Sentinel"/>; where the input must be valid to reach the site, a unique valid value is
/// used instead (<see cref="DateLiteral"/> in <see cref="Zone"/>), and the literal, its interpreted value and the zone id
/// must all be absent.
/// </summary>
public sealed class Elsa3InputFreeMessageTests
{
    private const string Sentinel = "SENTINEL-1237";

    /// <summary>A date literal without an offset, valid in <see cref="Zone"/>.</summary>
    private const string DateLiteral = "2031-07-13T04:05:06";

    /// <summary>What <see cref="DateLiteral"/> is interpreted as in <see cref="Zone"/> (July, Chatham Standard Time).</summary>
    private const string InterpretedOffset = "+12:45";

    private const string Zone = "Pacific/Chatham";

    private static readonly string[] DateInput = [DateLiteral, "2031-07-13", InterpretedOffset, Zone, "Chatham"];

    // ── Expression rewriter ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_strict_mode_parse_message_quotes_no_script()
    {
        var result = await ConvertExpressionAsync("JavaScript", $"with ({{ a: '{Sentinel}' }}) {{ a }}\n1");

        AssertRefusedWithout(result, Elsa3ImportDiagnosticCodes.UnsupportedShape, Sentinel);
    }

    [Fact]
    public async Task The_dotnet_usage_refusal_quotes_no_script()
    {
        var result = await ConvertExpressionAsync("JavaScript", $"'{Sentinel}'.ToUpper()");

        AssertRefusedWithout(result, Elsa3ImportDiagnosticCodes.DotNetMemberUsage, Sentinel);
    }

    [Fact]
    public async Task The_javascript_safe_lowering_parse_message_quotes_no_script()
    {
        var result = await ConvertExpressionAsync("JavaScript", $"'{Sentinel}' +");

        AssertRefusedWithout(result, Elsa3ImportDiagnosticCodes.UnsupportedShape, Sentinel);
    }

    [Fact]
    public async Task The_liquid_safe_lowering_parse_message_quotes_no_template()
    {
        var result = await ConvertExpressionAsync("Liquid", $"{{% if '{Sentinel}' %}}");

        AssertRefusedWithout(result, Elsa3ImportDiagnosticCodes.UnsupportedShape, Sentinel);
    }

    [Fact]
    public async Task The_toJson_arity_refusal_quotes_no_script()
    {
        var result = await ConvertExpressionAsync("JavaScript", $"toJson('{Sentinel}', 1)");

        AssertRefusedWithout(result, Elsa3ImportDiagnosticCodes.ForbiddenCapability, Sentinel);
    }

    [Fact]
    public async Task The_toJson_rewrite_warning_quotes_no_script()
    {
        var result = await ConvertExpressionAsync("JavaScript", $"toJson('{Sentinel}')");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ToJsonRewritten &&
                                                          diagnostic.Message.Contains("line 1, column 1", StringComparison.Ordinal));
        AssertFree(result, Sentinel);
    }

    [Theory]
    [InlineData(Elsa3DateInterpretation.Refuse)]
    [InlineData(Elsa3DateInterpretation.AssumeUtc)]
    public async Task A_zone_dependent_date_message_quotes_no_script(Elsa3DateInterpretation interpretation)
    {
        var result = await ConvertExpressionAsync("JavaScript", $"new Date('{Sentinel}').getHours()", interpretation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is Elsa3ImportDiagnosticCodes.ForbiddenCapability or Elsa3ImportDiagnosticCodes.DateAssumedUtc);
        AssertFree(result, Sentinel);
    }

    [Fact]
    public async Task Local_date_text_under_a_source_time_zone_names_neither_the_script_nor_the_zone()
    {
        var result = await ConvertExpressionAsync("JavaScript", $"new Date().toString() + '{Sentinel}'", Elsa3DateInterpretation.SourceTimeZone, Zone);

        AssertRefusedWithout(result, Elsa3ImportDiagnosticCodes.ForbiddenCapability, [Sentinel, .. DateInput]);
    }

    [Fact]
    public async Task The_locale_refusal_quotes_no_script()
    {
        var result = await ConvertExpressionAsync("JavaScript", $"'{Sentinel}'.localeCompare('b')");

        AssertRefusedWithout(result, Elsa3ImportDiagnosticCodes.ForbiddenCapability, Sentinel);
    }

    [Fact]
    public async Task The_script_date_interpretation_note_names_no_zone()
    {
        var result = await ConvertExpressionAsync("JavaScript", "new Date(2031, 6, 13, 4, 5, 6).getHours()", Elsa3DateInterpretation.SourceTimeZone, Zone);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateInterpretationApplied);
        AssertFree(result, DateInput);
    }

    // ── Literals and starting values ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_date_starting_value_is_interpreted_without_quoting_the_literal_its_instant_or_the_zone()
    {
        var variable = new Elsa3Variable { Id = "at", Name = "at", TypeName = "DateTime", Value = DateLiteral };

        var result = await ConvertAsync(Sequence("root"), [variable], Elsa3DateInterpretation.SourceTimeZone, Zone);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.LiteralDateInterpreted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateInterpretationApplied);
        AssertFree(result, DateInput);
        // What the literal became is the converted value, not part of any message.
        Assert.Equal("2031-07-12T15:20:06Z", Assert.IsType<JsonElement>(Assert.Single(result.Value!.State.Variables).Default!.Value).GetString());
    }

    [Fact]
    public async Task A_date_literal_on_an_activity_input_is_interpreted_without_quoting_the_literal_its_instant_or_the_zone()
    {
        var order = Activity("order", "Acme.JsonOrder", ("at", Literal(DateLiteral, "DateTime")));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.CreateWith(typeof(JsonOrderImportActivity).Assembly),
            ConversionServices.Definition(Sequence("root", order)),
            feature: new Elsa3MappingFeature { DateInterpretation = Elsa3DateInterpretation.SourceTimeZone, SourceTimeZone = Zone });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.LiteralDateInterpreted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateInterpretationApplied);
        AssertFree(result, DateInput);
    }

    [Theory]
    [InlineData("Int32", Sentinel)]
    [InlineData("List<String>", Sentinel + ", x")]
    [InlineData("List<Int32>", Sentinel + ", 2")]
    [InlineData("List<Int32>", "[\"" + Sentinel + "\"]")]
    [InlineData("ObjectDictionary", Sentinel)]
    [InlineData("Acme.Orders.Order, Acme", Sentinel)]
    public async Task A_refused_starting_value_is_not_echoed_and_no_guidance_is_built_from_it(string typeName, string value)
    {
        var variable = new Elsa3Variable { Id = "items", Name = "items", TypeName = typeName, Value = value };

        var result = await ConvertAsync(Sequence("root"), [variable]);

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.VariableValueUnconvertible, Sentinel);
    }

    // ── Activity members ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_switch_mode_message_quotes_no_literal()
    {
        var @switch = Activity("switch", "Elsa.Switch", ("mode", Literal(Sentinel)));

        var result = await ConvertAsync(Sequence("root", @switch));

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.UnsupportedMode, Sentinel);
    }

    [Theory]
    [InlineData("Literal", Sentinel)]
    [InlineData("JavaScript", "'" + Sentinel + "' +")]
    public async Task A_switch_case_condition_message_quotes_no_literal_or_script(string type, string condition)
    {
        var @switch = Activity("switch", "Elsa.Switch");
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new[]
        {
            new { label = "only", condition = new { expression = new { type, value = condition } } }
        });

        var result = await ConvertAsync(Sequence("root", @switch));

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.UnsupportedMode, Sentinel);
    }

    [Fact]
    public async Task The_fork_join_mode_message_quotes_no_value()
    {
        var fork = Activity("fork", "Elsa.Fork", ("joinMode", Literal(Sentinel)));
        fork.AdditionalProperties["branches"] = JsonSerializer.SerializeToElement(new[] { Port(Write("a")) });

        var result = await ConvertAsync(Sequence("root", fork));

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.UnsupportedMode, Sentinel);
    }

    [Fact]
    public async Task The_fork_branches_message_quotes_no_raw_text()
    {
        var fork = Activity("fork", "Elsa.FlowFork", ("branches", Literal($"[\"{Sentinel}\",")));
        var chart = Flowchart("root", Write("first"), fork, Write("b"));
        chart.Connections = [Connect("first", "Done", "fork"), Connect("fork", "x", "b")];

        var result = await ConvertAsync(chart);

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.UnsupportedMode, Sentinel);
    }

    [Fact]
    public async Task The_unreadable_outcomes_message_quotes_no_raw_text()
    {
        var complete = Activity("complete", "Elsa.Complete", ("outcomes", Literal($"[\"{Sentinel}\",")));

        var result = await ConvertAsync(Sequence("root", complete));

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.UnsupportedMode, Sentinel);
    }

    /// <summary>No built-in map entry uses the EnumNameToInt contract, so this map gives it to WriteHttpResponse's status.</summary>
    [Fact]
    public async Task The_http_status_literal_message_quotes_no_literal()
    {
        var response = Activity("response", "Elsa.WriteHttpResponse", ("statusCode", Literal(Sentinel)));

        var result = await ConvertAsync(Sequence("root", response), feature: new EnumNameToIntStatusFeature());

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.ValueOutsideContract, Sentinel);
    }

    [Fact]
    public async Task The_non_json_content_type_message_quotes_no_content_type()
    {
        var response = Activity("response", "Elsa.WriteHttpResponse",
            ("content", Expression("JavaScript", "return { id: 42 };", "Object")),
            ("contentType", Literal($"text/{Sentinel}")));

        var result = await ConvertAsync(Sequence("root", response));

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.ValueOutsideContract, Sentinel);
    }

    [Fact]
    public async Task The_clr_namespace_refusal_quotes_no_script()
    {
        var result = await ConvertExpressionAsync("JavaScript", $"System.Environment.GetEnvironmentVariable('{Sentinel}')");

        AssertRefusedWithout(result, Elsa3ImportDiagnosticCodes.DotNetMemberUsage, Sentinel);
    }

    [Fact]
    public async Task The_http_method_message_names_the_method_in_the_converters_spelling_only()
    {
        var request = Activity("request", "Elsa.SendHttpRequest", ("url", Literal("https://example.org")), ("method", Literal(" pAtCh ")));

        var result = await ConvertAsync(Sequence("root", request));

        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.NoCounterpart);
        Assert.Contains("PATCH", refusal.Message, StringComparison.Ordinal);
        AssertFree(result, "pAtCh");
    }

    // ── Exception pass-throughs ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unreadable_container_variables_pass_no_exception_text_through()
    {
        var root = Sequence("root", Write("a"));
        root.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "v", name = new[] { Sentinel } } });

        var result = await ConvertAsync(root);

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.UnknownMember, Sentinel);
    }

    [Fact]
    public async Task An_unreadable_child_activity_passes_no_exception_text_through()
    {
        var @if = Activity("if", "Elsa.If", ("then", JsonSerializer.SerializeToElement(new { id = new[] { Sentinel }, type = "Elsa.WriteLine" })));

        var result = await ConvertAsync(Sequence("root", @if));

        AssertRefusedWithout(result, Elsa3ActivityDiagnosticCodes.UnknownMember, Sentinel);
    }

    [Fact]
    public async Task An_unexpected_converter_failure_passes_no_exception_text_through()
    {
        var result = await ConvertAsync(Sequence("root", Write("a")), feature: new ThrowingMapFeature());

        AssertRefusedWithout(result, Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed, Sentinel);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void AssertRefusedWithout(Elsa3MigrationResult<Elsa3ConvertedDefinition> result, string code, params string[] forbidden)
    {
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        AssertFree(result, forbidden);
    }

    private static void AssertFree(Elsa3MigrationResult<Elsa3ConvertedDefinition> result, params string[] forbidden)
    {
        foreach (var diagnostic in result.Diagnostics)
        foreach (var text in forbidden)
        {
            Assert.DoesNotContain(text, diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(text, diagnostic.Guidance ?? string.Empty, StringComparison.Ordinal);
        }
    }

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertExpressionAsync(
        string language,
        string source,
        Elsa3DateInterpretation interpretation = Elsa3DateInterpretation.Refuse,
        string? zone = null) =>
        ConvertAsync(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression(language, source)))), [], interpretation, zone);

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3Activity root,
        IEnumerable<Elsa3Variable>? variables = null,
        Elsa3DateInterpretation interpretation = Elsa3DateInterpretation.Refuse,
        string? zone = null,
        Elsa3MappingFeature? feature = null) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables),
            feature: feature ?? new Elsa3MappingFeature { DateInterpretation = interpretation, SourceTimeZone = zone });

    private sealed class EnumNameToIntStatusFeature : Elsa3MappingFeature
    {
        public override void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IElsa3ActivityMap, EnumNameToIntStatusMap>();
            base.ConfigureServices(services);
        }
    }

    private sealed class EnumNameToIntStatusMap : IElsa3ActivityMap
    {
        public IReadOnlyList<Elsa3ActivityMapEntry> Entries { get; } = new Elsa3BuiltInActivityMap().Entries.Select(entry => entry with
        {
            Members = entry.Members.Select(rule => rule.Contract == Elsa3ValueContract.PositiveHttpResponseStatus
                ? rule with { Contract = Elsa3ValueContract.EnumNameToInt }
                : rule).ToArray()
        }).ToArray();

        public Elsa3ActivityMapEntry? Find(string type) => Entries.FirstOrDefault(entry => entry.Elsa3TypeName == type);
    }

    private sealed class ThrowingMapFeature : Elsa3MappingFeature
    {
        public override void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IElsa3ActivityMap, ThrowingMap>();
            base.ConfigureServices(services);
        }
    }

    /// <summary>A map that fails unexpectedly, with input text in its exception message.</summary>
    private sealed class ThrowingMap : IElsa3ActivityMap
    {
        public IReadOnlyList<Elsa3ActivityMapEntry> Entries { get; } = new Elsa3BuiltInActivityMap().Entries;

        public Elsa3ActivityMapEntry? Find(string type) => throw new InvalidOperationException($"Unexpected failure for {Sentinel}.");
    }
}
