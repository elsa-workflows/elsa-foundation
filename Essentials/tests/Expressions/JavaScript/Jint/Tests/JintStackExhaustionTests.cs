using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Services;
using Elsa.Primitives.Models;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Issue #4901: a script that goes deep must fail its evaluation, not overflow the stack and end the host process.
/// Every case here ended the test host with "Stack overflow." before the fix, under default options. Each evaluation is
/// started from a thread with a 1 MB stack, so the outcome cannot depend on how much stack the caller has.
/// </summary>
public sealed class JintStackExhaustionTests
{
    // The smallest stack a .NET thread commonly gets; every case below crashed a caller with this much before the fix.
    private const int CallerStackSize = 1024 * 1024;

    [Fact]
    public async Task Unbounded_recursion_fails_with_the_recursion_limit_at_the_default_options()
    {
        var exception = await EvaluateScriptFromSmallStackAsync("function f(n) { return f(n + 1); } return f(0);");

        Assert.IsType<RecursionDepthOverflowException>(exception);
    }

    [Fact]
    public async Task A_toJSON_that_passes_its_own_object_to_a_host_function_fails_the_evaluation()
    {
        // The issue's second reproduction: the host function's argument goes through the value bridge, which calls
        // toJSON, which calls the host function with the same object again.
        var nop = new JavaScriptHostFunctionDeclaration("acme.nop", _ => default);

        var exception = await EvaluateScriptFromSmallStackAsync(
            "var o = {}; o.toJSON = function () { acme.nop(o); return 1; }; acme.nop(o); return 1;",
            services: services => services.AddSingleton(nop));

        AssertStackLimitError(exception);
    }

    [Fact]
    public async Task A_toJSON_that_serializes_its_own_object_again_fails_the_evaluation()
    {
        // The same re-entry through JSON.stringify: serializing the object calls toJSON, which serializes it again.
        var exception = await EvaluateScriptFromSmallStackAsync(
            "var o = {}; o.toJSON = function () { JSON.stringify(o); return 1; }; return JSON.stringify(o);");

        AssertStackLimitError(exception);
    }

    [Fact]
    public async Task A_toJSON_that_returns_ever_deeper_data_fails_when_the_result_is_written()
    {
        // The value bridge serializes the returned value; each toJSON returns a new level, so the serializer recurses
        // without any script call stacking up and the recursion limit never applies.
        var exception = await EvaluateScriptFromSmallStackAsync(
            "var o = {}; o.toJSON = function () { return { a: o }; }; return o;");

        AssertStackLimitError(exception);
    }

    [Fact]
    public async Task A_limit_that_fires_deep_inside_nested_blocks_fails_the_evaluation()
    {
        // No recursion at all: when the timeout fires, the exception unwinds through 240 nested statement lists.
        var source = new string('{', 240) + " while (true) { } " + new string('}', 240);

        var exception = await EvaluateScriptFromSmallStackAsync(source, feature =>
        {
            feature.ExecutionTimeout = TimeSpan.FromMilliseconds(200);
            feature.MaxMemoryBytes = null;
        });

        Assert.IsType<TimeoutException>(exception);
    }

    [Fact]
    public async Task Recursion_through_nested_blocks_fails_with_a_range_error_before_the_stack_runs_out()
    {
        var blocks = 50;
        var source = "function f(n) { " + new string('{', blocks) + " return f(n + 1); " + new string('}', blocks) + " } return f(0);";

        var exception = await EvaluateScriptFromSmallStackAsync(source);

        AssertStackLimitError(exception);
    }

    [Fact]
    public async Task A_limit_that_fires_at_the_stack_budget_edge_still_unwinds()
    {
        // The worst case for unwinding: the deepest the budget allows, with many statement lists per call, and then a
        // limit exception that every one of them catches and throws again.
        var blocks = 100;
        var open = new string('{', blocks);
        var close = new string('}', blocks);
        var source =
            "var d = 0, target = -1; " +
            "function g(n) { " + open + " d = n; if (n === target) while (true) { } return g(n + 1); " + close + " } " +
            "try { g(0); } catch (e) { } target = d - 1; return g(0);";

        var exception = await EvaluateScriptFromSmallStackAsync(source, feature =>
        {
            feature.ExecutionTimeout = TimeSpan.FromMilliseconds(200);
            feature.MaxMemoryBytes = null;
        });

        Assert.IsType<TimeoutException>(exception);
    }

    [Fact]
    public async Task A_script_can_catch_the_range_error_and_carry_on()
    {
        var result = await EvaluateScriptValueFromSmallStackAsync(
            "function f(n) { { { { return f(n + 1); } } } } try { f(0); return 'no'; } catch (e) { return e instanceof RangeError ? e.message : 'other'; }");

        Assert.Null(result.Exception);
        Assert.Equal(JintEvaluationThreads.StackBudgetExceededMessage, result.Value!.Value.GetString());
    }

    [Fact]
    public async Task A_binding_expression_with_unbounded_recursion_fails_the_evaluation()
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IPortableExpressionEvaluator>();
        var definition = new ExpressionDefinition(
            "JavaScript",
            "(function f(n) { return f(n + 1); })(0)",
            new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding>(),
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingReadonlyV1);
        var request = new ExpressionEvaluationRequest(definition, new Dictionary<string, JsonElement>(), CancellationToken.None);

        var exception = await RunOnSmallStackAsync(() =>
            Record.Exception(() => evaluator.EvaluateAsync(request).AsTask().GetAwaiter().GetResult()));

        Assert.IsType<RecursionDepthOverflowException>(exception);
    }

    [Fact]
    public async Task An_evaluation_runs_on_an_evaluation_thread_and_a_nested_one_runs_inline()
    {
        var caller = Environment.CurrentManagedThreadId;

        var (outer, inner, nestedOnEvaluationThread) = await Task.Run(() => JintEvaluationThreads.Run(() =>
        {
            var outerThread = Environment.CurrentManagedThreadId;
            var innerThread = JintEvaluationThreads.Run(() => Environment.CurrentManagedThreadId);
            return (outerThread, innerThread, JintEvaluationThreads.IsEvaluationThread);
        }));

        Assert.NotEqual(caller, outer);
        Assert.Equal(outer, inner);
        Assert.True(nestedOnEvaluationThread);
        Assert.False(JintEvaluationThreads.IsEvaluationThread);
    }

    [Fact]
    public void An_evaluation_sees_the_callers_execution_context_and_rethrows_its_exception()
    {
        var local = new AsyncLocal<string> { Value = "caller" };

        Assert.Equal("caller", JintEvaluationThreads.Run(() => local.Value));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            JintEvaluationThreads.Run<int>(() => throw new InvalidOperationException("from the evaluation")));
        Assert.Equal("from the evaluation", exception.Message);
    }

    private static void AssertStackLimitError(Exception? exception)
    {
        if (exception is RecursionDepthOverflowException)
            return;
        var javaScript = Assert.IsType<JavaScriptException>(exception);
        Assert.Equal(JintEvaluationThreads.StackBudgetExceededMessage, javaScript.Message);
    }

    private static async Task<Exception?> EvaluateScriptFromSmallStackAsync(
        string source,
        Action<JintFeature>? configure = null,
        Action<IServiceCollection>? services = null) =>
        (await EvaluateScriptValueFromSmallStackAsync(source, configure, services)).Exception;

    private static async Task<(JsonElement? Value, Exception? Exception)> EvaluateScriptValueFromSmallStackAsync(
        string source,
        Action<JintFeature>? configure = null,
        Action<IServiceCollection>? services = null)
    {
        var feature = new JintFeature();
        configure?.Invoke(feature);
        var collection = new ServiceCollection();
        feature.ConfigureServices(collection);
        services?.Invoke(collection);
        await using var provider = collection.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ScriptEvaluator();
        var request = new JavaScriptScriptEvaluationRequest(source, JsonSerializer.SerializeToElement(new { }), CancellationToken.None);

        return await RunOnSmallStackAsync<(JsonElement?, Exception?)>(() =>
        {
            try
            {
                return (evaluator.EvaluateAsync(request).AsTask().GetAwaiter().GetResult().Value, null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        });
    }

    private static Task<T> RunOnSmallStackAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, CallerStackSize);
        thread.Start();
        return completion.Task;
    }
}
