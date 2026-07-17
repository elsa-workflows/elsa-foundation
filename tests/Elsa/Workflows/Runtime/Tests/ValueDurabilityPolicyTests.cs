using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Snapshot_materialization_propagates_stricter_source_sensitivity()
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

        var snapshot = await NewMaterializer()
            .MaterializeSnapshotAsync(node, "invocation-1", NewResolutionContext(), Now);

        var value = snapshot.Values["message"];
        Assert.True(value.Policy.IsSensitive);
        Assert.True(value.Policy.RequiresEncryption);
        Assert.Equal("Full", value.Policy.RedactionMode);
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
        Assert.Equal(policy.Lifecycle, value.Policy.Lifecycle);
        Assert.Equal(policy.Storage, value.Policy.Storage);
        Assert.Equal(policy.IsSensitive, value.Policy.IsSensitive);
        Assert.Equal(policy.RequiresEncryption, value.Policy.RequiresEncryption);
        Assert.Equal(policy.RedactionMode, value.Policy.RedactionMode);
    }

    [Fact]
    public async Task Variable_source_policy_is_propagated_to_consumer_binding()
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

        var snapshot = await NewMaterializer().MaterializeSnapshotAsync(
                NewTypedNode(binding, ActivityValuePolicy.Default),
                "invocation-1",
                context,
                Now);

        var value = snapshot.Values["message"];
        Assert.True(value.Policy.IsSensitive);
        Assert.True(value.Policy.RequiresEncryption);
        Assert.Equal("Full", value.Policy.RedactionMode);
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
        var contractPolicy = new ActivityValuePolicy(
            true, true, true, "Full", ActivityValueLifecycle.Instance,
            ActivityValueStorage.External, "encrypted", "P30D");
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            ValuePolicyCombiner.ToProtectionPolicy(contractPolicy),
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("request", "request.customer.id"));
        var context = NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
        {
            ["request"] = ValueEnvelope.External(new ValueTypeDescriptor("Request"), sourceReference, policy)
        });
        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
            .MaterializeSnapshotAsync(NewTypedNode(binding, contractPolicy), "invocation-1", context, Now);

        var value = snapshot.Values["message"];
        Assert.Null(value.InlineValue);
        Assert.Equal("payloads/activity:invocation-1:input:message", value.ExternalReference!.Locator);
        Assert.Equal("customer-7", store.Writes.Single().Payload.GetString());
        Assert.Equal("P30D", store.Writes.Single().Policy.RetentionPolicy);
    }

    [Fact]
    public async Task InlineLiteralIsExternalizedWhenItsOwningInputPolicyRequiresExternalStorage()
    {
        var contractPolicy = new ActivityValuePolicy(
            IsPersistable: true,
            IsSensitive: true,
            RequiresEncryption: true,
            RedactionMode: "Full",
            Lifecycle: ActivityValueLifecycle.Instance,
            Storage: ActivityValueStorage.External,
            StorageProfile: "encrypted-inputs");
        var policy = ValuePolicyCombiner.ToProtectionPolicy(contractPolicy);
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("secret"), policy));
        var store = new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>());

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
            .MaterializeSnapshotAsync(
                NewTypedNode(binding, contractPolicy),
                "invocation-1",
                NewResolutionContext(),
                Now);

        var value = snapshot.Values["message"];
        Assert.Null(value.InlineValue);
        Assert.Equal("payloads/activity:invocation-1:input:message", value.ExternalReference!.Locator);
        Assert.Equal("encrypted-inputs", Assert.Single(store.Writes).StorageProfile);
        Assert.Equal("secret", store.Writes.Single().Payload.GetString());
    }

    [Fact]
    public async Task Conflicting_input_retention_policies_are_rejected()
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

        Assert.Contains("incompatible retention", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Same_profile_external_source_is_rewritten_when_destination_strengthens_policy()
    {
        var sourcePolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            metadata: new Dictionary<string, string> { [ValuePolicyCombiner.StorageProfileMetadataKey] = "encrypted" });
        var destinationPolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full",
            metadata: new Dictionary<string, string> { [ValuePolicyCombiner.StorageProfileMetadataKey] = "encrypted" });
        var reference = new DurableValueExternalReference("encrypted", "payloads/source", new Dictionary<string, string>());
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            destinationPolicy,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("message"));
        var store = new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>
        {
            [reference.Locator] = JsonSerializer.SerializeToElement("secret")
        });
        var context = NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
        {
            ["message"] = ValueEnvelope.External(StringType, reference, sourcePolicy)
        });

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
            .MaterializeSnapshotAsync(
                NewTypedNode(binding, new ActivityValuePolicy(true, true, true, "Full", Storage: ActivityValueStorage.External, StorageProfile: "encrypted")),
                "invocation-1",
                context,
                Now);

        var value = snapshot.Values["message"];
        Assert.NotEqual(reference.Locator, value.ExternalReference!.Locator);
        var write = Assert.Single(store.Writes);
        Assert.True(write.Policy.IsSensitive);
        Assert.True(write.Policy.RequiresEncryption);
        Assert.Equal("Full", write.Policy.RedactionMode);
    }

    [Fact]
    public async Task Same_profile_external_source_is_rewritten_when_destination_adds_policy_metadata()
    {
        var sourcePolicy = ExternalPolicy("encrypted");
        var destinationPolicy = ExternalPolicy("encrypted", new KeyValuePair<string, string>("classification", "restricted"));
        var reference = new DurableValueExternalReference("encrypted", "payloads/source", new Dictionary<string, string>());
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            destinationPolicy,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("message"));
        var store = new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>
        {
            [reference.Locator] = JsonSerializer.SerializeToElement("value")
        });

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
            .MaterializeSnapshotAsync(
                NewTypedNode(binding, new ActivityValuePolicy(true, false, false, null, Storage: ActivityValueStorage.External, StorageProfile: "encrypted")),
                "invocation-1",
                NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
                {
                    ["message"] = ValueEnvelope.External(StringType, reference, sourcePolicy)
                }),
                Now);

        Assert.NotEqual(reference.Locator, snapshot.Values["message"].ExternalReference!.Locator);
        Assert.Equal("restricted", Assert.Single(store.Writes).Policy.Metadata["classification"]);
    }

    [Fact]
    public async Task Portable_expression_dereferences_external_dependency_and_propagates_its_policy()
    {
        var dependencyPolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Result,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full",
            metadata: new Dictionary<string, string> { [ValuePolicyCombiner.StorageProfileMetadataKey] = "encrypted" });
        var reference = new DurableValueExternalReference("encrypted", "payloads/expression-source", new Dictionary<string, string>());
        var store = new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>
        {
            [reference.Locator] = JsonSerializer.SerializeToElement("secret")
        });
        var expression = new RuntimeExpressionBinding(
            "test",
            "value",
            parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["value"] = new WorkflowRequestExpressionParameterBinding("source")
            });
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: expression);
        var services = new ServiceCollection().AddSingleton<IPortableExpressionEvaluator, EchoPortableEvaluator>();
        await using var provider = services.BuildServiceProvider();
        var context = NewResolutionContext(
            workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
            {
                ["source"] = ValueEnvelope.External(StringType, reference, dependencyPolicy)
            },
            serviceProvider: provider);

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new StringTypeRegistry(), store)
            .MaterializeSnapshotAsync(NewTypedNode(binding, ActivityValuePolicy.Default), "invocation-1", context, Now);

        var value = snapshot.Values["message"];
        Assert.Equal(DurableValueLifecycle.Result, value.Policy.Lifecycle);
        Assert.True(value.Policy.IsSensitive);
        Assert.True(value.Policy.RequiresEncryption);
        Assert.Equal("Full", value.Policy.RedactionMode);
        Assert.Equal("secret", Assert.Single(store.Writes).Payload.GetString());
    }

    [Fact]
    public async Task Portable_expression_failure_reports_the_definition_fingerprint()
    {
        var expression = new RuntimeExpressionBinding("test", "fail()");
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: expression);
        var services = new ServiceCollection().AddSingleton<IPortableExpressionEvaluator, ThrowingPortableEvaluator>();
        await using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new StringTypeRegistry(), externalPayloadStore: null)
                .MaterializeSnapshotAsync(
                    NewTypedNode(binding, ActivityValuePolicy.Default),
                    "invocation-1",
                    NewResolutionContext(serviceProvider: provider),
                    Now)
                .AsTask());

        Assert.Contains("portable 'test' expression", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fingerprint 'sha256:", exception.Message, StringComparison.Ordinal);
    }

    private static RuntimeActivityInputMaterializer NewMaterializer() =>
        new(new RuntimeInputBindingResolver());

    private static RuntimeInputBindingResolutionContext NewResolutionContext(
        IReadOnlyDictionary<string, ValueEnvelope>? workflowInputEnvelopes = null,
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? variableEnvelopes = null,
        IServiceProvider? serviceProvider = null) =>
        new(
            "workflow-1",
            "invocation-1",
            workflowInputEnvelopes: workflowInputEnvelopes,
            variableEnvelopes: variableEnvelopes,
            serviceProvider: serviceProvider);

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
            new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static ValueProtectionPolicy SensitiveExternalPolicy() => new(
        DurableValueLifecycle.Instance,
        DurableValueStorage.External,
        isSensitive: true,
        requiresEncryption: true,
        redactionMode: "Full");

    private static ValueProtectionPolicy ExternalPolicy(
        string profile,
        KeyValuePair<string, string>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, string>
        {
            [ValuePolicyCombiner.StorageProfileMetadataKey] = profile
        };
        if (additionalMetadata is { } entry)
            metadata.Add(entry.Key, entry.Value);
        return new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.External, metadata: metadata);
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

    private sealed class EchoPortableEvaluator : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(request.ParameterValues["value"].Clone());
    }

    private sealed class ThrowingPortableEvaluator : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromException<JsonElement>(new InvalidOperationException("Evaluation failed."));
    }

    private sealed class StringTypeRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias)
        {
            alias = "String";
            return type == typeof(string);
        }

        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => [typeof(string)];
        public string GetAliasOrDefault(Type type) => type == typeof(string) ? "String" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => TryGetTypeOrDefault(alias, out var type) ? type : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            type = typeof(string);
            return StringComparer.Ordinal.Equals(alias, "String");
        }
    }
}
