using System.Globalization;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// ADR 0088 D4 / spec 260: under binding-pure-v2 the sandbox has a <c>Date</c> that cannot read the clock, is UTC on
/// every host, and parses only ISO 8601 date-time strings. Everything else about <c>Date</c> follows ECMAScript.
/// </summary>
public sealed class ClocklessDateTests
{
    [Theory]
    [InlineData("new Date('2026-10-20T08:30:00.000Z').toISOString()", "2026-10-20T08:30:00.000Z")]
    [InlineData("new Date('2026-10-20T08:30:00+02:00').toISOString()", "2026-10-20T06:30:00.000Z")]
    [InlineData("new Date('2026-10-20T08:30').toISOString()", "2026-10-20T08:30:00.000Z")]
    [InlineData("new Date('2026-10-20T08:30:00.123').toISOString()", "2026-10-20T08:30:00.123Z")]
    [InlineData("new Date('2026-10-20').toISOString()", "2026-10-20T00:00:00.000Z")]
    [InlineData("new Date('2026-10').toISOString()", "2026-10-01T00:00:00.000Z")]
    [InlineData("new Date('2026').toISOString()", "2026-01-01T00:00:00.000Z")]
    [InlineData("new Date('+012026-10-20T00:00:00Z').getUTCFullYear()", "12026")]
    [InlineData("new Date(0).toISOString()", "1970-01-01T00:00:00.000Z")]
    [InlineData("new Date(2026, 9, 20, 5, 6, 7, 8).toISOString()", "2026-10-20T05:06:07.008Z")]
    [InlineData("new Date(new Date(7)).getTime()", "7")]
    [InlineData("Date.UTC(2026, 9, 20)", "1792454400000")]
    [InlineData("Date.parse('2026-10-20T00:00:00Z')", "1792454400000")]
    [InlineData("new Date(0).toJSON()", "1970-01-01T00:00:00.000Z")]
    [InlineData("new Date(0).valueOf()", "0")]
    [InlineData("new Date(0).toString()", "Thu Jan 01 1970 00:00:00 GMT+0000 (Coordinated Universal Time)")]
    [InlineData("new Date(0).toUTCString()", "Thu, 01 Jan 1970 00:00:00 GMT")]
    [InlineData("new Date(0).toDateString()", "Thu Jan 01 1970")]
    [InlineData("new Date(0).getTimezoneOffset()", "0")]
    [InlineData("new Date('2026-10-20T23:30:00Z').getHours() === new Date('2026-10-20T23:30:00Z').getUTCHours()", "true")]
    [InlineData("new Date('2026-10-20T23:30:00Z').getDate()", "20")]
    [InlineData("(() => { const d = new Date(0); d.setUTCFullYear(2026); d.setMonth(1); return d.toISOString(); })()", "2026-02-01T00:00:00.000Z")]
    [InlineData("new Date(10) - new Date(3)", "7")]
    [InlineData("new Date(3) < new Date(10)", "true")]
    [InlineData("new Date(1) instanceof Date", "true")]
    [InlineData("new Date(1).constructor === Date && Date.prototype.constructor === Date", "true")]
    [InlineData("(() => { class Stamp extends Date { constructor() { super(5); } } return new Stamp().getTime(); })()", "5")]
    [InlineData("JSON.stringify({ at: new Date(0) })", "{\"at\":\"1970-01-01T00:00:00.000Z\"}")]
    public async Task An_allowed_date_member_behaves_as_ecmascript_specifies_in_utc(string source, string expected)
    {
        var result = await ClocklessDateEvaluation.EvaluateAsync(source);

        Assert.Equal(expected, ClocklessDateEvaluation.Text(result));
    }

    [Theory]
    [InlineData("10/20/2026")]
    [InlineData("20 October 2026")]
    [InlineData("Tue Oct 20 2026")]
    [InlineData("2026-10-20 08:30")]
    [InlineData("2026-02-30")]
    [InlineData("2026-10-20T25:00")]
    [InlineData("2026-10-20T08:30:00.1234Z")]
    [InlineData("Thu Jan 01 1970 00:00:00 GMT+0000 (Coordinated Universal Time)")]
    public async Task A_string_outside_the_iso_date_time_format_is_an_invalid_date(string text)
    {
        var source = $"Number.isNaN(new Date({JsonSerializer.Serialize(text)}).getTime()) && Number.isNaN(Date.parse({JsonSerializer.Serialize(text)}))";

        var result = await ClocklessDateEvaluation.EvaluateAsync(source);

        Assert.True(result.GetBoolean());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public async Task Results_do_not_depend_on_the_current_culture(string culture)
    {
        const string source = "[new Date(Date.UTC(2026, 9, 20, 8, 30)).toString(), new Date('2026-10-20T08:30:00Z').toISOString(), new Date(2026, 9, 20).getTime()].join('|')";
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var result = await ClocklessDateEvaluation.EvaluateAsync(source);

            Assert.Equal("Tue Oct 20 2026 08:30:00 GMT+0000 (Coordinated Universal Time)|2026-10-20T08:30:00.000Z|1792454400000", result.GetString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("typeof Temporal")]
    [InlineData("typeof Intl")]
    [InlineData("typeof Math.random")]
    public async Task Other_ambient_capabilities_stay_unavailable(string source)
    {
        var result = await ClocklessDateEvaluation.EvaluateAsync(source);

        Assert.Equal("undefined", result.GetString());
    }

    [Fact]
    public async Task An_expression_cannot_restore_the_native_date()
    {
        const string source = """
            (() => {
              try { Date = function () {}; } catch (e) { }
              try { Object.defineProperty(Date, 'now', { value: () => 1 }); } catch (e) { }
              try { Date.prototype.toLocaleString = () => 'x'; } catch (e) { }
              try { return String(Date.now()); } catch (e) { return e.message; }
            })()
            """;

        var result = await ClocklessDateEvaluation.EvaluateAsync(source);

        Assert.StartsWith("Date.now() reads the clock", result.GetString(), StringComparison.Ordinal);
    }

    // SC-006: each Date and ID pattern from the JTest corpus written as a binding-pure-v2 expression.
    [Fact]
    public async Task Corpus_pattern_stamp_now()
    {
        var result = await ClocklessDateEvaluation.EvaluateAsync(
            "({ result: new Date(args.now).toISOString() })",
            new Dictionary<string, JsonElement> { ["now"] = JsonSerializer.SerializeToElement("2026-10-20T08:30:00.123Z") });

        Assert.Equal("2026-10-20T08:30:00.123Z", result.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Corpus_pattern_parse_a_request_date()
    {
        var result = await ClocklessDateEvaluation.EvaluateAsync(
            "const date = new Date(args.body.date); return date.toISOString();",
            new Dictionary<string, JsonElement> { ["body"] = JsonSerializer.SerializeToElement(new { date = "2026-10-20" }) });

        Assert.Equal("2026-10-20T00:00:00.000Z", result.GetString());
    }

    [Fact]
    public async Task Corpus_pattern_elapsed_milliseconds_between_two_dates()
    {
        var result = await ClocklessDateEvaluation.EvaluateAsync(
            "new Date(args.now).getTime() - new Date(args.startTime).getTime()",
            new Dictionary<string, JsonElement>
            {
                ["now"] = JsonSerializer.SerializeToElement("2026-10-20T08:30:01.500Z"),
                ["startTime"] = JsonSerializer.SerializeToElement("2026-10-20T08:30:00.000Z")
            });

        Assert.Equal(1500, result.GetInt64());
    }

    [Fact]
    public async Task Corpus_pattern_one_and_two_new_ids()
    {
        var result = await ClocklessDateEvaluation.EvaluateAsync(
            "({ result: args.id, other: args.other, correlationId: args.correlationId })",
            new Dictionary<string, JsonElement>
            {
                ["id"] = JsonSerializer.SerializeToElement("01929f1c-7e2a-7c3d-9b1e-4f2a6c8d0e11"),
                ["other"] = JsonSerializer.SerializeToElement("01929f1c-7e2a-7c3d-9b1e-4f2a6c8d0e12"),
                ["correlationId"] = JsonSerializer.SerializeToElement("order-42")
            });

        Assert.NotEqual(result.GetProperty("result").GetString(), result.GetProperty("other").GetString());
    }
}

/// <summary>Evaluates portable JavaScript under a chosen profile through the composed expression features.</summary>
internal static class ClocklessDateEvaluation
{
    public static async Task<JsonElement> EvaluateAsync(
        string source,
        IReadOnlyDictionary<string, JsonElement>? parameters = null,
        string profile = ExpressionCapabilityProfiles.BindingPureV2)
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();
        var values = parameters ?? new Dictionary<string, JsonElement>();
        var definition = new ExpressionDefinition(
            "JavaScript",
            source,
            new TypeReference("Any"),
            values.ToDictionary(item => item.Key, item => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(item.Value)),
            JsonSerializer.SerializeToElement(new { }),
            profile);
        return await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(definition, values, CancellationToken.None));
    }

    public static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText()
    };
}
