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

        Assert.Equal("customer-7", resolved.Value!.Value.GetString());
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

        Assert.Equal("customer-7", resolved.Value!.Value.GetString());
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
        Assert.Null(resolved.Value);
    }

    [Fact]
    public async Task Materialization_rejects_destination_that_downgrades_result_projection_policy()
    {
        var projectionPolicy = new ActivityValuePolicy(
            IsPersistable: true,
            IsSensitive: true,
            RequiresEncryption: true,
            RedactionMode: "Full");
        var producer = CompletedProducer();
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(_resolver)
                .MaterializeSnapshotAsync(NewConsumerNode(binding), consumer.InvocationId, context, DateTimeOffset.UnixEpoch)
                .AsTask());

        Assert.Contains("VF-ACT-005", exception.Message, StringComparison.Ordinal);
        Assert.Contains("downgrade", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private static WorkflowExecutable NewProducerExecutable(ActivityValuePolicy? projectionPolicy = null)
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
                [new ActivityResultProjectionContract("customer-id", "customerId", StringType, true, projectionPolicy ?? ActivityValuePolicy.Default)]),
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
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            activityContract: contract);
        return new WorkflowExecutable(
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());
    }

    private static ExecutableNode NewConsumerNode(RuntimeInputBinding binding)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "consumer" });
        var contract = new ActivityContract(
            "test/consumer",
            "1.0.0",
            "test",
            descriptor,
            [new ActivityInputContract("customer-id", "Customer ID", StringType, true, false, null, ActivityValuePolicy.Default)],
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
            new Dictionary<string, RuntimeOutputCapture>(),
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
