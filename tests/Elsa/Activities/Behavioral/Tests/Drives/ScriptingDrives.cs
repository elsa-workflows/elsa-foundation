using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Primitives;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// RunJavaScript: Done with no authored outcomes, and — with <c>PossibleOutcomes</c> authored — the port the
/// script's return value names plus the unmatched catch-all (#1114). The dynamic ports are per-node data the
/// publish compiler pins, so the drive supplies them on the node's contract exactly as the compiler would.
/// </summary>
public sealed class RunJavaScriptDrive : IActivityDrive
{
    public Type ActivityType => typeof(RunJavaScript);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        // No authored outcomes: the implicit Done completion, with the result output populated.
        await RunAsync(recorder, "return { answer: 42 };", possibleOutcomes: null);

        // Authored outcomes: the script routes by returning a declared name, then an undeclared one.
        await RunAsync(recorder, "return 'approved';", ["approved", "rejected"], expectedOutcome: "approved");
        await RunAsync(recorder, "return 'nope';", ["approved", "rejected"], expectedOutcome: RunJavaScript.UnmatchedOutcome);
    }

    private async Task RunAsync(
        ActivityDriveRecorder recorder,
        string script,
        string[]? possibleOutcomes,
        string? expectedOutcome = null)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.JavaScript.JavaScriptFeature().ConfigureServices(services))
            .WithFeature(services => new Expressions.JavaScript.Jint.JintFeature().ConfigureServices(services))
            .WithFeature(services => Microsoft.Extensions.DependencyInjection.MemoryCacheServiceCollectionExtensions.AddMemoryCache(services))
            .Build("actexec-js");

        // 'arguments' is bound explicitly rather than left to the activity default: an unbound JsonElement input
        // resolves to default(JsonElement), which the runtime cannot clone into a value envelope.
        var inputs = new Dictionary<string, object?>
        {
            ["script"] = script,
            ["arguments"] = System.Text.Json.JsonSerializer.SerializeToElement(new { })
        };
        if (possibleOutcomes is not null)
            inputs[RunJavaScript.PossibleOutcomesInputKey] = possibleOutcomes;

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            Nodes.Leaf("node-js", typeof(RunJavaScript), inputs)));

        recorder.Record(ActivityType, run, "node-js");

        if (expectedOutcome is not null)
            recorder.RecordOutcome(ActivityType, expectedOutcome);
    }
}
