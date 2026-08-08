using System.Text.Json;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Pins how multi-statement JavaScript is authored (spec 150 FR-C7). Three benchmarked sessions hit
/// <c>Unexpected token 'var'</c> and concluded multi-line JavaScript was impossible; it is not, but the
/// rule was published nowhere.
/// </summary>
/// <remarks>
/// The two evaluators wrap source differently, and that is the whole rule:
/// <list type="bullet">
/// <item><c>JintPortableJavaScriptEvaluator</c> (expression inputs) evaluates <c>("use strict"; (source))</c>
/// — parenthesised, so the source must be a single <em>expression</em> and a bare statement is a syntax error.</item>
/// <item><c>JintJavaScriptScriptEvaluator</c> (the <c>RunJavaScript</c> activity) evaluates
/// <c>("use strict"; (() =&gt; { source })())</c> — an IIFE body, so statements and <c>return</c> work directly.</item>
/// </list>
/// A consumer therefore has an escape hatch in an expression slot: write the IIFE themselves.
/// </remarks>
public sealed class MultiStatementAuthoringTests
{
    [Fact]
    public async Task An_expression_input_rejects_a_bare_statement()
    {
        // The reported 422: an expression slot is not a script slot.
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => EvaluateExpression("var x = 1; x + 1").AsTask());

        Assert.Contains("var", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_expression_input_accepts_multi_statement_logic_wrapped_in_an_iife()
    {
        // The escape hatch: an IIFE *is* an expression, so statements and return are legal inside it.
        var result = await EvaluateExpression("(() => { var total = 0; for (let i = 1; i <= 3; i++) { total += i; } return total; })()");

        Assert.Equal(6, result.GetInt32());
    }

    [Fact]
    public async Task The_script_evaluator_accepts_statements_and_return_without_a_wrapper()
    {
        // What the RunJavaScript activity uses: the evaluator supplies the IIFE, so the author does not.
        await using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        var evaluator = scope.ScriptEvaluator();

        var result = await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
            "var greeting = 'hello'; return greeting + ' world';",
            JsonSerializer.SerializeToElement(new { }),
            CancellationToken.None));

        Assert.Equal("hello world", result!.Value.GetString());
    }

    private static async ValueTask<JsonElement> EvaluateExpression(string source)
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var definition = new ExpressionDefinition(
            "JavaScript",
            source,
            new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding>(),
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);

        return await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(
            definition,
            new Dictionary<string, JsonElement>(),
            CancellationToken.None));
    }
}
