using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scripting.Tests;

public sealed class RunJavaScriptContractTests
{
    [Fact]
    public void Uses_plain_inputs_constructor_injection_and_one_typed_result()
    {
        Assert.True(typeof(RunJavaScript).IsSubclassOf(typeof(Activity<RunJavaScriptResult>)));
        Assert.NotNull(typeof(RunJavaScript).GetConstructor([typeof(IJavaScriptScriptEvaluator)]));

        var script = typeof(RunJavaScript).GetProperty(nameof(RunJavaScript.Script))!;
        var arguments = typeof(RunJavaScript).GetProperty(nameof(RunJavaScript.Arguments))!;
        Assert.Equal(typeof(string), script.PropertyType);
        Assert.Equal(typeof(JsonElement), arguments.PropertyType);
        Assert.NotNull(script.GetCustomAttributes(typeof(ActivityInputAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(arguments.GetCustomAttributes(typeof(ActivityInputAttribute), inherit: true).SingleOrDefault());
        Assert.All(typeof(RunJavaScript).GetProperties(), property =>
            Assert.False(property.PropertyType.Name.EndsWith("Argument", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Returns_a_frozen_typed_result_without_a_workflow_value_writer()
    {
        var evaluator = new RecordingScriptEvaluator(JsonSerializer.SerializeToElement(new { total = 42 }));
        var activity = new RunJavaScript(evaluator)
        {
            Id = "invocation-1",
            NodeId = "script-node",
            Script = "return { total: args.amount * 2 };",
            Arguments = JsonSerializer.SerializeToElement(new { amount = 21 })
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new SimpleActivityExecutionContext(services, activity, CancellationToken.None);

        var transition = await ((IActivity)activity).ExecuteAsync(context);

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<RunJavaScriptResult>>(transition);
        Assert.Equal(42, completion.Result.Value!.Value.GetProperty("total").GetInt32());
        Assert.Equal(21, evaluator.Request!.Arguments.GetProperty("amount").GetInt32());
    }

    private sealed class RecordingScriptEvaluator(JsonElement result) : IJavaScriptScriptEvaluator
    {
        public JavaScriptScriptEvaluationRequest? Request { get; private set; }

        public ValueTask<JsonElement?> EvaluateAsync(JavaScriptScriptEvaluationRequest request)
        {
            Request = request;
            return ValueTask.FromResult<JsonElement?>(result.Clone());
        }
    }

}
