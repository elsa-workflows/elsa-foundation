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
            Id = "script-1",
            NodeId = "script-node",
            Script = "return { answer: args.left + args.right };",
            Arguments = JsonSerializer.SerializeToElement(new { left = 40, right = 2 })
        };

        var transition = await ExecuteAsync(activity, scope.ServiceProvider);

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
            Id = "script-1",
            NodeId = "script-node",
            Script = "   "
        };

        var transition = await ExecuteAsync(activity, scope.ServiceProvider);

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
            Id = "script-1",
            NodeId = "script-node",
            Script = "while (true) { }"
        };

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await ExecuteAsync(activity, scope.ServiceProvider));
    }

    [Fact]
    public async Task Program_cannot_mutate_pinned_arguments_or_discover_workflow_hosts()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var arguments = JsonSerializer.SerializeToElement(new { order = new { total = 42 } });
        var activity = new RunJavaScript(scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>())
        {
            Id = "script-1",
            NodeId = "script-node",
            Script = "try { args.order.total = 0; } catch { } return [args.order.total, typeof getVariable, typeof services];",
            Arguments = arguments
        };

        var transition = await ExecuteAsync(activity, scope.ServiceProvider);

        var result = Assert.IsAssignableFrom<IActivityCompletionTransition<RunJavaScriptResult>>(transition).Result.Value!.Value;
        Assert.Equal(42, result[0].GetInt32());
        Assert.Equal("undefined", result[1].GetString());
        Assert.Equal("undefined", result[2].GetString());
        Assert.Equal(42, arguments.GetProperty("order").GetProperty("total").GetInt32());
    }

    private static async ValueTask<ActivityTransition> ExecuteAsync(RunJavaScript activity, IServiceProvider services)
    {
        var context = new SimpleActivityExecutionContext(activity, CancellationToken.None);
        return await ((IActivity)activity).ExecuteAsync(context);
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
