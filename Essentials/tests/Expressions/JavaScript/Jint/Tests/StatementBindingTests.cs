using System.Text.Json;
using Acornima;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed class StatementBindingTests
{
    [Fact]
    public async Task Bare_object_literal_is_kept_as_an_expression()
    {
        var result = await EvaluateAsync(
            "{ answer: args.value }",
            new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(42) });

        Assert.Equal(42, result.GetProperty("answer").GetInt32());
    }

    [Fact]
    public async Task Statement_body_supports_comments_newlines_and_top_level_return()
    {
        var result = await EvaluateAsync(
            "// A value binding may use a statement body.\nconst greeting = args.name;\nreturn greeting + '!';",
            new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") });

        Assert.Equal("Ada!", result.GetString());
    }

    [Fact]
    public async Task Statement_completion_value_is_used_without_an_explicit_return()
    {
        var result = await EvaluateAsync("const prefix = 'a '; prefix + 1");

        Assert.Equal("a 1", result.GetString());
    }

    [Fact]
    public async Task Statement_body_can_return_an_object_literal()
    {
        var result = await EvaluateAsync(
            "return { answer: args.value };",
            new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(42) });

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal(42, result.GetProperty("answer").GetInt32());
    }

    [Fact]
    public async Task Statement_body_cannot_mutate_its_read_only_args_snapshot()
    {
        var values = new Dictionary<string, JsonElement>
        {
            ["order"] = JsonSerializer.SerializeToElement(new { total = 42 })
        };

        await Assert.ThrowsAsync<JavaScriptException>(() =>
            EvaluateAsync("args.order.total = 0; return args.order.total;", values));

        Assert.Equal(42, values["order"].GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Statement_body_runs_in_strict_mode()
    {
        var exception = await Assert.ThrowsAsync<JavaScriptException>(() =>
            EvaluateAsync("implicitBindingGlobal = 1; return implicitBindingGlobal;"));

        Assert.Contains("implicitBindingGlobal", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t")]
    public async Task Empty_binding_is_rejected_by_the_definition_contract(string source)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => EvaluateAsync(source));
    }

    [Theory]
    [InlineData("// A comment is not a value.")]
    [InlineData("/* A comment is not a value. */")]
    public async Task Comment_only_binding_is_rejected_before_execution(string source)
    {
        await Assert.ThrowsAnyAsync<ParseErrorException>(() => EvaluateAsync(source));
    }

    [Theory]
    [InlineData("''")]
    [InlineData("return '';")]
    [InlineData("const value = ''; return value;")]
    public async Task Empty_string_is_still_a_valid_result(string source)
    {
        Assert.Equal(string.Empty, (await EvaluateAsync(source)).GetString());
    }

    [Fact]
    public async Task Statement_body_without_a_value_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EvaluateAsync("const local = 1;"));

        Assert.Contains("undefined is not supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Statement_body_respects_the_statement_limit()
    {
        await Assert.ThrowsAsync<StatementsCountOverflowException>(() =>
            EvaluateAsync(
                "while (true) { }",
                configureFeature: feature =>
                {
                    feature.ExecutionTimeout = null;
                    feature.MaxStatements = 1_000;
                    feature.MaxRecursionDepth = null;
                }));
    }

    [Fact]
    public async Task Statement_body_respects_the_array_limit()
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            EvaluateAsync("return new Array(9);", configureFeature: feature => feature.MaxArrayLength = 8));

        Assert.Contains("array", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Statement_body_honors_cancellation_during_evaluation()
    {
        using var cancellation = new CancellationTokenSource();

        var evaluation = Task.Run(async () =>
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
            return await EvaluateAsync(
                "while (true) { }",
                cancellationToken: cancellation.Token,
                configureFeature: feature =>
                {
                    feature.ExecutionTimeout = TimeSpan.FromSeconds(5);
                    feature.MaxStatements = null;
                    feature.MaxRecursionDepth = null;
                    // A delayed cancellation callback must not race an unrelated allocation limit.
                    feature.MaxMemoryBytes = null;
                });
        });

        // Jint reports cancellation observed during execution with its own cancellation exception.
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => evaluation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(exception is ExecutionCanceledException or OperationCanceledException, exception.ToString());
    }

    [Fact]
    public async Task Statement_body_stripped_capability_error_keeps_the_binding_diagnostic()
    {
        var exception = await Assert.ThrowsAsync<JavaScriptBindingEvaluationException>(() =>
            EvaluateAsync("return new Date();"));

        Assert.Contains("ambient capability [Date]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_error_is_not_retried_as_a_statement_body()
    {
        // The mutable counter is confined to this fresh evaluation engine. If runtime failure triggered a second
        // evaluation attempt in the same engine, the error would report attempt 2.
        var exception = await Assert.ThrowsAsync<JavaScriptException>(() => EvaluateAsync(
            "(globalThis.__bindingExecutionCount = (globalThis.__bindingExecutionCount || 0) + 1, (() => { throw new Error('attempt ' + globalThis.__bindingExecutionCount); })())")
        );

        Assert.Contains("attempt 1", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> EvaluateAsync(
        string source,
        IReadOnlyDictionary<string, JsonElement>? values = null,
        CancellationToken cancellationToken = default,
        Action<JintFeature>? configureFeature = null)
    {
        values ??= new Dictionary<string, JsonElement>();
        await using var provider = JintTestHost.Build(configureFeature);
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
            ExpressionCapabilityProfiles.BindingPureV1);
        return await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(definition, values, cancellationToken));
    }
}
