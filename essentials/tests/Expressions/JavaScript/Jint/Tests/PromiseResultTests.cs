using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Jint.Services;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Issue #5687: a Run JavaScript body or an input expression whose result is a Promise yields the value the Promise
/// settled with, as Elsa 3 did, instead of a silent <c>{}</c>. A rejected Promise faults like a throw; one that never
/// settles faults with <c>VF-PROMISE-001</c>.
/// </summary>
public sealed class PromiseResultTests
{
    // The two Promise scripts of the Elsa 3 JTest corpus (javascript/specialized-methods-tests.json), with acme data.
    private const string AllSettled = """
        const promises = [Promise.resolve(args.fulfilledValue), Promise.reject(args.errorReason)];
        return (async () => await Promise.allSettled(promises))();
        """;

    private const string BusyWaitDelay = """
        const delay = ms => new Promise(resolve => {
            for (let start = Date.now(); Date.now() - start < ms;);
            resolve();
        });
        return (async () => {
            await delay(args.waitTimeMilliseconds);
            return `waited ${args.waitTimeMilliseconds} milliseconds!`;
        })();
        """;

    private static readonly JsonElement CorpusArguments = JsonSerializer.SerializeToElement(new
    {
        fulfilledValue = "acme-fulfilled",
        errorReason = "acme-rejected",
        waitTimeMilliseconds = 5
    });

    [Fact]
    public async Task A_script_returning_an_async_result_yields_the_settled_value()
    {
        var result = await EvaluateScriptAsync("return (async () => { await null; return 'acme'; })();");

        Assert.Equal("acme", result.Value!.Value.GetString());
    }

    [Fact]
    public async Task A_script_returning_Promise_allSettled_yields_the_settled_array()
    {
        var result = await EvaluateScriptAsync(AllSettled, CorpusArguments);

        Assert.Equal(
            """[{"status":"fulfilled","value":"acme-fulfilled"},{"status":"rejected","reason":"acme-rejected"}]""",
            result.Value!.Value.GetRawText());
    }

    [Fact]
    public async Task A_script_awaiting_a_busy_wait_delay_yields_the_settled_text()
    {
        var result = await EvaluateScriptAsync(BusyWaitDelay, CorpusArguments);

        Assert.Equal("waited 5 milliseconds!", result.Value!.Value.GetString());
    }

    [Fact]
    public async Task A_script_returning_a_rejected_Promise_faults_as_a_throw_would()
    {
        var exception = await Assert.ThrowsAsync<JavaScriptException>(() =>
            EvaluateScriptAsync("return (async () => { throw new Error('acme failed'); })();"));

        Assert.Contains("acme failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_returning_a_Promise_that_never_settles_faults_with_guidance()
    {
        var exception = await Assert.ThrowsAsync<PendingPromiseResultException>(() =>
            EvaluateScriptAsync("return new Promise(() => { });"));

        Assert.StartsWith("VF-PROMISE-001", exception.Message, StringComparison.Ordinal);
        Assert.Equal(exception.Message, exception.ValueFreeMessage);
    }

    [Fact]
    public async Task A_completion_with_a_Promise_result_yields_the_settled_value_and_keeps_its_outcome()
    {
        var result = await EvaluateScriptAsync("return complete(Promise.resolve(7), 'Done');");

        Assert.Equal(7, result.Value!.Value.GetInt32());
        Assert.Equal(JavaScriptOutcomeSelectionState.Named, result.SelectionState);
        Assert.Equal(["Done"], result.OutcomeNames);
    }

    [Fact]
    public async Task A_Promise_nested_in_returned_data_stays_an_empty_object_as_in_Elsa_3()
    {
        // Elsa 3 unwrapped only the top-level result; a nested Promise converted to an object without members.
        var result = await EvaluateScriptAsync("return { value: Promise.resolve(1) };");

        Assert.Equal("""{"value":{}}""", result.Value!.Value.GetRawText());
    }

    [Theory]
    [InlineData("(async () => 'acme')()")]
    [InlineData("return Promise.resolve('acme');")]
    public async Task An_input_expression_returning_a_Promise_yields_the_settled_value(string source)
    {
        var result = await EvaluateBindingAsync(source);

        Assert.Equal("acme", result.GetString());
    }

    [Fact]
    public async Task An_input_expression_returning_Promise_allSettled_yields_the_settled_array()
    {
        var result = await EvaluateBindingAsync(AllSettled, CorpusArguments);

        Assert.Equal(
            """[{"status":"fulfilled","value":"acme-fulfilled"},{"status":"rejected","reason":"acme-rejected"}]""",
            result.GetRawText());
    }

    [Fact]
    public async Task An_input_expression_returning_a_rejected_Promise_faults_as_a_throw_would()
    {
        var exception = await Assert.ThrowsAsync<JavaScriptException>(() =>
            EvaluateBindingAsync("Promise.reject(new Error('acme failed'))"));

        Assert.Contains("acme failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_input_expression_returning_a_Promise_that_never_settles_faults_with_guidance()
    {
        var exception = await Assert.ThrowsAsync<PendingPromiseResultException>(() =>
            EvaluateBindingAsync("new Promise(() => { })"));

        Assert.StartsWith("VF-PROMISE-001", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<JavaScriptScriptEvaluationResult> EvaluateScriptAsync(string source, JsonElement? arguments = null)
    {
        await using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        return await scope.ScriptEvaluator().EvaluateAsync(new JavaScriptScriptEvaluationRequest(
            source,
            arguments ?? JsonSerializer.SerializeToElement(new { }),
            CancellationToken.None));
    }

    private static async Task<JsonElement> EvaluateBindingAsync(string source, JsonElement? arguments = null)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (arguments is { } supplied)
        {
            foreach (var property in supplied.EnumerateObject())
                values[property.Name] = property.Value.Clone();
        }

        await using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableJavaScriptEvaluator>();
        var bindings = values.ToDictionary(
            item => item.Key,
            item => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(item.Value),
            StringComparer.Ordinal);
        var definition = new ExpressionDefinition(
            "JavaScript",
            source,
            new Elsa.Primitives.Models.TypeReference("Any"),
            bindings,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingReadonlyV1);
        return await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(definition, values, CancellationToken.None));
    }
}
