using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Issue #5687 end to end, through the real engine, the real JavaScript evaluators and Run JavaScript: a script or an
/// input expression whose result is a Promise yields the value the Promise settled with, as in Elsa 3, instead of a
/// silent <c>{}</c>; a rejected Promise faults the step.
/// </summary>
public sealed class PromiseResultRunTests
{
    [Fact]
    public async Task A_script_returning_an_async_result_completes_with_the_settled_value()
    {
        await using var harness = NewHarness();
        var run = await RunAsync(harness, "return (async () => { await null; return { customer: 'acme' }; })();", ArgumentsLiteral());

        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-js");
        Assert.Equal("""{"customer":"acme"}""", state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetRawText());
    }

    [Fact]
    public async Task An_input_expression_returning_a_Promise_passes_the_settled_value_to_the_step()
    {
        await using var harness = NewHarness();
        var run = await RunAsync(harness, "return args.customer;", ArgumentsExpression("(async () => ({ customer: 'acme' }))()"));

        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-js");
        Assert.Equal("\"acme\"", state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetRawText());
    }

    [Fact]
    public async Task A_script_returning_a_rejected_Promise_faults_the_step()
    {
        await using var harness = NewHarness();
        var run = await RunAsync(harness, "return Promise.reject(new Error('acme failed'));", ArgumentsLiteral());

        var state = run.State("node-js");
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Contains("acme failed", state.Fault!.Message, StringComparison.Ordinal);
    }

    private static WorkflowExecutionHarness NewHarness() => WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
            })
            .Build("actexec-js-promise");

    private static async Task<WorkflowExecutionRun> RunAsync(WorkflowExecutionHarness harness, string script, RuntimeInputBinding arguments)
    {
        var node = new ExecutableNode(
            executableNodeId: "node-js",
            authoredActivityId: "authored-js",
            activityType: typeof(RunJavaScript).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["script"] = Literal("script", "String", JsonSerializer.SerializeToElement(script)),
                ["arguments"] = arguments
            },
            metadata: new Dictionary<string, string>());

        return await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(node),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);
    }

    private static RuntimeInputBinding ArgumentsLiteral() => Literal("arguments", "JsonElement", JsonSerializer.SerializeToElement(new { }));

    private static RuntimeInputBinding Literal(string key, string alias, JsonElement value)
    {
        var type = new ValueTypeDescriptor(alias);
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(type, value, policy));
    }

    private static RuntimeInputBinding ArgumentsExpression(string source)
    {
        var type = new ValueTypeDescriptor("JsonElement");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            "arguments",
            type,
            policy,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                source,
                new RuntimeValueTypeDescriptor("alias", "JsonElement", null),
                parameters: new Dictionary<string, ExpressionParameterBinding>(),
                options: JsonSerializer.SerializeToElement(new { }),
                capabilityProfile: ExpressionCapabilityProfiles.BindingReadonlyV1));
    }
}
