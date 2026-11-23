using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// What a .NET-implemented function receives from a script body (spec 1223 FR-003, FR-028, FR-029): a number that lost
/// precision when it entered JavaScript reaches the function with a warning naming the function and the argument path,
/// and the call runs; a function that reads variables reads them through the body's session, as it does through an
/// input binding's reader.
/// </summary>
public sealed class HostFunctionArgumentTests
{
    private const string Order = """{"id":9007199254740993,"price":10.50}""";

    [Fact]
    public async Task A_lossy_number_argument_warns_naming_the_function_and_the_argument_and_the_call_runs()
    {
        var (result, warnings) = await RunAsync("return acme.echo(variables.order.id) === 9007199254740992 ? 'ran' : 'other';");

        Assert.Equal("ran", result!.Value.GetString());
        var warning = Assert.Single(warnings.Warnings, warning => warning.Details?.ContainsKey("function") == true);
        Assert.Equal(JavaScriptValueDiagnostics.PrecisionLossRead, warning.Code);
        Assert.Equal("acme.echo", warning.Details!["function"]);
        Assert.Equal("argument 0", warning.Details["paths"]);
        Assert.DoesNotContain("9007199254740993", warning.Message, StringComparison.Ordinal);
        Assert.Null(warning.ProtectedForm);
    }

    [Fact]
    public async Task A_lossy_number_inside_an_argument_names_its_path_and_the_protected_form_only_the_argument()
    {
        var (_, warnings) = await RunAsync("acme.echo({ order: { id: variables.order.id } }); return 1;");

        var warning = Assert.Single(warnings.Warnings, warning => warning.Details?.ContainsKey("function") == true);
        Assert.Equal("argument 0.order.id", warning.Details!["paths"]);
        Assert.Equal("argument 0", warning.ProtectedForm!.Details!["paths"]);
    }

    [Fact]
    public async Task A_protected_read_reports_the_protected_form()
    {
        var (_, warnings) = await RunAsync("acme.echo({ id: variables.order.id }); return 1;", protectedRead: true);

        var warning = Assert.Single(warnings.Warnings, warning => warning.Details?.ContainsKey("function") == true);
        Assert.Equal("argument 0", warning.Details!["paths"]);
    }

    [Fact]
    public async Task An_exact_number_argument_raises_no_function_warning()
    {
        var (_, warnings) = await RunAsync("acme.echo(variables.order.price); acme.echo(42); return 1;");

        Assert.DoesNotContain(warnings.Warnings, warning => warning.Details?.ContainsKey("function") == true);
    }

    [Fact]
    public async Task The_same_lossy_call_in_a_loop_warns_once()
    {
        var (_, warnings) = await RunAsync("for (var i = 0; i < 5; i++) acme.echo(variables.order.id); return 1;");

        Assert.Single(warnings.Warnings, warning => warning.Details?.ContainsKey("function") == true);
    }

    [Fact]
    public async Task Calls_with_different_member_paths_under_one_argument_warn_once()
    {
        var (_, warnings) = await RunAsync("acme.echo({ a: variables.order.id }); acme.echo({ b: variables.order.id }); return 1;");

        Assert.Single(warnings.Warnings, warning => warning.Details?.ContainsKey("function") == true);
    }

    // Spec 1222 FR-034: a function sees what the script holds for a variable it changed, not the committed value.
    [Theory]
    [InlineData("variables.order = { price: 1 }; return [variables.order.price, acme.readOrder().price];", "[1,1]")]
    [InlineData("variables.order.total = 2; return acme.readOrder().total;", "2")]
    [InlineData("var p = variables.order.price; return acme.readOrder().price === p;", "true")]
    // Read but unchanged: the function gets the stored JSON, every digit kept.
    [InlineData("var o = variables.order; return acme.readOrderIdText();", "\"9007199254740993\"")]
    public async Task A_function_sees_the_script_s_current_value_of_a_variable(string source, string expected)
    {
        var (result, _) = await RunAsync(source);

        Assert.Equal(expected, result!.Value.GetRawText());
    }

    // A lossy number a function returns is tracked like any lossy entered number: reading it warns.
    [Fact]
    public async Task A_lossy_number_a_function_returns_is_tracked()
    {
        var (_, warnings) = await RunAsync("var n = acme.big(); return n > 0 ? 1 : 0;");

        var warning = Assert.Single(warnings.Warnings, warning => warning.Code == JavaScriptValueDiagnostics.PrecisionLossRead);
        Assert.Contains("acme.big()", warning.Details!["paths"], StringComparison.Ordinal);
    }

    // Each call's result is its own entered value: writing one call's result does not hide another call's lossy read.
    [Fact]
    public async Task A_lossy_result_read_from_one_call_still_warns_when_another_call_s_result_is_written()
    {
        var (_, warnings) = await RunAsync("var x = acme.lookup('a').n * 2; variables.order = acme.lookup('b'); return x > 0 ? 1 : 0;");

        Assert.Contains(warnings.Warnings, warning => warning.Code == JavaScriptValueDiagnostics.PrecisionLossRead &&
                                                     warning.Details!["paths"].Contains("acme.lookup()", StringComparison.Ordinal));
    }

    // A blind write is the script's value: the function's read records no session read the script never made.
    [Fact]
    public async Task A_function_reading_a_variable_the_script_assigned_records_no_session_read()
    {
        var session = new Session(Order);
        var (result, _) = await RunAsync("variables.order = { price: 1 }; return acme.readOrder().price;", session: session);

        Assert.Equal(1, result!.Value.GetInt32());
        Assert.Empty(session.ReadNames);
    }

    // A member set from a stored Infinity to null is a change, as the value bridge sees it: the function gets null.
    [Fact]
    public async Task A_function_sees_a_member_changed_from_infinity_to_null()
    {
        var (result, _) = await RunAsync("variables.order.n = null; return acme.readOrderText();", session: new Session("""{"n":1e400}"""));

        Assert.Equal("""{"n":null}""", result!.Value.GetString());
    }

    // What the value bridge counts as unchanged, the function gets as stored: every digit, as the session keeps it.
    [Theory]
    [InlineData("var o = variables.order; delete o.price; o.price = 10.5; return acme.readOrderIdText();")]
    [InlineData("variables.order.price = new Number(10.5).valueOf(); return acme.readOrderIdText();")]
    public async Task A_value_the_bridge_counts_unchanged_reaches_the_function_with_every_digit(string source)
    {
        var (result, _) = await RunAsync(source);

        Assert.Equal("9007199254740993", result!.Value.GetString());
    }

    // A changed variable reaches the function as the bridge will write it: untouched members keep their stored digits.
    [Theory]
    [InlineData("variables.order.price = 11; return acme.readOrderIdText();", "\"9007199254740993\"")]
    [InlineData("variables.order = [variables.order]; return acme.readOrderText();", "\"[{\\\"id\\\":9007199254740993,\\\"price\\\":10.50}]\"")]
    public async Task A_changed_variable_reaches_the_function_as_the_bridge_will_write_it(string source, string expected)
    {
        var (result, _) = await RunAsync(source);

        Assert.Equal(expected, result!.Value.GetRawText());
    }

    // A preview remembers nothing: a later change after the function read is still written.
    [Fact]
    public async Task A_change_after_a_function_read_is_still_collected()
    {
        var session = new Session(Order);
        await RunAsync("var o = variables.order; acme.readOrder(); o.price = 12; return 1;", session: session);

        Assert.Contains("\"price\":12", session.Written!.Value.GetRawText(), StringComparison.Ordinal);
    }

    // A preview nested in a preview (a toJSON calling a function that reads a variable) leaves the outer one remembering
    // nothing: a change made afterwards is still written.
    [Fact]
    public async Task A_nested_preview_leaves_a_later_change_collected()
    {
        var session = new Session("""{"c":{"n":1},"d":1}""");
        await RunAsync(
            "var o = variables.order; var depth = 0;" +
            " o.t = { toJSON: function () { if (depth++ === 0) acme.readOrder(); return 0; } };" +
            " acme.readOrder(); o.c.n = 5; return 1;",
            session: session);

        Assert.Contains("\"n\":5", session.Written!.Value.GetRawText(), StringComparison.Ordinal);
    }

    // Many calls of one function report it once, not once per call.
    [Fact]
    public async Task Lossy_results_of_many_calls_report_the_function_once()
    {
        var (_, warnings) = await RunAsync("var s = 0; for (var i = 0; i < 50; i++) s += acme.big() > 0 ? 1 : 0; return s;");

        var warning = Assert.Single(warnings.Warnings, warning => warning.Code == JavaScriptValueDiagnostics.PrecisionLossRead);
        Assert.Equal("acme.big()", warning.Details!["paths"]);
    }

    // The evaluation's limits count across host-function calls, which call back into the engine; a script cannot catch
    // a limit as the function's error.
    [Theory]
    [InlineData("while (true) acme.echo(1);")]
    [InlineData("while (true) { try { acme.readOrder(); } catch (e) { } }")]
    public async Task Statement_limit_counts_across_host_function_calls(string source)
    {
        await Assert.ThrowsAsync<global::Jint.Runtime.StatementsCountOverflowException>(() =>
            RunAsync(source, configure: feature => { feature.MaxStatements = 1_000; feature.ExecutionTimeout = null; }));
    }

    [Fact]
    public async Task Timeout_counts_across_host_function_calls()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            RunAsync("variables.order.price = 1; while (true) acme.readOrder();", configure: feature =>
            {
                feature.ExecutionTimeout = TimeSpan.FromMilliseconds(200);
                feature.MaxStatements = null;
            }));
    }

    // A function's own timeout (a lock, a remote call) is its error, which the script can catch; only the evaluation's
    // timeout stops the evaluation.
    [Fact]
    public async Task A_function_s_own_timeout_is_a_catchable_error()
    {
        var (result, _) = await RunAsync("try { acme.slow(); return 'no'; } catch (e) { return e.message; }");

        Assert.Equal("acme.slow: lock wait timed out", result!.Value.GetString());
    }

    // TimeSpan.MaxValue means no timeout.
    [Fact]
    public async Task The_longest_timeout_never_comes()
    {
        var (result, _) = await RunAsync("return acme.echo(1);", configure: feature => feature.ExecutionTimeout = TimeSpan.MaxValue);

        Assert.Equal(1, result!.Value.GetInt32());
    }

    // The memory limit is the script's: serializing, comparing and patching plain data is host work (PlainJson), so
    // reading a large changed variable through a function many times, or passing it as an argument, does not add up.
    // (A function that hands the whole variable back gives the script a new value each call: that is counted.)
    [Fact]
    public async Task Bridge_work_on_a_large_variable_does_not_count_against_the_memory_limit()
    {
        var members = string.Join(",", Enumerable.Range(0, 9_000).Select(i => $"\"m{i}\":{{\"id\":{i},\"name\":\"item{i}\",\"price\":{i}.5}}"));
        var session = new Session("{" + members + "}");

        var (result, _) = await RunAsync(
            "variables.order.updatedAt = 1; for (var i = 0; i < 16; i++) { acme.readOrderSize(); acme.size(variables.order); } return 1;",
            session: session,
            configure: feature => feature.MaxMemoryBytes = 64 * 1024 * 1024);

        Assert.Equal(1, result!.Value.GetInt32());
        Assert.Contains("\"updatedAt\":1", session.Written!.Value.GetRawText(), StringComparison.Ordinal);
    }

    // The script's own code met in host work (a toJSON on an argument) is the script's: its statements and the memory it
    // keeps count against the script's limits, across calls.
    [Fact]
    public async Task Script_code_in_host_work_counts_its_statements()
    {
        await Assert.ThrowsAsync<global::Jint.Runtime.StatementsCountOverflowException>(() => RunAsync(
            "var n = 0; for (var j = 0; j < 30; j++) acme.echo({ toJSON: function () { for (var k = 0; k < 4000; k++) n++; return 1; } }); return n;",
            configure: feature => { feature.MaxStatements = 10_000; feature.ExecutionTimeout = null; }));
    }

    [Fact]
    public async Task Script_code_in_host_work_counts_the_memory_it_keeps()
    {
        await Assert.ThrowsAsync<global::Jint.Runtime.MemoryLimitExceededException>(() => RunAsync(
            "var keep = []; for (var i = 0; i < 20; i++) acme.echo({ toJSON: function () { keep.push('x'.repeat(20000000) + i); return 1; } }); return keep.length;",
            configure: feature => { feature.MaxMemoryBytes = 64 * 1024 * 1024; feature.ExecutionTimeout = null; }));
    }

    // A native toJSON runs no statement, and a function's result is built for the script: both are the script's memory.
    [Theory]
    [InlineData("var src = Array.from({ length: 60000 }); var all = []; for (var i = 0; i < 300; i++) { var k = []; all.push(k); acme.echo({ toJSON: Function.prototype.apply.bind(Array.prototype.push, k, src) }); } return 1;")]
    [InlineData("var s = 'x'.repeat(400000); var keep = []; for (var i = 0; i < 300; i++) keep.push(acme.echo(s)); return 1;")]
    public async Task Memory_the_script_keeps_from_host_work_counts(string source)
    {
        await Assert.ThrowsAsync<global::Jint.Runtime.MemoryLimitExceededException>(() =>
            RunAsync(source, configure: feature => { feature.MaxMemoryBytes = 64 * 1024 * 1024; feature.ExecutionTimeout = null; }));
    }

    // An error a function call raises can be caught and kept by the script, stack text and all: counted.
    [Fact]
    public async Task Errors_the_script_keeps_from_function_calls_count()
    {
        await Assert.ThrowsAsync<global::Jint.Runtime.MemoryLimitExceededException>(() => RunAsync(
            "var name = 'n'.repeat(100000); var o = { [name]: function () { try { acme.echo(function () { }); } catch (e) { return e; } } };" +
            " var keep = []; for (var i = 0; i < 3000; i++) keep.push(o[name]()); return 1;",
            configure: feature => { feature.MaxMemoryBytes = 64 * 1024 * 1024; feature.ExecutionTimeout = null; }));
    }

    // A changed value that is not plain (a getter) inside a written variable makes the whole write go through the engine,
    // in its own order: the getter's changes to keys still to come read as JSON.stringify reads them.
    [Fact]
    public async Task A_getter_inside_a_written_variable_serializes_as_the_engine_does()
    {
        var session = new Session("""{"a":{"x":1},"h":"shown"}""");
        await RunAsync(
            "var o = variables.order; Object.prototype.h = 'inherited';" +
            " Object.defineProperty(o.a, 'g', { enumerable: true, configurable: true, get: function () { delete o.h; Object.defineProperty(o.a, 'g', { value: 2, enumerable: true }); return 2; } });" +
            " return 1;",
            session: session);

        var expected = await RunAsync(
            "var o = variables.order; Object.prototype.h = 'inherited';" +
            " Object.defineProperty(o.a, 'g', { enumerable: true, configurable: true, get: function () { delete o.h; Object.defineProperty(o.a, 'g', { value: 2, enumerable: true }); return 2; } });" +
            " return JSON.stringify(o, function (k, v) { if (k === '') JSON.stringify(v); return v; });",
            session: new Session("""{"a":{"x":1},"h":"shown"}"""));

        Assert.Equal(expected.Result!.Value.GetString(), session.Written!.Value.GetRawText());
    }

    [Fact]
    public async Task A_function_that_reads_variables_reads_through_the_body_session()
    {
        var (result, _) = await RunAsync("return acme.readOrder().price;");

        Assert.Equal(10.5, result!.Value.GetDouble());
    }

    private static async Task<(JsonElement? Result, RecordingWarningSink Warnings)> RunAsync(
        string source, bool protectedRead = false, Session? session = null, Action<JintFeature>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.echo", call => call.Arguments[0].Clone()));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration(
            "acme.readOrderSize",
            call => JsonSerializer.SerializeToElement(call.Variables!.Read("order").Value.GetRawText().Length),
            ReadsVariables: true));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.size", call => JsonSerializer.SerializeToElement(call.Arguments[0].GetRawText().Length)));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.slow", _ => throw new TimeoutException("lock wait timed out")));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.big", _ => JsonSerializer.Deserialize<JsonElement>("9007199254740993")));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.lookup", call => JsonSerializer.Deserialize<JsonElement>(
            call.Arguments[0].GetString() == "a" ? """{"n":9007199254740993}""" : """{"m":1}""")));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration(
            "acme.readOrderText",
            call => JsonSerializer.SerializeToElement(call.Variables!.Read("order").Value.GetRawText()),
            ReadsVariables: true));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration(
            "acme.readOrderIdText",
            call => JsonSerializer.SerializeToElement(call.Variables!.Read("order").Value.GetProperty("id").GetRawText()),
            ReadsVariables: true));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration(
            "acme.readOrder",
            call => call.Variables!.Read("order").Value,
            ReadsVariables: true));
        var feature = new JintFeature();
        configure?.Invoke(feature);
        feature.ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var warnings = new RecordingWarningSink();
        var result = await scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>().EvaluateAsync(
            new JavaScriptScriptEvaluationRequest(source, JsonSerializer.SerializeToElement(new { }), CancellationToken.None)
            {
                Warnings = warnings,
                Variables = session ?? new Session(Order) { HasReadProtectedValue = protectedRead }
            });
        return (result.Value, warnings);
    }

    private sealed class Session(string order) : IScriptVariableSession
    {
        public IReadOnlyList<string> CandidateNames { get; } = ["order"];
        public bool HasReadProtectedValue { get; init; }
        public bool IsReadOnly(string name) => false;

        // This fixture supplies protection explicitly, independently of concurrency reads, as the runtime session does.
        public void ObservePendingRead(string name) { }

        public List<string> ReadNames { get; } = [];

        public ExpressionVariableRead Read(string name)
        {
            ReadNames.Add(name);
            return name == "order" ? ExpressionVariableRead.Present(JsonSerializer.Deserialize<JsonElement>(order)) : ExpressionVariableRead.Missing;
        }

        public JsonElement? Written { get; private set; }

        public void Write(string name, JsonElement? value) => Written = value;
    }
}
