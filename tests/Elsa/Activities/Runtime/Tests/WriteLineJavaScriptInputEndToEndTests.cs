using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Regression gate for #921: a compiled executable whose activity input is bound to a JavaScript expression
/// (language "JavaScript", capability profile binding-pure-v1 — exactly what the publishing compiler emits for a
/// Studio-authored JS value) must evaluate that expression at runtime and run to completion. Drives the real
/// in-process agent + scheduler + drain + Jint evaluator through <see cref="WorkflowExecutionHarness"/> — no
/// hand-built resolution context — so a regression that leaves the runtime unable to evaluate a JS-bound input
/// (the reported "JavaScript expressions are entirely broken at runtime") is caught here.
/// </summary>
public sealed class WriteLineJavaScriptInputEndToEndTests
{
    [Fact]
    public async Task WriteLine_with_javascript_bound_text_input_evaluates_and_completes()
    {
        await using var harness = NewHarness("wl-exec");

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(WriteLineNode("node-wl", "\"a \" + 1")));

        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-wl");
        Assert.Equal("a 1", state.InputSnapshot!.Values["text"].InlineValue!.Value.GetString());
    }

    private static WorkflowExecutionHarness NewHarness(params string[] ids)
    {
        var harness = WorkflowExecutionHarness.Create()
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
            .Build(ids);

        using var scope = harness.Services.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

        return harness;
    }

    private static ExecutableNode WriteLineNode(string nodeId, string script)
    {
        // Leave the CLR contract unpinned: the harness reflects WriteLine's contract during Build and preserves
        // this Expression binding (see WorkflowExecutionHarness.NormalizeBinding).
        var binding = new RuntimeInputBinding(
            "text",
            new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", script));
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(WriteLine).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["text"] = binding },
            metadata: new Dictionary<string, string>());
    }
}
