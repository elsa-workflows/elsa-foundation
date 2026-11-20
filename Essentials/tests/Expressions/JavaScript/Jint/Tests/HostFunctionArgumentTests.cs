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
                                                     warning.Details!["paths"].Contains("acme.lookup()[1]", StringComparison.Ordinal));
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

    [Fact]
    public async Task A_function_that_reads_variables_reads_through_the_body_session()
    {
        var (result, _) = await RunAsync("return acme.readOrder().price;");

        Assert.Equal(10.5, result!.Value.GetDouble());
    }

    private static async Task<(JsonElement? Result, ExecutionWarningCollector Warnings)> RunAsync(string source, bool protectedRead = false, Session? session = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.echo", call => call.Arguments[0].Clone()));
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
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var warnings = new ExecutionWarningCollector();
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
