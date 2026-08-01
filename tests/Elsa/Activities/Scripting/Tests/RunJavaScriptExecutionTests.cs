using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Workflows.Runtime.Core.Services;
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
        Assert.Equal("Done", completion.Outcome);
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

    /// <summary>
    /// #1114: with <c>PossibleOutcomes</c> authored, the script routes by returning. The value output is still
    /// populated on every branch — routing does not replace the result.
    /// </summary>
    [Theory]
    [InlineData("return 'approved';", "approved")]
    [InlineData("return 'rejected';", "rejected")]
    [InlineData("return args.total > 100 ? 'rejected' : 'approved';", "rejected")]
    public async Task Script_return_value_selects_a_declared_outcome(string script, string expectedOutcome)
    {
        var completion = await RunAsync(script, ["approved", "rejected"]);

        Assert.Equal(expectedOutcome, completion.Outcome);
        Assert.Equal(expectedOutcome, completion.Result.Value!.Value.GetString());
    }

    /// <summary>A return value that names no declared port takes the catch-all, exactly like an unmatched status code.</summary>
    [Theory]
    [InlineData("return 'something-else';")]
    [InlineData("return null;")]
    [InlineData("return { verdict: 'approved' };")]
    [InlineData("return ['approved'];")]
    [InlineData("")]
    public async Task Unrecognized_return_value_takes_the_unmatched_outcome(string script)
    {
        var completion = await RunAsync(script, ["approved", "rejected"]);

        Assert.Equal(RunJavaScript.UnmatchedOutcome, completion.Outcome);
    }

    /// <summary>Outcome names are identifiers: the match is ordinal, so casing is not forgiven.</summary>
    [Fact]
    public async Task Outcome_matching_is_ordinal()
    {
        var completion = await RunAsync("return 'Approved';", ["approved"]);

        Assert.Equal(RunJavaScript.UnmatchedOutcome, completion.Outcome);
    }

    /// <summary>A numeric return names a port by its textual form, mirroring SendHttpRequest's status-code ports.</summary>
    [Fact]
    public async Task Numeric_return_value_selects_a_numerically_named_outcome()
    {
        var completion = await RunAsync("return 404;", ["200", "404"]);

        Assert.Equal("404", completion.Outcome);
    }

    /// <summary>Without authored outcomes the activity keeps its single implicit Done completion.</summary>
    [Fact]
    public async Task Without_possible_outcomes_the_activity_always_completes_done()
    {
        var completion = await RunAsync("return 'approved';", possibleOutcomes: null);

        Assert.Equal("Done", completion.Outcome);
        Assert.Equal("approved", completion.Result.Value!.Value.GetString());
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
