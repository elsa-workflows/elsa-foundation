using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ValueDurabilityPolicyTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_materialization_preserves_external_sensitive_encrypted_and_redacted_policy_without_argument_wrappers()
    {
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full");
        var externalReference = new DurableValueExternalReference(
            "encrypted-payloads",
            "payloads/message-1",
            new Dictionary<string, string>());
        var node = NewTypedNode(
            ValueEnvelope.External(StringType, externalReference, policy),
            policy,
            new ActivityValuePolicy(true, true, true, "Full"));

        var snapshot = await NewMaterializer().MaterializeSnapshotAsync(
            node,
            "invocation-1",
            NewResolutionContext(),
            Now);

        var value = Assert.Single(snapshot.Values).Value;
        Assert.Equal(ValuePresence.Present, value.Presence);
        Assert.Null(value.InlineValue);
        Assert.Equal("payloads/message-1", value.ExternalReference!.Locator);
        Assert.Equal(DurableValueStorage.External, value.Policy.Storage);
        Assert.True(value.Policy.IsSensitive);
        Assert.True(value.Policy.RequiresEncryption);
        Assert.Equal("Full", value.Policy.RedactionMode);
    }

    [Fact]
    public async Task Durable_snapshot_rejects_transient_input_policy()
    {
        var node = NewTypedNode(
            ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("unsafe"), ValueProtectionPolicy.Transient),
            ValueProtectionPolicy.Transient,
            ActivityValuePolicy.Default);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => NewMaterializer()
            .MaterializeSnapshotAsync(node, "invocation-1", NewResolutionContext(), Now).AsTask());

        Assert.Contains("VF-ACT-005", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_materialization_rejects_source_sensitivity_downgrade()
    {
        var sensitiveSourcePolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full");
        var node = NewTypedNode(
            ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("secret"), sensitiveSourcePolicy),
            ValueProtectionPolicy.InstanceInline,
            ActivityValuePolicy.Default);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => NewMaterializer()
            .MaterializeSnapshotAsync(node, "invocation-1", NewResolutionContext(), Now).AsTask());

        Assert.Contains("downgrade", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workflow_request_source_preserves_external_payload_and_policy_in_snapshot()
    {
        var policy = SensitiveExternalPolicy();
        var source = ValueEnvelope.External(
            StringType,
            new DurableValueExternalReference("encrypted", "requests/customer-7", new Dictionary<string, string>()),
            policy);
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            policy,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("customer-id"));
        var context = NewResolutionContext(
            workflowInputEnvelopes: new Dictionary<string, ValueEnvelope> { ["customer-id"] = source });

        var snapshot = await NewMaterializer().MaterializeSnapshotAsync(
            NewTypedNode(binding, new ActivityValuePolicy(true, true, true, "Full")),
            "invocation-1",
            context,
            Now);

        var value = Assert.Single(snapshot.Values).Value;
        Assert.Equal("requests/customer-7", value.ExternalReference!.Locator);
        Assert.Equal(policy, value.Policy);
    }

    [Fact]
    public async Task Variable_source_policy_cannot_be_downgraded_by_consumer_binding()
    {
        var sourcePolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full");
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference("customer-id", "scope:root"));
        var context = NewResolutionContext(
            variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>
            {
                [new RuntimeVariableValueAddress("scope:root", "customer-id")] =
                    ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("secret"), sourcePolicy)
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => NewMaterializer()
            .MaterializeSnapshotAsync(
                NewTypedNode(binding, ActivityValuePolicy.Default),
                "invocation-1",
                context,
                Now)
            .AsTask());

        Assert.Contains("downgrade", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_request_projection_is_dereferenced_and_re_externalized_for_the_pinned_input()
    {
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full",
            retentionPolicy: "P30D");
        var sourceReference = new DurableValueExternalReference(
            "encrypted",
            "requests/customer-7",
            new Dictionary<string, string>());
        var store = new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>
        {
            [sourceReference.Locator] = JsonSerializer.SerializeToElement(new { customer = new { id = "customer-7" } })
        });
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            policy,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("request", "request.customer.id"));
        var context = NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
        {
            ["request"] = ValueEnvelope.External(new ValueTypeDescriptor("Request"), sourceReference, policy)
        });
        var contractPolicy = new ActivityValuePolicy(
            true, true, true, "Full", ActivityValueLifecycle.Instance,
            ActivityValueStorage.External, "encrypted", "P30D");

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
            .MaterializeSnapshotAsync(NewTypedNode(binding, contractPolicy), "invocation-1", context, Now);

        var value = snapshot.Values["message"];
        Assert.Null(value.InlineValue);
        Assert.Equal("payloads/activity:invocation-1:input:message", value.ExternalReference!.Locator);
        Assert.Equal("customer-7", store.Writes.Single().Payload.GetString());
        Assert.Equal("P30D", store.Writes.Single().Policy.RetentionPolicy);
    }

    [Fact]
    public async Task Input_retention_policy_cannot_be_downgraded()
    {
        var sourcePolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            retentionPolicy: "P30D");
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, retentionPolicy: "P7D"),
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("value"), sourcePolicy));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => NewMaterializer()
            .MaterializeSnapshotAsync(NewTypedNode(binding, ActivityValuePolicy.Default), "invocation-1", NewResolutionContext(), Now)
            .AsTask());

        Assert.Contains("downgrade", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeActivityInputMaterializer NewMaterializer() =>
        new(new RuntimeInputBindingResolver());

    private static RuntimeInputBindingResolutionContext NewResolutionContext(
        IReadOnlyDictionary<string, ValueEnvelope>? workflowInputEnvelopes = null,
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? variableEnvelopes = null) =>
        new(
            "workflow-1",
            "invocation-1",
            new Dictionary<string, DurableValueState>(),
            EmptyOutputReader.Instance,
            workflowInputEnvelopes: workflowInputEnvelopes,
            variableEnvelopes: variableEnvelopes);

    private static ExecutableNode NewTypedNode(
        ValueEnvelope literal,
        ValueProtectionPolicy effectivePolicy,
        ActivityValuePolicy contractPolicy)
    {
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            effectivePolicy,
            RuntimeInputBindingSource.Literal,
            literal: literal);

        return NewTypedNode(binding, contractPolicy);
    }

    private static ExecutableNode NewTypedNode(RuntimeInputBinding binding, ActivityValuePolicy contractPolicy)
    {
        using var descriptor = JsonDocument.Parse("""{"type":"test"}""");
        var contract = new ActivityContract(
            "test/activity",
            "1.0.0",
            "test",
            descriptor.RootElement,
            [new ActivityInputContract("message", "Message", StringType, true, false, null, contractPolicy)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/activity"));
        return new ExecutableNode(
            "node-1",
            "authored-node-1",
            "test/activity",
            "1.0.0",
            "test",
            descriptor.RootElement,
            new Dictionary<string, RuntimeInputBinding> { ["message"] = binding },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static ValueProtectionPolicy SensitiveExternalPolicy() => new(
        DurableValueLifecycle.Instance,
        DurableValueStorage.External,
        isSensitive: true,
        requiresEncryption: true,
        redactionMode: "Full");

    private sealed class EmptyOutputReader : Elsa.Workflows.Runtime.Core.Contracts.IRuntimeActivityOutputReader
    {
        public static readonly EmptyOutputReader Instance = new();

        public bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output)
        {
            output = null!;
            return false;
        }

        public IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId) => [];
    }

    private sealed class RecordingExternalPayloadStore(IReadOnlyDictionary<string, JsonElement> payloads) : IExternalPayloadStore
    {
        public List<ExternalPayloadWriteRequest> Writes { get; } = [];

        public ValueTask<DurableValueExternalReference> WriteAsync(
            ExternalPayloadWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(request);
            return ValueTask.FromResult(new DurableValueExternalReference(
                request.StorageProfile,
                $"payloads/{request.OwnerKey}",
                new Dictionary<string, string>()));
        }

        public ValueTask<JsonElement> ReadAsync(
            DurableValueExternalReference reference,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(payloads[reference.Locator].Clone());
    }
}
