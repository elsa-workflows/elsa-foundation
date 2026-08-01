using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using CronActivity = Elsa.Activities.Scheduling.Activities.Cron;
using TimerActivity = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// Drives the fourth assertion of the behavioural brief: a required input, when absent, must fault <em>cleanly</em> —
/// the run ends Faulted rather than completing as if nothing were wrong, or hanging.
/// </summary>
/// <remarks>
/// <para>
/// Every case is the same shape — build the activity's node with the required input simply not bound, run it, and
/// read the committed state — so they live together here rather than being sprinkled through the per-module drives.
/// The only thing that varies is the feature wiring each activity needs, which is what <c>Harness</c> supplies.
/// </para>
/// <para>
/// Nothing is asserted about <em>how</em> it faults. The contract being measured is "absence is not silently
/// tolerated", not a particular exception type or message.
/// </para>
/// </remarks>
public sealed class RequiredInputDrive : IActivityDrive
{
    // This drive spans activities, so its own ActivityType is only used for failure attribution. Each case records
    // against the activity it actually exercised.
    public Type ActivityType => typeof(RequiredInputDrive);

    // Only the feature wiring varies per activity. The input KEYS are read from the contract rather than restated
    // here: a hand-written key that drifts from the real one (WriteLine's is "text", not "Text") would silently
    // record enforcement against a key nothing asserts, and the assertion would fail for the wrong reason.
    private static readonly (Type Activity, Func<WorkflowExecutionHarness> Harness)[] Cases =
    [
        (typeof(WriteLine), () => Harness()),
        (typeof(PublishEvent), () => Harness(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))),
        (typeof(Event), () => Harness(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))),
        (typeof(RunJavaScript), ScriptingHarness),
        (typeof(TimerActivity), SchedulingHarnessForRequiredInput),
        (typeof(CronActivity), SchedulingHarnessForRequiredInput),
        (typeof(HttpEndpoint), HttpHarness),
        (typeof(SendHttpRequest), HttpHarness)
    ];

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        // Each case is independent: one activity's outcome must not decide whether the rest are measured at all.
        foreach (var (activity, harnessFactory) in Cases)
        {
            var inputKey = Xunit.Assert.Single(ActivityContractInventory.RequiredInputs(activity));
            await using var harness = harnessFactory();
            const string nodeId = "node-missing-required-input";

            // The input is left unbound entirely — the harness then binds the property's own default, which is what
            // an author who never filled the field in would get.
            var node = Nodes.Leaf(nodeId, activity);

            try
            {
                var run = await harness.RunAsync(
                    WorkflowExecutionHarness.NewExecutable(node),
                    allowPendingWorkOnTerminalCompletion: true);
                recorder.RecordRequiredInputFault(activity, inputKey, run, nodeId);
            }
            catch (InvalidOperationException rejection)
            {
                // The value-flow guard (VF-ACT-004) refuses the start command outright, so there is no run to read.
                // That is the contract being kept, at an earlier depth than a mid-run fault — not a broken drive.
                recorder.RecordRequiredInputFault(activity, inputKey, run: null, nodeId, rejection);
            }
        }
    }

    private static WorkflowExecutionHarness Harness(params Action<IServiceCollection>[] features)
    {
        var builder = WorkflowExecutionHarness.Create();
        foreach (var feature in features)
            builder = builder.WithFeature(feature);
        return builder.Build("actexec-required-input");
    }

    private static WorkflowExecutionHarness ScriptingHarness() => Harness(
        services => new SerializationFeature().ConfigureServices(services),
        services => new ActivitiesPrimitivesFeature().ConfigureServices(services),
        services => new ActivitiesScriptingFeature().ConfigureServices(services),
        services => new Expressions.JavaScript.JavaScriptFeature().ConfigureServices(services),
        services => new Expressions.JavaScript.Jint.JintFeature().ConfigureServices(services),
        services => services.AddMemoryCache());

    private static WorkflowExecutionHarness SchedulingHarnessForRequiredInput() => Harness(
        services => new SerializationFeature().ConfigureServices(services),
        services => new ActivitiesPrimitivesFeature().ConfigureServices(services),
        services => new Workflows.Runtime.Scheduling.WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    private static WorkflowExecutionHarness HttpHarness() => Harness(
        services => new SerializationFeature().ConfigureServices(services),
        services => new ActivitiesPrimitivesFeature().ConfigureServices(services),
        services => new ActivitiesHttpFeature().ConfigureServices(services),
        services => services.AddSingleton<Elsa.Http.Core.Contracts.IHttpRequestBodyParser, Elsa.Http.Services.HttpRequestBodyParser>());
}
