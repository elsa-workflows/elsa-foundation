using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Exceptions;
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

        var state = Assert.Single(changes.DurableValues).State!;
        Assert.Equal("Int64", state.Type.Kind);
        Assert.Equal(7U, state.InlineValue!.Value.GetUInt32());
    }

    [Fact]
    public async Task Project_rejects_transient_resources_before_durable_capture_state_is_written()
    {
        var type = new ValueTypeDescriptor("String");
        var (node, transition, completion) = NewCaptureScenario(
            type,
            new ValueConversionPlan(
                ValueConversionPlan.CurrentSchemaVersion,
                ValueRepresentation.TransientResource,
                type,
                type,
                ValueConversionMode.Auto,
                ValueConversionOperation.Identity,
                profile: null,
                limits: null,
                options: null));
        var drivers = new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeOutputCaptureProjector(drivers).ProjectAsync(
                "workflow",
                "activity",
                node,
                (IActivityCompletionTransition)transition,
                completion,
                DateTimeOffset.UnixEpoch).AsTask());

        Assert.Contains("VF-ACT-005", exception.Message, StringComparison.Ordinal);
        Assert.Contains("TransientResource", exception.Message, StringComparison.Ordinal);
        Assert.Contains("destination storage policy 'Custom'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("execution-local activity-result binding", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DurableReference/resource-handle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_allows_durable_capture_when_mixed_transient_capture_is_not_selected()
    {
        var countType = new ValueTypeDescriptor("UInt32");
        var streamType = new ValueTypeDescriptor("Stream");
        var descriptor = JsonSerializer.SerializeToElement(new { type = "test.mixed" });
        var contract = new ActivityContract(
            "test.mixed",
            "1",
            "test",
            descriptor,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Test.MixedResult"),
                true,
                ActivityValuePolicy.Default,
                [
                    new ActivityResultProjectionContract("count", "count", countType, true, ActivityValuePolicy.Default),
                    new ActivityResultProjectionContract("stream", "stream", streamType, true, ActivityValuePolicy.Default, ValueRepresentation.TransientResource)
                ]),
            ["Done"],
            new ActivityActivationRequirement("test", "test.mixed"));
        var transition = ActivityTransition.Complete(new MixedResult(7U, "execution-local-stream-placeholder"));
        var completion = new ActivityCompletionProjector().Project(
            "invocation",
            new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, DateTimeOffset.UnixEpoch),
            contract,
            transition,
            DateTimeOffset.UnixEpoch);
        var node = new ExecutableNode(
            "mixed",
            "mixed",
            "test.mixed",
            "1",
            "test",
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                ["count"] = new RuntimeOutputCapture(
                    "count",
                    "variable-count",
                    new RuntimeValueTypeDescriptor("UInt32", WellKnownRuntimeDurableValueStorageDrivers.Json, null),
                    DurableValueLifecycle.Instance,
                    DurableValueStorage.Custom,
                    captureOnSuccessfulCompletion: true),
                ["stream"] = new RuntimeOutputCapture(
                    "stream",
                    "variable-stream",
                    new RuntimeValueTypeDescriptor("Stream", WellKnownRuntimeDurableValueStorageDrivers.Json, null),
                    DurableValueLifecycle.Instance,
                    DurableValueStorage.Custom,
                    captureOnSuccessfulCompletion: false,
                    conversionPlan: new ValueConversionPlan(
                        ValueConversionPlan.CurrentSchemaVersion,
                        ValueRepresentation.TransientResource,
                        streamType,
                        streamType,
                        ValueConversionMode.Auto,
                        ValueConversionOperation.Identity,
                        profile: null,
                        limits: null,
                        options: null))
            });

        var changes = await new RuntimeOutputCaptureProjector(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]))
            .ProjectAsync(
                "workflow",
                "activity",
                node,
                (IActivityCompletionTransition)transition,
                completion,
                DateTimeOffset.UnixEpoch);

        var state = Assert.Single(changes.DurableValues).State!;
        Assert.Equal("variable-count", state.ValueId);
        Assert.Equal(7U, state.InlineValue!.Value.GetUInt32());
    }

    [Fact]
    public async Task Project_applies_registered_typed_json_plan_before_persisting_the_target_value()
    {
        var sourceType = new ValueTypeDescriptor("String");
        var targetType = new ValueTypeDescriptor("Acme.Customer");
        var plan = new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            sourceType,
            targetType,
            ValueConversionMode.Auto,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"),
            limits: null,
            options: null);

        var state = await ProjectCustomerBodyAsync("""{"name":"Ada","loyaltyPoints":42}""", plan);

        Assert.Equal("Acme.Customer", state.Type.Kind);
        Assert.Equal("Ada", state.InlineValue!.Value.GetProperty("name").GetString());
        Assert.Equal(42, state.InlineValue.Value.GetProperty("loyaltyPoints").GetInt64());
    }

    [Fact]
    public async Task Project_applies_registered_typed_xml_plan_before_persisting_the_target_value()
    {
        var sourceType = new ValueTypeDescriptor("String");
        var targetType = new ValueTypeDescriptor("Acme.Customer");
        var plan = new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            sourceType,
            targetType,
            ValueConversionMode.Xml,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.xml", "1"),
            limits: null,
            options: null);

        var state = await ProjectCustomerBodyAsync("<customer loyaltyPoints=\"42\"><name>Ada</name></customer>", plan);

        Assert.Equal("Acme.Customer", state.Type.Kind);
        Assert.Equal("Ada", state.InlineValue!.Value.GetProperty("name").GetString());
        Assert.Equal(42, state.InlineValue.Value.GetProperty("loyaltyPoints").GetInt64());
    }

    private static async Task<DurableValueState> ProjectCustomerBodyAsync(string body, ValueConversionPlan plan)
    {
        var sourceType = new ValueTypeDescriptor("String");
        var descriptor = JsonSerializer.SerializeToElement(new { type = "test.customer" });
        var contract = new ActivityContract(
            "test.customer",
            "1",
            "test",
            descriptor,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Test.CustomerResult"),
                true,
                ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("body", "body", sourceType, true, ActivityValuePolicy.Default, ValueRepresentation.FormattedContent)]),
            ["Done"],
            new ActivityActivationRequirement("test", "test.customer"));
        var transition = ActivityTransition.Complete(new CustomerResult(body));
        var completion = new ActivityCompletionProjector().Project(
            "invocation",
            new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, DateTimeOffset.UnixEpoch),
            contract,
            transition,
            DateTimeOffset.UnixEpoch);
        var capture = new RuntimeOutputCapture(
            "body",
            "variable-customer",
            new RuntimeValueTypeDescriptor(plan.TargetType.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            conversionPlan: plan);
        var node = new ExecutableNode(
            "customer",
            "customer",
            "test.customer",
            "1",
            "test",
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["body"] = capture });
        var drivers = new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]);
        var projector = new RuntimeOutputCaptureProjector(drivers, new RuntimeValueConversionExecutor(new TestTypeRegistry(
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                ["Acme.Customer"] = typeof(CustomerContract)
            })));

        var changes = await projector.ProjectAsync(
            "workflow",
            "activity",
            node,
            (IActivityCompletionTransition)transition,
            completion,
            DateTimeOffset.UnixEpoch);

        return Assert.Single(changes.DurableValues).State!;
    }

    private static (ExecutableNode Node, ActivityTransition Transition, ActivityCompletionProjection Completion) NewCaptureScenario(
        ValueTypeDescriptor sourceType,
        ValueConversionPlan plan)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "test.echo" });
        var contract = new ActivityContract(
            "test.echo",
            "1",
            "test",
            descriptor,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Test.EchoResult"),
                true,
                ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("value", "value", sourceType, true, ActivityValuePolicy.Default)]),
            ["Done"],
            new ActivityActivationRequirement("test", "test.echo"));
        var transition = ActivityTransition.Complete(new EchoResult("raw"));
        var completion = new ActivityCompletionProjector().Project(
            "invocation",
            new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, DateTimeOffset.UnixEpoch),
            contract,
            transition,
            DateTimeOffset.UnixEpoch);
        var capture = new RuntimeOutputCapture(
            "value",
            "variable-value",
            new RuntimeValueTypeDescriptor(sourceType.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            conversionPlan: plan);
        var node = new ExecutableNode(
            "echo",
            "echo",
            "test.echo",
            "1",
            "test",
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["value"] = capture });

        return (node, transition, completion);
    }

    private sealed record CountResult(uint Count);
    private sealed record EchoResult(string Value);
    private sealed record CustomerResult(string Body);
    private sealed record MixedResult(uint Count, string Stream);

    private sealed class TestTypeRegistry(IReadOnlyDictionary<string, Type> aliases) : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias)
        {
            var match = aliases.SingleOrDefault(item => item.Value == type);
            alias = match.Key!;
            return match.Value is not null;
        }

        public bool TryGetType(string alias, out Type type) => aliases.TryGetValue(alias, out type!);
        public IEnumerable<Type> ListTypes() => aliases.Values;
        public string GetAliasOrDefault(Type type) => TryGetAlias(type, out var alias) ? alias : type.FullName!;
        public Type GetTypeOrDefault(string alias) => TryGetType(alias, out var type) ? type : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }

    private sealed record CustomerContract
    {
        public required string Name { get; init; }
        public long LoyaltyPoints { get; init; }
    }
}
