using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scripting.Tests;

public sealed class RunJavaScriptExecutionTests
{
    [Fact]
    public async Task Evaluates_a_program_from_explicit_args_and_returns_one_typed_result()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>())
        {
            Script = "return { answer: args.left + args.right };",
            Arguments = JsonSerializer.SerializeToElement(new { left = 40, right = 2 })
        };

        var transition = await ExecuteAsync(activity);

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<RunJavaScriptResult>>(transition);
        Assert.Equal(42, completion.Result.Value!.Value.GetProperty("answer").GetInt32());
        Assert.Equal(new[] { "Done" }, completion.Selection.ResolveNames());
    }

    [Fact]
    public async Task Whitespace_program_completes_with_an_empty_typed_result()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>())
        {
            Script = "   "
        };

        var transition = await ExecuteAsync(activity);

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<RunJavaScriptResult>>(transition);
        Assert.Null(completion.Result.Value);
    }

    [Fact]
    public async Task Shared_statement_limit_faults_an_unbounded_program()
    {
        await using var provider = BuildProvider(maxStatements: 10_000);
        await using var scope = provider.CreateAsyncScope();
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>())
        {
            Script = "while (true) { }"
        };

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await ExecuteAsync(activity));
    }

    [Fact]
    public async Task Program_cannot_mutate_pinned_arguments_or_discover_workflow_hosts()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var arguments = JsonSerializer.SerializeToElement(new { order = new { total = 42 } });
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>())
        {
            Script = "try { args.order.total = 0; } catch { } return [args.order.total, typeof getVariable, typeof services];",
            Arguments = arguments
        };

        var transition = await ExecuteAsync(activity);

        var result = Assert.IsAssignableFrom<IActivityCompletionTransition<RunJavaScriptResult>>(transition).Result.Value!.Value;
        Assert.Equal(42, result[0].GetInt32());
        Assert.Equal("undefined", result[1].GetString());
        Assert.Equal("undefined", result[2].GetString());
        Assert.Equal(42, arguments.GetProperty("order").GetProperty("total").GetInt32());
    }

    // Spec 1208 (S-3), User Story 1: a script selects outcomes only by returning complete(result, outcomes).

    [Fact]
    public async Task Returning_complete_with_one_name_selects_that_outcome()
    {
        var completion = await RunAsync("return complete({ id: 7 }, 'Approved');", ["Approved", "Rejected", "Audit"]);

        Assert.Equal(OutcomeSelection.Named("Approved"), completion.Selection);
        Assert.Equal(7, completion.Result.Value!.Value.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Returning_complete_with_several_names_selects_each_once_in_order()
    {
        var completion = await RunAsync("return complete(1, ['Approved', 'Audit', 'Approved']);", ["Approved", "Rejected", "Audit"]);

        Assert.Equal(OutcomeSelection.Named("Approved", "Audit"), completion.Selection);
    }

    [Theory]
    [InlineData("return complete('order');")]
    [InlineData("return complete('order', undefined);")]
    [InlineData("return 'order';")]
    public async Task Selecting_nothing_leaves_the_selection_unspecified(string script)
    {
        var completion = await RunAsync(script, ["Approved"]);

        Assert.Equal(OutcomeSelection.Unspecified, completion.Selection);
        Assert.Equal(new[] { "Done" }, completion.Selection.ResolveNames());
        Assert.Equal("order", completion.Result.Value!.Value.GetString());
    }

    [Fact]
    public async Task Returning_complete_with_an_empty_array_is_an_explicitly_empty_selection()
    {
        var completion = await RunAsync("return complete('kept', []);", ["Approved"]);

        Assert.Equal(OutcomeSelection.Empty, completion.Selection);
        Assert.Empty(completion.Selection.ResolveNames());
        Assert.Equal("kept", completion.Result.Value!.Value.GetString());
    }

    /// <summary>US1 scenario 5 and FR-008: returned data is the result only; it never selects an outcome (#1114 is gone).</summary>
    [Theory]
    [InlineData("return 'Approved';")]
    [InlineData("return 404;")]
    [InlineData("return { result: 'x', outcomes: ['Approved'] };")]
    public async Task A_returned_value_never_selects_an_outcome(string script)
    {
        var completion = await RunAsync(script, ["Approved", "404"]);

        Assert.Equal(OutcomeSelection.Unspecified, completion.Selection);
    }

    [Fact]
    public async Task A_completion_returned_from_a_nested_function_selects()
    {
        var completion = await RunAsync(
            "function decide(order) { return complete(order, 'Rejected'); } return decide(args.total);",
            ["Approved", "Rejected"]);

        Assert.Equal(OutcomeSelection.Named("Rejected"), completion.Selection);
        Assert.Equal(250, completion.Result.Value!.Value.GetInt32());
    }

    [Fact]
    public async Task Calling_complete_without_returning_it_changes_nothing()
    {
        var completion = await RunAsync("complete('ignored', 'Approved'); complete(null, null); return 'data';", ["Approved"]);

        Assert.Equal(OutcomeSelection.Unspecified, completion.Selection);
        Assert.Equal("data", completion.Result.Value!.Value.GetString());
    }

    [Fact]
    public async Task Outcomes_are_snapshotted_when_complete_is_called()
    {
        var completion = await RunAsync("const o = ['Approved']; const c = complete(1, o); o.push('Audit'); return c;", ["Approved", "Audit"]);

        Assert.Equal(OutcomeSelection.Named("Approved"), completion.Selection);
    }

    /// <summary>FR-007a: a completion value counts only as the top-level returned value; nested in data it faults.</summary>
    [Theory]
    [InlineData("return { wrapped: complete(1, 'Approved') };")]
    [InlineData("return [complete(1)];")]
    [InlineData("return complete(complete(1, 'Approved'), 'Approved');")]
    public async Task A_nested_completion_value_faults_with_VF_ACT_006(string script)
    {
        var fault = await FaultAsync(script, ["Approved"]);

        Assert.Equal("VF-ACT-006", fault.Code);
    }

    /// <summary>FR-007b: outcomes are omitted, undefined, a name or an array of names; anything else faults.</summary>
    [Theory]
    [InlineData("return complete(1, null);")]
    [InlineData("return complete(1, 7);")]
    [InlineData("return complete(1, { name: 'Approved' });")]
    [InlineData("return complete(1, ['Approved', 7]);")]
    [InlineData("return complete(1, '  ');")]
    [InlineData("return complete(1, ['']);")]
    public async Task Invalid_outcomes_fault_with_VF_ACT_006(string script)
    {
        var fault = await FaultAsync(script, ["Approved"]);

        Assert.Equal("VF-ACT-006", fault.Code);
    }

    /// <summary>
    /// FR-007b: an undefined result follows the same boundary rule as a plain <c>return undefined</c>. Spec 1216 (S-2)
    /// has not introduced the undefined policies on this branch yet, so both read as no result today.
    /// </summary>
    [Theory]
    [InlineData("return complete(undefined, 'Approved');")]
    [InlineData("return complete();")]
    public async Task An_undefined_result_is_read_like_return_undefined(string script)
    {
        var plain = await RunAsync("return undefined;", ["Approved"]);
        var completion = await RunAsync(script, ["Approved"]);

        Assert.Equal(plain.Result.Value.HasValue, completion.Result.Value.HasValue);
        Assert.Null(completion.Result.Value);
    }

    /// <summary>US1 scenario 6 / FR-005: a fault after selecting discards the selection; nothing is completed.</summary>
    [Fact]
    public async Task A_fault_after_selecting_completes_nothing()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>())
        {
            Script = "const c = complete(1, 'Approved'); throw new Error('boom');",
            PossibleOutcomes = ["Approved"]
        };

        await Assert.ThrowsAnyAsync<Exception>(async () => await ExecuteAsync(activity));
    }

    [Fact]
    public async Task Complete_cannot_be_reassigned_by_the_script()
    {
        var completion = await RunAsync("try { complete = () => 1; } catch { } return complete(2, 'Approved');", ["Approved"]);

        Assert.Equal(OutcomeSelection.Named("Approved"), completion.Selection);
    }

    private static async Task<IActivityCompletionTransition<RunJavaScriptResult>> RunAsync(
        string script,
        ICollection<string>? possibleOutcomes)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>())
        {
            Script = script,
            Arguments = JsonSerializer.SerializeToElement(new { total = 250 }),
            PossibleOutcomes = possibleOutcomes
        };

        return Assert.IsAssignableFrom<IActivityCompletionTransition<RunJavaScriptResult>>(await ExecuteAsync(activity));
    }

    private static async Task<ActivityFault> FaultAsync(string script, ICollection<string>? possibleOutcomes)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>())
        {
            Script = script,
            PossibleOutcomes = possibleOutcomes
        };

        return Assert.IsAssignableFrom<IActivityFaultTransition>(await ExecuteAsync(activity)).Fault;
    }

    private static async ValueTask<ActivityTransition> ExecuteAsync(RunJavaScript activity)
    {
        var context = new SimpleActivityExecutionContext(
            activity,
            CancellationToken.None,
            invocationId: "script-1",
            executableNodeId: "script-node");
        return await ((IActivity)activity).ExecuteAsync(context.ToActivityExecutionContext());
    }

    private static ServiceProvider BuildProvider(int? maxStatements = null)
    {
        var services = new ServiceCollection();
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        services.AddMemoryCache();
        if (maxStatements is not null)
        {
            services.Configure<Elsa.Expressions.JavaScript.Jint.Options.FeatureOptions>(options =>
                options.MaxStatements = maxStatements.Value);
        }

        return services.BuildServiceProvider();
    }
}
