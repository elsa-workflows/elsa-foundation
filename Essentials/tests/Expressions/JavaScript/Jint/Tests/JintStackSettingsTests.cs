using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed class JintStackSettingsTests
{
    [Theory]
    [InlineData("script")]
    [InlineData("binding")]
    public void Each_production_evaluator_uses_its_hosts_stack_settings(string surface)
    {
        foreach (var (stack, budget) in new[] { (24 * 1024 * 1024, 128 * 1024), (40 * 1024 * 1024, 256 * 1024) })
        {
            var services = new ServiceCollection();
            new JintFeature { EvaluationThreadStackBytes = stack, EvaluationStackBudgetBytes = budget }.ConfigureServices(services);
            services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.stack", _ =>
                JsonSerializer.SerializeToElement(new { stack = JintEvaluationThreads.CurrentStackSize, budget = JintEvaluationThreads.CurrentBudget })));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var result = JintCrashProbe.Evaluate(scope, surface, "return acme.stack();");
            Assert.Equal(stack, result.GetProperty("stack").GetInt32());
            Assert.Equal(budget, result.GetProperty("budget").GetInt64());
        }
    }

    [Theory]
    [InlineData(16 * 1024 * 1024 - 1, 512 * 1024)]
    [InlineData(64 * 1024 * 1024 + 1, 512 * 1024)]
    [InlineData(16 * 1024 * 1024, 64 * 1024 - 1)]
    [InlineData(64 * 1024 * 1024, 512 * 1024 + 1)]
    public void Unsafe_settings_are_rejected_before_an_evaluator_is_resolved(int stack, long budget)
    {
        using var provider = JintTestHost.Build(feature =>
        {
            feature.EvaluationThreadStackBytes = stack;
            feature.EvaluationStackBudgetBytes = budget;
        });
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<FeatureOptions>>().Value);
    }

    [Fact]
    public void Reused_workers_keep_the_requested_stack_and_each_hosts_budget()
    {
        var options = new FeatureOptions { EvaluationThreadStackBytes = 24 * 1024 * 1024, EvaluationStackBudgetBytes = 128 * 1024 };
        var first = JintEvaluationThreads.Run(() =>
            (Environment.CurrentManagedThreadId, JintEvaluationThreads.CurrentStackSize, JintEvaluationThreads.CurrentBudget), options);
        options.EvaluationStackBudgetBytes = 256 * 1024;
        var second = JintEvaluationThreads.Run(() =>
            (Environment.CurrentManagedThreadId, JintEvaluationThreads.CurrentStackSize, JintEvaluationThreads.CurrentBudget), options);
        Assert.Equal(first.Item1, second.Item1);
        Assert.Equal(options.EvaluationThreadStackBytes, first.Item2);
        Assert.Equal(options.EvaluationThreadStackBytes, second.Item2);
        Assert.Equal(128 * 1024, first.Item3);
        Assert.Equal(256 * 1024, second.Item3);
    }

    [Theory]
    [InlineData(16 * 1024 * 1024)]
    [InlineData(32 * 1024 * 1024)]
    public void Nested_evaluations_keep_the_outer_budget_even_on_a_larger_worker(int nestedStack)
    {
        var outer = new FeatureOptions { EvaluationStackBudgetBytes = 128 * 1024 };
        var inner = new FeatureOptions { EvaluationThreadStackBytes = nestedStack };
        JintEvaluationThreads.Run(() =>
        {
            var thread = Environment.CurrentManagedThreadId;
            var before = JintEvaluationThreads.CurrentBudget;
            JintEvaluationThreads.Run(() =>
            {
                Assert.InRange(JintEvaluationThreads.CurrentBudget, 1, before);
                Assert.True(JintEvaluationThreads.CurrentStackSize >= nestedStack);
                Assert.Equal(nestedStack == outer.EvaluationThreadStackBytes, thread == Environment.CurrentManagedThreadId);
                return 0;
            }, inner);
            Assert.Equal(before, JintEvaluationThreads.CurrentBudget);
            return 0;
        }, outer);
    }

    [Fact]
    public void A_tighter_nested_budget_is_restored_after_an_exception()
    {
        JintEvaluationThreads.Run(() =>
        {
            var before = JintEvaluationThreads.CurrentBudget;
            Assert.Throws<InvalidOperationException>(() => JintEvaluationThreads.Run<int>(() =>
            {
                Assert.True(JintEvaluationThreads.CurrentBudget < before);
                throw new InvalidOperationException();
            }, new FeatureOptions { EvaluationStackBudgetBytes = 64 * 1024 }));
            Assert.Equal(before, JintEvaluationThreads.CurrentBudget);
            return 0;
        });
    }

    [Fact]
    public async Task Catching_a_statement_free_stack_error_does_not_corrupt_later_calls()
    {
        using var provider = JintTestHost.Build(f => f.MaxRecursionDepth = null);
        using var scope = provider.CreateScope();
        var result = await scope.ScriptEvaluator().EvaluateAsync(new JavaScriptScriptEvaluationRequest(
            "function f(x = f()) {} var caught = 0; for(var i = 0; i < 3; i++) { try { f(); } catch(e) { if (e instanceof RangeError) caught++; else throw e; } } " +
            "function sum(a, b = 2) { return a + b; } return caught + sum(1);",
            JsonSerializer.SerializeToElement(new { }), CancellationToken.None));
        Assert.Equal(6, result.Value!.Value.GetInt32());
    }
}
