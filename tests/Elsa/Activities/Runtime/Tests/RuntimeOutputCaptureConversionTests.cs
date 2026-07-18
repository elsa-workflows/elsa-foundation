using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class RuntimeOutputCaptureConversionTests
{
    [Fact]
    public async Task Project_applies_the_pinned_plan_before_persisting_the_target_value()
    {
        var sourceType = new ValueTypeDescriptor("UInt32");
        var targetType = new ValueTypeDescriptor("Int64");
        var descriptor = JsonSerializer.SerializeToElement(new { type = "test.counter" });
        var contract = new ActivityContract(
            "test.counter",
            "1",
            "test",
            descriptor,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Test.CountResult"),
                true,
                ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("count", "count", sourceType, true, ActivityValuePolicy.Default)]),
            ["Done"],
            new ActivityActivationRequirement("test", "test.counter"));
        var transition = ActivityTransition.Complete(new CountResult(7U));
        var completion = new ActivityCompletionProjector().Project(
            "invocation",
            new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, DateTimeOffset.UnixEpoch),
            contract,
            transition,
            DateTimeOffset.UnixEpoch);
        var plan = new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.TypedValue,
            sourceType,
            targetType,
            ValueConversionMode.Auto,
            ValueConversionOperation.NumericWidening,
            profile: null,
            limits: null,
            options: null);
        var capture = new RuntimeOutputCapture(
            "count",
            "variable-total",
            new RuntimeValueTypeDescriptor("Int64", WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            conversionPlan: plan);
        var node = new ExecutableNode(
            "counter",
            "counter",
            "test.counter",
            "1",
            "test",
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["count"] = capture });
        var drivers = new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]);

        var changes = await new RuntimeOutputCaptureProjector(drivers).ProjectAsync(
            "workflow",
            "activity",
            node,
            (IActivityCompletionTransition)transition,
            completion,
            DateTimeOffset.UnixEpoch);

        var state = Assert.Single(changes).State!;
        Assert.Equal("Int64", state.Type.Kind);
        Assert.Equal(7U, state.InlineValue!.Value.GetUInt32());
    }

    private sealed record CountResult(uint Count);
}
