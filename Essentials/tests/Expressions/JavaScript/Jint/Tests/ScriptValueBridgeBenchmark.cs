using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
using Jint;
using Jint.Native;
using Jint.Runtime.Descriptors;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// The bridge benchmark of spec 1228 SC-007 (T044): the time of one script evaluation on 1 KB, 100 KB and 1 MB inputs,
/// through the copy-compare-patch bridge and through the engine as it was before spec 1228 (values converted up front
/// and frozen, no change detection), kept here as the baseline so a later bridge can be compared. The bridge must stay
/// within twice the baseline on 1 MB. Timing is machine-dependent, so it runs only with ELSA_RUN_BENCHMARKS=1:
/// <c>ELSA_RUN_BENCHMARKS=1 dotnet test --filter ScriptValueBridgeBenchmark</c>.
/// </summary>
public sealed class ScriptValueBridgeBenchmark(ITestOutputHelper output)
{
    private const string Script = "let total = 0; for (const item of args.order.items) total += item.price; return total;";

    [Fact]
    public void The_bridge_stays_within_twice_the_frozen_baseline_on_one_megabyte()
    {
        if (Environment.GetEnvironmentVariable("ELSA_RUN_BENCHMARKS") != "1")
            return;

        var options = new FeatureOptions { MaxInputBytes = 4 * 1024 * 1024, MaxInputNodes = 1_000_000, MaxArrayLength = 1_000_000, ExecutionTimeout = TimeSpan.FromMinutes(1) };
        var bridge = new CopyComparePatchValueBridge(Microsoft.Extensions.Options.Options.Create(options));
        var policy = new RefusingPrecisionPolicy();
        var table = new StringBuilder("| Input | Baseline (ms) | Bridge (ms) | Ratio |\n|---|---|---|---|\n");
        double ratioAtOneMegabyte = 0;

        foreach (var (label, bytes) in new[] { ("1 KB", 1024), ("100 KB", 100 * 1024), ("1 MB", 1024 * 1024) })
        {
            var arguments = Arguments(bytes);
            var baseline = Median(() => RunBaseline(arguments, options));
            var current = Median(() => RunBridge(arguments, options, bridge, policy));
            var ratio = current / baseline;
            if (bytes == 1024 * 1024)
                ratioAtOneMegabyte = ratio;
            table.Append(CultureInfo.InvariantCulture, $"| {label} ({arguments.GetRawText().Length} B) | {baseline:F2} | {current:F2} | {ratio:F2} |\n");
        }

        output.WriteLine(table.ToString());
        Assert.True(ratioAtOneMegabyte <= 2.0, $"SC-007: the bridge took {ratioAtOneMegabyte:F2}x the baseline on 1 MB.\n{table}");
    }

    private static void RunBridge(JsonElement arguments, FeatureOptions options, CopyComparePatchValueBridge bridge, RefusingPrecisionPolicy policy)
    {
        var engine = IsolatedJintEngine.Create(options, CancellationToken.None);
        using var values = bridge.Begin(engine);
        IsolatedJintEngine.SetArgs(engine, values, arguments, options);
        var result = engine.Evaluate(PreparedJavaScript.Script(Script));
        JavaScriptValueWrites.Apply(values.Write(result, "result"), "result", policy);
    }

    /// <summary>The engine before spec 1228: every value converted up front into frozen objects, no change detection.</summary>
    private static void RunBaseline(JsonElement arguments, FeatureOptions options)
    {
        var engine = IsolatedJintEngine.Create(options, CancellationToken.None);
        var args = engine.Intrinsics.Object.Construct([]);
        foreach (var property in arguments.EnumerateObject())
            args.DefineOwnProperty(property.Name, new PropertyDescriptor(Frozen(engine, property.Value), writable: false, enumerable: true, configurable: false));
        args.PreventExtensions();
        engine.Global.DefineOwnProperty("args", new PropertyDescriptor(args, writable: false, enumerable: true, configurable: false));
        var result = engine.Evaluate(PreparedJavaScript.Script(Script));
        _ = JintResultMaterializer.ParseResult(JintResultMaterializer.Stringify(engine, result)!, options);
    }

    private static JsValue Frozen(Engine engine, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var value = engine.Intrinsics.Object.Construct([]);
                foreach (var property in element.EnumerateObject())
                    value.DefineOwnProperty(property.Name, new PropertyDescriptor(Frozen(engine, property.Value), writable: false, enumerable: true, configurable: false));
                value.PreventExtensions();
                return value;
            case JsonValueKind.Array:
                var length = element.GetArrayLength();
                var array = engine.Intrinsics.Array.Construct((uint)length);
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    array.DefineOwnProperty((index++).ToString(CultureInfo.InvariantCulture), new PropertyDescriptor(Frozen(engine, item), writable: false, enumerable: true, configurable: false));
                array.DefineOwnProperty("length", new PropertyDescriptor(JsNumber.Create(length), writable: false, enumerable: false, configurable: false));
                array.PreventExtensions();
                return array;
            case JsonValueKind.String:
                return JsValue.FromObject(engine, element.GetString());
            case JsonValueKind.Number when element.TryGetInt64(out var integer):
                return JsValue.FromObject(engine, integer);
            case JsonValueKind.Number:
                return JsValue.FromObject(engine, element.GetDouble());
            case JsonValueKind.True:
                return JsBoolean.True;
            case JsonValueKind.False:
                return JsBoolean.False;
            default:
                return JsValue.Null;
        }
    }

    private static JsonElement Arguments(int bytes)
    {
        var items = new List<object>();
        var size = 0;
        for (var index = 0; size < bytes - 64; index++)
        {
            var item = new { id = 9_000_000_000_000L + index, name = $"item-{index}", price = 10.5, tags = new[] { "a", "b" } };
            items.Add(item);
            size += JsonSerializer.Serialize(item).Length + 1;
        }

        return JsonSerializer.SerializeToElement(new { order = new { items } });
    }

    private static double Median(Action run)
    {
        for (var warmup = 0; warmup < 3; warmup++)
            run();
        var samples = new List<double>();
        for (var sample = 0; sample < 15; sample++)
        {
            var started = Stopwatch.GetTimestamp();
            run();
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }
}
