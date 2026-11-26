using System.Text.Json.Nodes;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
using Elsa.Workbench.CorePreview;
using Xunit;

namespace Elsa.Modularity.Tests.CorePreview;

/// <summary>The kernel host's engines: RunJavaScript on the isolated Jint evaluator, and cron occurrences on Cronos as the old core reads them.</summary>
public sealed class NativeHostAdapterTests
{
    private static readonly JintNativeScripts Scripts = new(JintScriptEvaluators.Create(new FeatureOptions()));

    [Fact]
    public async Task A_program_sees_its_arguments_and_returns_its_value()
    {
        var value = await Scripts.EvaluateAsync("return args.a + args.b;", new JsonObject { ["a"] = 2, ["b"] = 3 }, CancellationToken.None);
        Assert.Equal("5", value?.ToJsonString());
    }

    [Fact]
    public async Task A_program_without_a_value_returns_null()
    {
        Assert.Null(await Scripts.EvaluateAsync("const x = 1;", new JsonObject(), CancellationToken.None));
    }

    [Fact]
    public async Task A_program_that_writes_its_arguments_throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () => await Scripts.EvaluateAsync("args.a = 1; return 1;", new JsonObject { ["a"] = 0 }, CancellationToken.None));
    }

    [Fact]
    public async Task An_object_value_keeps_its_fields()
    {
        var value = await Scripts.EvaluateAsync("return { status: 'approve', count: 2 };", new JsonObject(), CancellationToken.None);
        Assert.Equal("approve", value?["status"]?.GetValue<string>());
        Assert.Equal("2", value?["count"]?.ToJsonString());
    }

    [Fact]
    public void Five_fields_are_standard_cron_and_an_occurrence_is_strictly_after()
    {
        var calendar = new CronosCalendar();
        var midnight = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(midnight + 60_000, calendar.Next("* * * * *", midnight));
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 9, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), calendar.Next("30 9 * * *", midnight + 10 * 3_600_000));
    }

    [Fact]
    public void Six_fields_include_seconds()
    {
        var midnight = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(midnight + 30_000, new CronosCalendar().Next("*/30 * * * * *", midnight));
    }

    [Fact]
    public void An_expression_that_does_not_read_is_refused()
    {
        Assert.NotNull(CronosCalendar.Error("* * *"));
        Assert.Null(CronosCalendar.Error("0 0 * * *"));
        Assert.Throws<FormatException>(() => new CronosCalendar().Next("not cron", 0));
    }
}
