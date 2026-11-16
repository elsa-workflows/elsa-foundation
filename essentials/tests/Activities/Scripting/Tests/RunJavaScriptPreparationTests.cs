using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Activities.Scripting.Tests;

public sealed class RunJavaScriptPreparationTests(ITestOutputHelper output)
{
    [Fact]
    public void Fresh_hydrated_activities_reuse_equal_script_content_with_current_arguments()
    {
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>();
        var hydrator = new ActivityInputHydrator();
        var source = string.Join("\n", Enumerable.Range(0, 256).Select(i => $"const v{i} = args.value + {i};")) + "\nreturn v255;";
        var type = new ValueTypeDescriptor("String");
        var contract = new ActivityContract("test/script", "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
            [new ActivityInputContract("script", "Script", type, true, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Object"), true, ActivityValuePolicy.Default, []),
            ["Done"], new ActivityActivationRequirement("test", "test/script"));
        ActivityInputSnapshot Snapshot(string text) => new("invocation", contract.SchemaFingerprint, "bindings",
            new Dictionary<string, ValueEnvelope> { ["script"] = ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(text), ValueProtectionPolicy.InstanceInline) }, DateTimeOffset.UnixEpoch);
        var warmSnapshot = Snapshot(source);
        var coldSnapshots = Enumerable.Range(0, 16).Select(i => Snapshot(source + $"\n/* unique:{Guid.NewGuid()} */")).ToArray();

        void Execute(ActivityInputSnapshot snapshot, int value)
        {
            var activity = new RunJavaScript(evaluator) { Script = "" };
            hydrator.Hydrate(activity, contract, snapshot);
            Assert.NotSame(source, activity.Script);
            activity.Arguments = JsonSerializer.SerializeToElement(new { value });
            var context = new SimpleActivityExecutionContext(activity, CancellationToken.None,
                invocationId: "invocation", executableNodeId: "script-node");
            var pending = ((IActivity)activity).ExecuteAsync(context.ToActivityExecutionContext());
            Assert.True(pending.IsCompleted);
            var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<RunJavaScriptResult>>(pending.GetAwaiter().GetResult());
            Assert.Equal(value + 255, completion.Result.Value!.Value.GetInt32());
        }
        Execute(warmSnapshot, 1);
        long Measure(bool reuse)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 16; i++) Execute(reuse ? warmSnapshot : coldSnapshots[i], i);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        var cold = Measure(false);
        var warm = Measure(true);
        output.WriteLine($"Fresh hydrated activity bytes: distinct content={cold}, repeated content={warm}.");
        Assert.True(warm < cold * .8, $"Fresh hydration missed preparation reuse: warm={warm}, cold={cold}.");
    }
}
