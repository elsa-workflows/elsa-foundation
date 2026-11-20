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

    [Fact]
    public async Task A_function_that_reads_variables_reads_through_the_body_session()
    {
        var (result, _) = await RunAsync("return acme.readOrder().price;");

        Assert.Equal(10.5, result!.Value.GetDouble());
    }

    private static async Task<(JsonElement? Result, ExecutionWarningCollector Warnings)> RunAsync(string source, bool protectedRead = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.echo", call => call.Arguments[0].Clone()));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.big", _ => JsonSerializer.Deserialize<JsonElement>("9007199254740993")));
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
                Variables = new Session(Order) { HasReadProtectedValue = protectedRead }
            });
        return (result.Value, warnings);
    }

    private sealed class Session(string order) : IScriptVariableSession
    {
        public IReadOnlyList<string> CandidateNames { get; } = ["order"];
        public bool HasReadProtectedValue { get; init; }
        public bool IsReadOnly(string name) => false;

        public ExpressionVariableRead Read(string name) =>
            name == "order" ? ExpressionVariableRead.Present(JsonSerializer.Deserialize<JsonElement>(order)) : ExpressionVariableRead.Missing;

        public void Write(string name, JsonElement? value)
        {
        }
    }
}
