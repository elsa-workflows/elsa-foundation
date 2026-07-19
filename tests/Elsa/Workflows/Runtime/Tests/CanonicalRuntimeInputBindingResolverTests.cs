using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CanonicalRuntimeInputBindingResolverTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");
    private readonly RuntimeInputBindingResolver _resolver = new();

    [Fact]
    public void Resolve_reads_a_pinned_workflow_request_member()
    {
        var binding = new RuntimeInputBinding(
            "customer-id",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("customer-id"));
        var context = NewContext(workflowInputs: new Dictionary<string, object?>
        {
            ["customer-id"] = JsonSerializer.SerializeToElement("customer-7")
        });

        var resolved = _resolver.Resolve(binding, context);

        Assert.Equal("customer-7", resolved.Envelope!.InlineValue!.Value.GetString());
    }

    [Fact]
    public void Resolve_projects_from_the_unique_committed_causal_result()
    {
        var producer = CompletedProducer();
        var consumer = RunningConsumer(producer.InvocationId);
        var executable = NewProducerExecutable();
        var binding = new RuntimeInputBinding(
            "customer-id",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer-id", "scope:root"));
        var context = NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer],
            executable: executable);

        var resolved = _resolver.Resolve(binding, context);

        Assert.Equal("customer-7", resolved.Envelope!.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Direct_result_conversion_preserves_the_pinned_source_contract_until_materialization()
    {
        var sourceType = new ValueTypeDescriptor("System.UInt32");
        var targetType = new ValueTypeDescriptor("System.Int64");
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
        var producer = CompletedProducer(ValueEnvelope.Inline(
            new ValueTypeDescriptor("test/result"),
            JsonSerializer.SerializeToElement(new { customerId = 7U }),
            ValueProtectionPolicy.InstanceInline));
        var consumer = RunningConsumer(producer.InvocationId);
        var binding = new RuntimeInputBinding(
            "customer-id",
            targetType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer-id", "scope:root"),
            conversionPlan: plan);
        var context = NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer],
            executable: NewProducerExecutable(projectionType: sourceType));

        var resolved = _resolver.Resolve(binding, context);
        var snapshot = await new RuntimeActivityInputMaterializer(_resolver)
            .MaterializeSnapshotAsync(
                NewConsumerNode(binding, inputType: targetType),
                consumer.InvocationId,
                context,
                DateTimeOffset.UnixEpoch);

        Assert.Equal(sourceType, resolved.Envelope!.Type);
        Assert.Equal(targetType, snapshot.Values["customer-id"].Type);
        Assert.Equal(7U, snapshot.Values["customer-id"].InlineValue!.Value.GetUInt32());
    }

    [Fact]
    public void Resolve_result_projection_preserves_source_protection_policy()
    {
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full");
        var producer = CompletedProducer(ValueEnvelope.Inline(
            new ValueTypeDescriptor("test/result"),
            JsonSerializer.SerializeToElement(new { customerId = "customer-7" }),
            policy));
        var consumer = RunningConsumer(producer.InvocationId);
        var binding = new RuntimeInputBinding(
            "customer-id",
            StringType,
            policy,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer-id", "scope:root"));

        var resolved = _resolver.Resolve(binding, NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer],
            executable: NewProducerExecutable()));

        Assert.True(resolved.Envelope!.Policy.Satisfies(policy));
        Assert.True(policy.Satisfies(resolved.Envelope.Policy));
        Assert.Equal("customer-7", resolved.Envelope.InlineValue!.Value.GetString());
    }

    [Fact]
    public void Resolve_whole_result_preserves_external_payload_without_materializing_it()
    {
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full");
        var externalReference = new DurableValueExternalReference("encrypted", "results/producer-1", new Dictionary<string, string>());
        var producer = CompletedProducer(ValueEnvelope.External(new ValueTypeDescriptor("test/result"), externalReference, policy));
        var consumer = RunningConsumer(producer.InvocationId);
        var binding = new RuntimeInputBinding(
            "result",
            new ValueTypeDescriptor("test/result"),
            policy,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "$result", "scope:root"));

        var resolved = _resolver.Resolve(binding, NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer]));

        Assert.Equal("results/producer-1", resolved.Envelope!.ExternalReference!.Locator);
        Assert.Equal(policy, resolved.Envelope.Policy);
        Assert.Null(resolved.Envelope.InlineValue);
    }

    [Fact]
    public async Task Materialization_propagates_result_projection_policy_to_destination()
    {
        var projectionPolicy = new ActivityValuePolicy(
            IsPersistable: true,
            IsSensitive: true,
            RequiresEncryption: true,
            RedactionMode: "Full");
        var producer = CompletedProducer(ValueEnvelope.Inline(
            new ValueTypeDescriptor("test/result"),
            JsonSerializer.SerializeToElement(new { customerId = "customer-7" }),
            ValuePolicyCombiner.ToProtectionPolicy(projectionPolicy)));
        var consumer = RunningConsumer(producer.InvocationId);
        var executable = NewProducerExecutable(projectionPolicy);
        var binding = new RuntimeInputBinding(
            "customer-id",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer-id", "scope:root"));
        var context = NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer],
            executable: executable);

        var resolved = _resolver.Resolve(binding, context);
        Assert.True(resolved.Envelope!.Policy.IsSensitive);
        Assert.True(resolved.Envelope.Policy.RequiresEncryption);
        Assert.Equal("Full", resolved.Envelope.Policy.RedactionMode);

        var snapshot = await new RuntimeActivityInputMaterializer(_resolver)
            .MaterializeSnapshotAsync(NewConsumerNode(binding), consumer.InvocationId, context, DateTimeOffset.UnixEpoch);

        var value = snapshot.Values["customer-id"];
        Assert.True(value.Policy.IsSensitive);
        Assert.True(value.Policy.RequiresEncryption);
        Assert.Equal("Full", value.Policy.RedactionMode);
    }

    [Fact]
    public async Task Materialization_preserves_result_lifecycle_when_destination_requires_only_instance_lifecycle()
    {
        var resultPolicy = new ValueProtectionPolicy(DurableValueLifecycle.Result, DurableValueStorage.Inline);
        var producer = CompletedProducer(ValueEnvelope.Inline(
            new ValueTypeDescriptor("test/result"),
            JsonSerializer.SerializeToElement(new { customerId = "customer-7" }),
            resultPolicy));
        var consumer = RunningConsumer(producer.InvocationId);
        var binding = new RuntimeInputBinding(
            "customer-id",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer-id", "scope:root"));
        var context = NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer],
            executable: NewProducerExecutable(new ActivityValuePolicy(
                IsPersistable: true,
                IsSensitive: false,
                RequiresEncryption: false,
                RedactionMode: null,
                Lifecycle: ActivityValueLifecycle.Result)));

        var snapshot = await new RuntimeActivityInputMaterializer(_resolver)
            .MaterializeSnapshotAsync(NewConsumerNode(binding), consumer.InvocationId, context, DateTimeOffset.UnixEpoch);

        Assert.Equal(DurableValueLifecycle.Result, snapshot.Values["customer-id"].Policy.Lifecycle);
        Assert.Equal("customer-7", snapshot.Values["customer-id"].InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Materialization_preserves_a_canonical_absent_optional_input_in_the_complete_snapshot()
    {
        var binding = new RuntimeInputBinding(
            "customer-id",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline));

        var snapshot = await new RuntimeActivityInputMaterializer(_resolver)
            .MaterializeSnapshotAsync(
                NewConsumerNode(binding, isRequired: false),
                "consumer",
                NewContext(),
                DateTimeOffset.UnixEpoch);

        var value = Assert.Contains("customer-id", snapshot.Values);
        Assert.Equal(ValuePresence.Absent, value.Presence);
        Assert.Equal(StringType, value.Type);
    }

    [Fact]
    public async Task Materialization_rejects_a_canonical_absent_required_input()
    {
        var binding = new RuntimeInputBinding(
            "customer-id",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(_resolver)
                .MaterializeSnapshotAsync(
                    NewConsumerNode(binding),
                    "consumer",
                    NewContext(),
                    DateTimeOffset.UnixEpoch)
                .AsTask());

        Assert.Contains("Required input 'customer-id'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot materialize as absent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_result_projection_preserves_stricter_lifecycle_storage_and_retention()
    {
        var projectionPolicy = new ActivityValuePolicy(
            IsPersistable: true,
            IsSensitive: true,
            RequiresEncryption: true,
            RedactionMode: "Full",
            Lifecycle: ActivityValueLifecycle.Audit,
            Storage: ActivityValueStorage.External,
            StorageProfile: "audit-payloads",
            RetentionPolicy: "P30D");
        var sourceReference = new DurableValueExternalReference(
            "audit-payloads",
            "results/producer-1",
            new Dictionary<string, string>());
        var producer = CompletedProducer(ValueEnvelope.External(
            new ValueTypeDescriptor("test/result"),
            sourceReference,
            ValuePolicyCombiner.ToProtectionPolicy(projectionPolicy)));
        var consumer = RunningConsumer(producer.InvocationId);
        var binding = new RuntimeInputBinding(
            "customer-id",
            StringType,
            new ValueProtectionPolicy(
                DurableValueLifecycle.Audit,
                DurableValueStorage.External,
                isSensitive: true,
                requiresEncryption: true,
                redactionMode: "Full",
                retentionPolicy: "P30D"),
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer-id", "scope:root"));

        var resolved = _resolver.Resolve(binding, NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer],
            executable: NewProducerExecutable(projectionPolicy)));

        Assert.Equal(DurableValueLifecycle.Audit, resolved.Envelope!.Policy.Lifecycle);
        Assert.Equal(DurableValueStorage.External, resolved.Envelope.Policy.Storage);
        Assert.Equal("P30D", resolved.Envelope.Policy.RetentionPolicy);
        Assert.Equal("results/producer-1", resolved.Envelope.ExternalReference!.Locator);
        Assert.True(resolved.Envelope.Policy.IsSensitive);
        Assert.True(resolved.Envelope.Policy.RequiresEncryption);
    }

    private static RuntimeInputBindingResolutionContext NewContext(
        IReadOnlyDictionary<string, object?>? workflowInputs = null,
        ActivityExecutionState? consumer = null,
        IReadOnlyCollection<ActivityExecutionState>? runtimeView = null,
        WorkflowExecutable? executable = null) =>
        new(
            "workflow-1",
            consumer?.InvocationId ?? "consumer",
            workflowInputEnvelopes: workflowInputs?.ToDictionary(
                item => item.Key,
                item => ValueEnvelope.Inline(
                    StringType,
                    item.Value is JsonElement json ? json : JsonSerializer.SerializeToElement(item.Value),
                    ValueProtectionPolicy.InstanceInline),
                StringComparer.Ordinal),
            consumerInvocation: consumer,
            runtimeView: runtimeView,
            executable: executable);

    private static WorkflowExecutable NewProducerExecutable(
        ActivityValuePolicy? projectionPolicy = null,
        ValueTypeDescriptor? projectionType = null)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "producer" });
        var contract = new ActivityContract(
            "test/producer",
            "1.0.0",
            "test",
            descriptor,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("test/result"),
                true,
                ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("customer-id", "customerId", projectionType ?? StringType, true, projectionPolicy ?? ActivityValuePolicy.Default)]),
            ["Done"],
            new ActivityActivationRequirement("test", "test/producer"));
        var node = new ExecutableNode(
            "producer",
            "producer",
            "test/producer",
            "1.0.0",
            "test",
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract);
        return new WorkflowExecutable(
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());
    }

    private static ExecutableNode NewConsumerNode(
        RuntimeInputBinding binding,
        bool isRequired = true,
        ValueTypeDescriptor? inputType = null)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "consumer" });
        var contract = new ActivityContract(
            "test/consumer",
            "1.0.0",
            "test",
            descriptor,
            [new ActivityInputContract("customer-id", "Customer ID", inputType ?? StringType, isRequired, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/consumer"));
        return new ExecutableNode(
            "consumer",
            "consumer",
            "test/consumer",
            "1.0.0",
            "test",
            descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["customer-id"] = binding },
            new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static ActivityExecutionState CompletedProducer(ValueEnvelope? result = null)
    {
        var completedAt = DateTimeOffset.UnixEpoch.AddSeconds(1);
        return State("producer-1", "producer", ActivityExecutionStatus.Completed, null) with
        {
            Completion = new ActivityCompletion(
                "producer-1",
                "attempt-1",
                result ?? ValueEnvelope.Inline(
                    new ValueTypeDescriptor("test/result"),
                    JsonSerializer.SerializeToElement(new { customerId = "customer-7" }),
                    ValueProtectionPolicy.InstanceInline),
                "Done",
                completedAt,
                "contract"),
            CompletedAt = completedAt
        };
    }

    private static ActivityExecutionState RunningConsumer(string predecessorId) =>
        State("consumer", "consumer", ActivityExecutionStatus.Running, predecessorId);

    private static ActivityExecutionState State(
        string invocationId,
        string nodeId,
        ActivityExecutionStatus status,
        string? predecessorId) =>
        new(
            new ActivityExecution(invocationId, "workflow-1", nodeId, nodeId, $"test/{nodeId}", "1.0.0"),
            status,
            null,
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            predecessorId,
            null,
            null,
            null,
            ActivitySchedulingProvenance.From(
                "workflow-1",
                null,
                predecessorId,
                null,
                null,
                "path:root",
                "scope:root",
                "test"),
            0,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());

}
