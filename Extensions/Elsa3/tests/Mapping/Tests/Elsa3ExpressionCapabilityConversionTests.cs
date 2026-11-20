using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 1223 (S-1): Elsa 3 time, GUID, Base64 and JSON functions convert faithfully (US2), local date and time follows
/// the converter's date interpretation (FR-021 to FR-024, FR-034), locale-dependent calls need a locale (FR-033), and
/// relied-on live .NET objects are refused with VF-IMP-012 (US3). Quickstart C and D, E18 to E24.
/// </summary>
public sealed class Elsa3ExpressionCapabilityConversionTests
{
    // --- Faithful conversions (FR-014, FR-021, quickstart C13, C14) -----------------------------------------------

    [Theory]
    [InlineData("Date.now()")]
    [InlineData("new Date().toISOString()")]
    [InlineData("new Date().getTime()")]
    [InlineData("Date.UTC(2026, 9, 28)")]
    [InlineData("new Date(0).toJSON()")]
    [InlineData("new Date('2026-10-28')")]
    [InlineData("new Date('2026-10-28T09:15:00Z').getUTCHours()")]
    [InlineData("new Date('2026-10-28T09:15:00+01:00').getTime()")]
    [InlineData("Date.parse('2026-10-28T09:15:00Z')")]
    [InlineData("new Date(Date.now() - 1000).toISOString()")]
    [InlineData("new Date(new Date().toISOString()).getTime()")]
    [InlineData("new Date(Date.now() + 3600000).toISOString()")]
    [InlineData("new Date(new Date().getTime() + 60 * 1000).toISOString()")]
    [InlineData("'id-' + Date.now()")]
    [InlineData("(function () { var ms = 1000; return new Date(Date.now() + ms).getTime(); })()")]
    [InlineData("(function () { var d = new Date(); d = d.getTime(); return 'ts=' + d; })()")]
    [InlineData("(function () { var now = new Date(); now = now.toISOString(); return 'Created ' + now; })()")]
    [InlineData("(function () { var d; if (Math.random() > 2) d = new Date(); d = d ? d.getTime() : 0; return 'ms ' + d; })()")]
    [InlineData("(function () { function f() { var d = new Date(); return d.getTime(); } var d = new Date(); d = d.getTime(); return 'ms ' + d + f(); })()")]
    [InlineData("(function () { var d = new Date(); function fmt() { var d = 5; return 'n' + d; } return fmt() + d.toISOString(); })()")]
    [InlineData("(function () { var d = new Date(); for (d = d.getTime(); d < 0;) {} return 'ms ' + d; })()")]
    [InlineData("(function () { var d = new Date(); var stamp = d.toISOString(); var d = 'done'; return d + '!' + stamp; })()")]
    [InlineData("(function () { var d = new Date(); var t = d.getTime(); for (var d of ['a']) {} return d.toString() + t; })()")]
    [InlineData("(function () { var d = 'x'; class C { static { var d = new Date(); } } return d + '!'; })()")]
    [InlineData("(function () { function a() { var d = new Date(); return new Date(d).toISOString(); } function b() { var d = 1; return d; } return a() + b(); })()")]
    [InlineData("(function () { var d = new Date(); var d; return new Date(d).toISOString(); })()")]
    [InlineData("new Date(5000) - new Date(1000)")]
    [InlineData("Math.random()")]
    [InlineData("newGuid()")]
    [InlineData("newGuidString()")]
    [InlineData("getGuidString()")]
    [InlineData("newShortGuid()")]
    [InlineData("getShortGuid()")]
    [InlineData("parseGuid('6F9619FF-8B86-D011-B42D-00CF4FC964FF')")]
    [InlineData("stringToBase64('a')")]
    [InlineData("stringFromBase64('YQ==')")]
    [InlineData("'id-' + newGuid()")]
    [InlineData("newGuid().toString()")]
    [InlineData("isNullOrEmpty('')")]
    [InlineData("(5).toLocaleString('nl-NL')")]
    [InlineData("'a'.localeCompare('b', 'nl-NL')")]
    public async Task Faithful_constructs_convert_under_the_default_refuse(string expression)
    {
        var result = await ConvertAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity != Elsa3MigrationDiagnosticSeverity.Info &&
                                                                diagnostic.Code != Elsa3ImportDiagnosticCodes.ToJsonRewritten);
    }

    [Fact]
    public async Task The_guid_and_base64_built_ins_return_elsa3s_text()
    {
        Assert.Equal("6f9619ff-8b86-d011-b42d-00cf4fc964ff",
            (await EvaluateAsync("parseGuid('{6F9619FF-8B86-D011-B42D-00CF4FC964FF}')")).GetString());
        Assert.Equal("w6lt4oKs8J+Ygg==", (await EvaluateAsync("stringToBase64('\\u00e9m\\u20ac\\ud83d\\ude02')")).GetString());
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", (await EvaluateAsync("newGuid()")).GetString());
        Assert.Matches("^[A-Za-z0-9]{1,22}$", (await EvaluateAsync("newShortGuid()")).GetString());
    }

    [Fact]
    public async Task ToJson_is_rewritten_to_json_stringify_with_a_behaviour_warning()
    {
        var result = await ConvertAsync("toJson({ a: '<b>' })");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains("JSON.stringify({ a: '<b>' })", ReadExpression(result).Source, StringComparison.Ordinal);
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ToJsonRewritten);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("toJson({ a: '<b>' })", warning.Message, StringComparison.Ordinal);
        Assert.Contains("NaN", warning.Message, StringComparison.Ordinal);
        Assert.Equal("{\"a\":\"<b>\"}", (await PortableJavaScript.EvaluateAsync(ReadExpression(result))).GetString());
    }

    [Fact]
    public async Task A_guid_built_in_that_is_also_a_generated_accessor_is_ambiguous()
    {
        var result = await ConvertAsync("getGuidString()", new Elsa3Variable { Id = "v", Name = "guidString", TypeName = "String" });

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    // --- Date interpretation (FR-022 to FR-024, FR-034, quickstart C12) ---------------------------------------------

    [Theory]
    [InlineData("new Date('2026-10-28T09:15:00').getHours()")]
    [InlineData("Date.parse('2026-10-28T09:15:00')")]
    [InlineData("new Date(2026, 9, 28)")]
    [InlineData("new Date().getTimezoneOffset()")]
    [InlineData("var s = '2026-10-28'; new Date(s)")]
    [InlineData("var s = '2026-10-28'; Date.parse(s)")]
    [InlineData("var t = '\"2026-10-28T09:15:00\"'; new Date(JSON.parse(t)).getTime()")]
    [InlineData("var s = 'x'; new Date(s + 'T09:15:00').getTime()")]
    [InlineData("var rest = '-03-01T10:00'; new Date(2026 + rest).getUTCHours()")]
    [InlineData("var s = '2026-03-01'; var t = 'T10:00'; new Date(s.valueOf() + t).getUTCHours()")]
    [InlineData("Date.parse('1-7-2026')")]
    [InlineData("var parts = [2026, 0, 1]; new Date(...parts)")]
    public async Task Zone_dependent_usage_is_refused_under_refuse_naming_both_settings(string expression)
    {
        var result = await ConvertAsync(expression);

        Assert.False(result.Succeeded);
        // A local parse followed by a local getter is two zone-dependent uses, each refused.
        Assert.All(result.Diagnostics.Where(diagnostic => diagnostic.IsError), refusal =>
        {
            Assert.Equal(Elsa3ImportDiagnosticCodes.ForbiddenCapability, refusal.Code);
            Assert.Contains("SourceTimeZone", refusal.Guidance, StringComparison.Ordinal);
            Assert.Contains("AssumeUtc", refusal.Guidance, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("new Date('2026-10-28T09:15:00').getHours()", 9)]
    [InlineData("new Date(2026, 9, 28, 9, 15).getUTCHours()", 9)]
    [InlineData("var s = '2026-10-28T09:15:00'; new Date(s).getUTCHours()", 9)]
    public async Task Under_assume_utc_zone_dependent_usage_converts_in_utc_with_a_warning(string expression, int expected)
    {
        var result = await ConvertAsync(expression, Elsa3DateInterpretation.AssumeUtc);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var warnings = result.Diagnostics.Where(diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateAssumedUtc).ToArray();
        Assert.NotEmpty(warnings);
        Assert.All(warnings, warning =>
        {
            Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("UTC", warning.Message, StringComparison.Ordinal);
        });
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateInterpretationApplied);
        Assert.Equal(expected, (await PortableJavaScript.EvaluateAsync(ReadExpression(result))).GetInt32());
    }

    [Fact]
    public async Task The_date_interpretation_is_recorded_on_every_workflow_whose_javascript_converts()
    {
        var utc = await ConvertAsync("1 + 1", Elsa3DateInterpretation.AssumeUtc);
        var info = Assert.Single(utc.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateInterpretationApplied);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Info, info.Severity);
        Assert.Contains("AssumeUtc", info.Message, StringComparison.Ordinal);

        var refuse = await ConvertAsync("1 + 1");
        Assert.DoesNotContain(refuse.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateInterpretationApplied);
    }

    [Theory]
    // A wall-clock literal in Amsterdam: CET after the 25 October change, CEST before it.
    [InlineData("new Date('2026-10-28T09:15:00').getHours()", "9")]
    [InlineData("new Date('2026-10-28T09:15:00').toISOString()", "2026-10-28T08:15:00.000Z")]
    [InlineData("new Date('2026-07-01T09:15:00').toISOString()", "2026-07-01T07:15:00.000Z")]
    [InlineData("Date.parse('2026-10-28T09:15:00')", "1793175300000")]
    [InlineData("new Date(Date.UTC(2026, 9, 28, 8, 15)).getHours()", "9")]
    [InlineData("new Date(Date.UTC(2026, 6, 1, 8, 15)).getHours()", "10")]
    [InlineData("new Date(Date.UTC(2026, 0, 15, 8, 15)).getTimezoneOffset()", "-60")]
    [InlineData("new Date(Date.UTC(2026, 6, 15, 8, 15)).getTimezoneOffset()", "-120")]
    [InlineData("new Date(Date.UTC(2026, 9, 28, 23, 30)).getDate()", "29")]
    [InlineData("new Date(Date.UTC(2026, 9, 28, 23, 30)).getDay()", "4")]
    // The local constructor rolls over out-of-range fields, as Date does.
    [InlineData("new Date(2026, 0, 32).toISOString()", "2026-01-31T23:00:00.000Z")]
    [InlineData("new Date(2026, 12, 1).getFullYear()", "2027")]
    // 02:30 on 29 March does not exist in Amsterdam: Date moves forward; 02:30 on 25 October is repeated: the earlier.
    [InlineData("new Date(2026, 2, 29, 2, 30).toISOString()", "2026-03-29T01:30:00.000Z")]
    [InlineData("new Date(2026, 9, 25, 2, 30).toISOString()", "2026-10-25T00:30:00.000Z")]
    // A local setter changes the field in the zone and keeps the instant in step.
    [InlineData("(function () { const d = new Date(Date.UTC(2026, 0, 15, 8, 15)); d.setHours(23); return d.toISOString(); })()", "2026-01-15T22:15:00.000Z")]
    [InlineData("(function () { const d = new Date(Date.UTC(2026, 0, 31, 12)); d.setMonth(1); return d.toISOString(); })()", "2026-03-03T12:00:00.000Z")]
    // A non-literal parse: text without an offset is local, text with one is an instant.
    [InlineData("var s = '2026-10-28T09:15:00'; new Date(s).toISOString()", "2026-10-28T08:15:00.000Z")]
    [InlineData("var s = '2026-10-28T09:15:00Z'; new Date(s).toISOString()", "2026-10-28T09:15:00.000Z")]
    [InlineData("var s = '2026-10-28'; new Date(s).toISOString()", "2026-10-28T00:00:00.000Z")]
    // A parenthesized receiver, and a nested local call.
    [InlineData("(new Date(Date.UTC(2026, 9, 28, 8, 15))).getHours()", "9")]
    [InlineData("new Date(2026, new Date(Date.UTC(2026, 9, 28, 8, 15)).getMonth(), 1).toISOString()", "2026-09-30T22:00:00.000Z")]
    // On an invalid date, setFullYear starts from local midnight on 1 January 1970.
    [InlineData("(function () { var d = new Date(NaN); d.setFullYear(2020); return d.getTime(); })()", "1577833200000")]
    // An optional chain keeps short-circuiting.
    [InlineData("(function () { var d = null; return d?.getHours() ?? 'none'; })()", "none")]
    // A day-month-year text has no offset: a local time.
    [InlineData("new Date('2026-01-15 10:00').toISOString()", "2026-01-15T09:00:00.000Z")]
    // Layout does not matter: the classification reads the syntax tree, so whitespace, line breaks and comments (even
    // ones holding '.' or '(') change nothing, and escapes in a string literal are decoded before its text is classified.
    [InlineData("var   \t d\n=\n   new   Date('2026-10-28T09:15:00') ;\n d . getHours ( )", "9")]
    [InlineData("const\n\nd = new Date(Date.UTC(2026, 9, 28, 8, 15)); d /* a.b( */ . /* c.d( */ getHours /* e( */ ( /* f) */ )", "9")]
    [InlineData("let d = new Date(Date.UTC(2026, 9, 28, 8, 15)); // a.b(\n d\n  .getHours // c(\n  ()", "9")]
    [InlineData("new Date('2026\\u002D10\\u002D28T09:15:00').getHours()", "9")]
    [InlineData("new Date(`2026-10-28T09:15:00`).getHours()", "9")]
    [InlineData("(new Date(Date.UTC(2026, 9, 28, 8, 15)) /* x) */ ) . getHours()", "9")]
    // Zone-independent usage is untouched.
    [InlineData("new Date(Date.UTC(2026, 9, 28, 8, 15)).getUTCHours()", "8")]
    public async Task Under_source_time_zone_the_zone_is_written_in_the_code_and_matches_elsa3(string expression, string expected)
    {
        var result = await ConvertAsync(expression, Elsa3DateInterpretation.SourceTimeZone, "Europe/Amsterdam");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == Elsa3MigrationDiagnosticSeverity.Warning);
        var converted = ReadExpression(result);
        var value = await PortableJavaScript.EvaluateAsync(converted);
        Assert.Equal(expected, value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText());
        if (converted.Source.Contains("__elsa3Tz", StringComparison.Ordinal))
            Assert.Contains("\"Europe/Amsterdam\"", converted.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Under_source_time_zone_a_null_receiver_outside_an_optional_chain_still_fails()
    {
        var call = await ConvertAsync("(function () { var d = null; return d.getHours?.(); })()", Elsa3DateInterpretation.SourceTimeZone, "Europe/Amsterdam");
        Assert.True(call.Succeeded, Describe(call.Diagnostics));
        await Assert.ThrowsAnyAsync<Exception>(() => PortableJavaScript.EvaluateAsync(ReadExpression(call)));

        // Behind a deeper optional chain the helper cannot tell which link was null: refused, not guessed.
        var chain = await ConvertAsync("(function () { var a = { b: null }; return a?.b.getHours(); })()", Elsa3DateInterpretation.SourceTimeZone, "Europe/Amsterdam");
        Assert.Contains("?.", Assert.Single(chain.Diagnostics, diagnostic => diagnostic.IsError).Guidance, StringComparison.Ordinal);
    }

    // An Elsa 3 workflow stores the script inside JSON, escaped (\u0027 for a quote, \n for a line break); the importer
    // reads the JSON first, so the converter sees the script as written.
    [Fact]
    public async Task A_script_read_from_escaped_workflow_json_converts_as_written()
    {
        const string script = "var\t when = new Date('2026-10-28T09:15:00');\n\n  return 'Due <' + when.toISOString() + '> & ' + when.getHours();";
        var definition = ConversionServices.Definition(
            Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(script)))));
        // System.Text.Json's default encoder escapes ', <, >, & and + as \u0027 and so on, as Elsa 3 stored them.
        var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\\u0027", json, StringComparison.Ordinal);
        var read = JsonSerializer.Deserialize<Elsa3WorkflowDefinition>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), read,
            feature: new Elsa3MappingFeature { DateInterpretation = Elsa3DateInterpretation.SourceTimeZone, SourceTimeZone = "Europe/Amsterdam" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var converted = Text(Input(Node(result.Value!.State, "node"), "script"));
        Assert.Equal("Due <2026-10-28T08:15:00.000Z> & 9", (await PortableJavaScript.RunScriptAsync(converted))!.Value.GetString());
    }

    [Fact]
    public async Task A_var_in_a_class_static_block_is_not_declared_outside_it()
    {
        var result = await ConvertAsync("(function () { class C { static { var e = 1; } } e = 2; return e; })()");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.UnsupportedShape &&
                                                          diagnostic.Message.Contains("'e'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Under_source_time_zone_a_spread_construction_is_refused_with_field_guidance()
    {
        var result = await ConvertAsync("new Date(...[2026, 9, 28]).getUTCHours()", Elsa3DateInterpretation.SourceTimeZone, "Europe/Amsterdam");

        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Contains("separate arguments", refusal.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Under_source_time_zone_a_script_body_converts_and_runs_in_the_zone()
    {
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript",
            ("script", Literal("var d = new Date('2026-10-28T09:15:00'); return d.getHours() + ':' + d.getMinutes();"))));
        var feature = new Elsa3MappingFeature { DateInterpretation = Elsa3DateInterpretation.SourceTimeZone, SourceTimeZone = "Europe/Amsterdam" };

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root), feature: feature);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var script = Text(Input(Node(result.Value!.State, "node"), "script"));
        Assert.Contains("Europe/Amsterdam", script, StringComparison.Ordinal);
        Assert.Equal("9:15", (await PortableJavaScript.RunScriptAsync(script))!.Value.GetString());
    }

    [Theory]
    [InlineData("new Date().toString()")]
    [InlineData("new Date().toDateString()")]
    [InlineData("Date()")]
    [InlineData("'Started ' + new Date()")]
    [InlineData("`Started ${new Date()}`")]
    [InlineData("String(new Date())")]
    [InlineData("var d = new Date(); d.toString()")]
    [InlineData("(function () { const d = new Date(); function inner() { return 'at ' + d; } return inner(); })()")]
    [InlineData("(function () { function a() { var d = new Date(); return 'at ' + d; } function b() { var d = 1; return d; } return a() + b(); })()")]
    [InlineData("(function () { { let d = new Date(); var s = 'at ' + d; } { let d = 2; } return s; })()")]
    [InlineData("(function () { var d = new Date(0); var s = 'x'; s += d; return s; })()")]
    public async Task Local_text_is_refused_under_source_time_zone_and_warned_under_assume_utc(string expression)
    {
        var zoned = await ConvertAsync(expression, Elsa3DateInterpretation.SourceTimeZone, "Europe/Amsterdam");
        var refusal = Assert.Single(zoned.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Contains("toISOString()", refusal.Guidance, StringComparison.Ordinal);

        var utc = await ConvertAsync(expression, Elsa3DateInterpretation.AssumeUtc);
        Assert.True(utc.Succeeded, Describe(utc.Diagnostics));
        Assert.Contains(utc.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateAssumedUtc);
    }

    [Theory]
    [InlineData("new Date(Date.UTC(2026, 9, 28, 8, 15)).toLocaleTimeString('nl-NL')", "09:15:00")]
    [InlineData("new Date(Date.UTC(2026, 6, 1, 8, 15)).toLocaleTimeString('nl-NL', {})", "10:15:00")]
    [InlineData("new Date(Date.UTC(2026, 6, 1, 8, 15)).toLocaleTimeString('nl-NL', undefined)", "10:15:00")]
    [InlineData("new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit' }).format(Date.UTC(2026, 9, 28, 8, 15))", "09:15")]
    [InlineData("Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit' }).format(Date.UTC(2026, 9, 28, 8, 15))", "09:15")]
    public async Task Under_source_time_zone_locale_text_on_a_date_names_the_zone(string expression, string expected)
    {
        var result = await ConvertAsync(expression, Elsa3DateInterpretation.SourceTimeZone, "Europe/Amsterdam");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var converted = ReadExpression(result);
        Assert.Contains("timeZone: \"Europe/Amsterdam\"", converted.Source, StringComparison.Ordinal);
        Assert.Equal(expected, (await PortableJavaScript.EvaluateAsync(converted)).GetString());
    }

    [Theory]
    [InlineData("new Date().toLocaleString('nl-NL')")]
    [InlineData("new Intl.DateTimeFormat('nl-NL').format(new Date())")]
    public async Task Locale_text_on_a_date_without_a_zone_follows_the_date_interpretation(string expression)
    {
        Assert.False((await ConvertAsync(expression)).Succeeded);
        var utc = await ConvertAsync(expression, Elsa3DateInterpretation.AssumeUtc);
        Assert.Contains(utc.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DateAssumedUtc);
        var options = "(function () { var o = { hour: '2-digit' }; return new Date().toLocaleString('nl-NL', o); })()";
        Assert.False((await ConvertAsync(options, Elsa3DateInterpretation.SourceTimeZone, "Europe/Amsterdam")).Succeeded);
    }

    [Theory]
    [InlineData("new Date().toLocaleString('nl-NL', { timeZone: 'Europe/Amsterdam' })")]
    [InlineData("new Intl.DateTimeFormat('nl-NL', { timeZone: 'UTC' }).format(0)")]
    public async Task Locale_text_with_an_explicit_zone_converts(string expression)
    {
        var result = await ConvertAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("(5).toLocaleString()")]
    [InlineData("new Date().toLocaleDateString()")]
    [InlineData("'a'.localeCompare('b')")]
    [InlineData("new Intl.NumberFormat().format(5)")]
    [InlineData("Intl.Collator(undefined).compare('a', 'b')")]
    public async Task Locale_dependent_calls_without_a_locale_are_refused_under_every_interpretation(string expression)
    {
        foreach (var interpretation in Enum.GetValues<Elsa3DateInterpretation>())
        {
            var result = await ConvertAsync(expression, interpretation, interpretation == Elsa3DateInterpretation.SourceTimeZone ? "Europe/Amsterdam" : null);
            var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
            Assert.Contains("explicit locale", refusal.Guidance, StringComparison.Ordinal);
        }
    }

    // --- Date-typed Elsa 3 values (FR-032) ---------------------------------------------------------------------------

    [Fact]
    public async Task A_date_typed_variable_is_read_as_a_date_of_the_same_instant()
    {
        var due = new Elsa3Variable { Id = "due", Name = "due", TypeName = "DateTime" };

        var result = await ConvertAsync("getVariable('due').getTime()", due);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var converted = ReadExpression(result);
        Assert.Contains("new Date(variables.due)", converted.Source, StringComparison.Ordinal);
        var value = await PortableJavaScript.EvaluateAsync(converted, new Dictionary<string, JsonElement>
        {
            ["due"] = JsonSerializer.SerializeToElement("2026-10-28T08:15:00Z"),
        });
        Assert.Equal(1793175300000, value.GetInt64());

        // An unset value stays null.
        var nullCheck = await ConvertAsync("getVariable('due') === null", due);
        var unset = await PortableJavaScript.EvaluateAsync(ReadExpression(nullCheck), new Dictionary<string, JsonElement>
        {
            ["due"] = JsonSerializer.SerializeToElement<object?>(null),
        });
        Assert.True(unset.GetBoolean());
    }

    [Fact]
    public async Task Local_use_of_a_date_typed_variable_follows_the_date_interpretation()
    {
        var due = new Elsa3Variable { Id = "due", Name = "due", TypeName = "DateTime" };

        var text = await ConvertAsync("getVariable('due').toString()", due);
        Assert.False(text.Succeeded, Describe(text.Diagnostics));
        var hours = await ConvertAsync("getVariable('due').getHours()", due);
        Assert.False(hours.Succeeded, Describe(hours.Diagnostics));
        var utc = await ConvertAsync("getVariable('due').getHours()", due, Elsa3DateInterpretation.AssumeUtc);
        Assert.True(utc.Succeeded, Describe(utc.Diagnostics));
    }

    // --- Refused functions (FR-013, FR-015, quickstart D16, D17) -------------------------------------------------------

    [Theory]
    [InlineData("bytesToBase64(x)")]
    [InlineData("streamToBase64(x)")]
    [InlineData("getConfig('Key')")]
    [InlineData("getConfiguration('Key')")]
    [InlineData("crypto.randomUUID()")]
    [InlineData("DateTime.Now")]
    [InlineData("new Guid('6F9619FF-8B86-D011-B42D-00CF4FC964FF')")]
    [InlineData("new Random().Next()")]
    [InlineData("TimeSpan.FromHours(1)")]
    public async Task Unavailable_functions_and_dotnet_globals_keep_vf_imp_005(string expression)
    {
        var result = await ConvertAsync(expression);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    // --- Relied-on live .NET objects (FR-016 to FR-019, quickstart E18 to E22a) ---------------------------------------

    [Theory]
    [InlineData("newGuid().toString('N')", "newGuid().toString('N')")]
    [InlineData("newGuid().toByteArray()", "newGuid().toByteArray()")]
    [InlineData("(function () { const g = parseGuid('6F9619FF-8B86-D011-B42D-00CF4FC964FF'); return g.toByteArray(); })()", "g.toByteArray()")]
    [InlineData("new Uri('https://example.org').Host", "new Uri('https://example.org')")]
    [InlineData("(function () { var payload = JSON.parse('{}'); return payload.Items.Where(function (x) { return x; }); })()", "payload.Items.Where(")]
    [InlineData("'abc'.ToUpper()", "'abc'.ToUpper()")]
    [InlineData("JSON.parse('{\"Items\": []}').Items.Where(function (x) { return x; })", "JSON.parse(")]
    [InlineData("Object.values({ a: 1 }).ToList()", "Object.values({ a: 1 }).ToList()")]
    public async Task Member_use_of_a_dotnet_object_or_type_is_refused_quoting_it(string expression, string quoted)
    {
        var result = await ConvertAsync(expression);

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ImportDiagnosticCodes.DotNetMemberUsage, refusal.Code);
        Assert.Contains(quoted, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("JSON", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("getVariable('span').totalHours")]
    [InlineData("getVariable('span').add(getVariable('span'))")]
    [InlineData("toJson(getVariable('span'))")]
    [InlineData("(function () { const span = getVariable('span'); return span.TotalHours; })()")]
    public async Task Member_use_of_a_wrapped_dotnet_variable_is_refused(string expression)
    {
        // A FormFile or Stream variable is refused before its scripts are read (a resource Elsa 4 cannot hold yet); a
        // TimeSpan converts as a value, but Elsa 3 handed scripts the wrapped .NET object.
        var span = new Elsa3Variable { Id = "span", Name = "span", TypeName = "System.TimeSpan" };

        var result = await ConvertAsync(expression, span);

        Assert.True(result.Diagnostics.Any(diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DotNetMemberUsage), Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("getVariable('items').map(function (x) { return x; })", true)]
    [InlineData("getVariable('items').length", true)]
    [InlineData("getVariable('items')[0]", true)]
    [InlineData("getVariable('items').Add('x')", false)]
    [InlineData("getVariable('items').Count > 0", false)]
    [InlineData("getVariable('id').toString()", true)]
    [InlineData("'id-' + getVariable('id')", true)]
    [InlineData("getVariable('id').toString('N')", false)]
    public async Task Lists_keep_their_javascript_members_and_guids_their_text(string expression, bool converts)
    {
        var result = await ConvertAsync(expression,
            new Elsa3Variable { Id = "items", Name = "items", TypeName = "String", IsArray = true },
            new Elsa3Variable { Id = "id", Name = "id", TypeName = "Guid" });

        Assert.Equal(converts, result.Succeeded);
        if (!converts)
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DotNetMemberUsage);
    }

    [Theory]
    [InlineData("(function () { var payload = JSON.parse('{\"OrderId\": 1}'); return payload.OrderId; })()")]
    [InlineData("'abc'.toUpperCase()")]
    [InlineData("Intl.DateTimeFormat('en-GB', { timeZone: 'UTC' }).format(0)")]
    [InlineData("Math.max(1, 2)")]
    [InlineData("(function () { var helpers = { Format: function (x) { return x; } }; return helpers.Format(1); })()")]
    [InlineData("(function () { var o = { a: function () { return 1; } }; var name = 'a'; return o[name](); })()")]
    [InlineData("(function () { const Path = { join: function (a, b) { return a + b; } }; return Path.join('a', 'b'); })()")]
    public async Task Json_reads_native_calls_and_script_members_are_not_refused(string expression)
    {
        var result = await ConvertAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task The_converter_and_publish_use_the_one_dotnet_usage_analysis()
    {
        var calls = new RecordingAnalyzer();
        var rewriter = new Elsa3ExpressionRewriter(calls);

        rewriter.Rewrite("JavaScript", "newGuid()", new Elsa.Primitives.Models.TypeReference("String"),
            _ => Elsa3ExpressionParameterResolution.Failure(new("x", "x", "x")));

        var context = Assert.Single(calls.Contexts);
        Assert.NotNull(context);
    }

    private sealed class RecordingAnalyzer : IJavaScriptDotNetUsageAnalyzer
    {
        public List<JavaScriptDotNetUsageContext?> Contexts { get; } = [];

        public IReadOnlyList<JavaScriptDotNetUsage>? Analyze(string source, bool scriptBody = false, JavaScriptDotNetUsageContext? context = null)
        {
            Contexts.Add(context);
            return [];
        }
    }

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(string expression, params Elsa3Variable[] variables) =>
        ConvertAsync(expression, Elsa3DateInterpretation.Refuse, null, variables);

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        string expression,
        Elsa3Variable variable,
        Elsa3DateInterpretation interpretation) =>
        ConvertAsync(expression, interpretation, null, variable);

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        string expression,
        Elsa3DateInterpretation interpretation,
        string? zone = null,
        params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))), variables: variables),
            feature: new Elsa3MappingFeature { DateInterpretation = interpretation, SourceTimeZone = zone });

    private static async Task<JsonElement> EvaluateAsync(string expression)
    {
        var result = await ConvertAsync(expression);
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return await PortableJavaScript.EvaluateAsync(ReadExpression(result));
    }

    private static ExpressionDefinition ReadExpression(Elsa3MigrationResult<Elsa3ConvertedDefinition> result)
    {
        var argument = Input(Node(result.Value!.State, "node"), "text");
        return (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
            .Deserialize<ExpressionDefinition>()!;
    }

    private static string Describe(IEnumerable<Elsa3MigrationDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
