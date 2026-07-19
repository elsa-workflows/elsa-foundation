using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ValueDurabilityPolicyTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly ValueTypeDescriptor AnyType = new("Elsa.Any");
    private static readonly ValueTypeDescriptor CustomerType = new("Acme.Customer");
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

    [Theory]
    [InlineData(ValuePresence.Absent)]
    [InlineData(ValuePresence.ExplicitNull)]
    public async Task Snapshot_materialization_rejects_nullish_value_for_non_nullable_contract(ValuePresence presence)
    {
        var value = presence == ValuePresence.Absent
            ? ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline);
        var node = NewTypedNode(
            value,
            ValueProtectionPolicy.InstanceInline,
            ActivityValuePolicy.Default,
            isRequired: false,
            isNullable: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewMaterializer().MaterializeSnapshotAsync(node, "invocation-1", NewResolutionContext(), Now).AsTask());

        Assert.Contains("VF-ACT-004", exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not accept null or absence", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_materialization_allows_explicit_null_for_required_nullable_contract()
    {
        var node = NewTypedNode(
            ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline),
            ValueProtectionPolicy.InstanceInline,
            ActivityValuePolicy.Default,
            isRequired: true,
            isNullable: true);

        var snapshot = await NewMaterializer()
            .MaterializeSnapshotAsync(node, "invocation-1", NewResolutionContext(), Now);

        Assert.Equal(ValuePresence.ExplicitNull, snapshot.Values["message"].Presence);
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
    public async Task Externalization_rejects_a_provider_response_with_a_different_storage_profile()
    {
        var policy = ExternalPolicy("encrypted-inputs");
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("secret"), policy));
        var store = new RecordingExternalPayloadStore(
            new Dictionary<string, JsonElement>(),
            returnedProfile: "plain-inputs");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
                .MaterializeSnapshotAsync(
                    NewTypedNode(binding, new ActivityValuePolicy(true, false, false, null, Storage: ActivityValueStorage.External, StorageProfile: "encrypted-inputs")),
                    "invocation-1",
                    NewResolutionContext(),
                    Now)
                .AsTask());

        Assert.Contains("storage profile", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plain-inputs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Externalization_rejects_a_provider_response_with_a_blank_locator()
    {
        var policy = ExternalPolicy("encrypted-inputs");
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("secret"), policy));
        var store = new RecordingExternalPayloadStore(
            new Dictionary<string, JsonElement>(),
            returnedLocator: " ");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
                .MaterializeSnapshotAsync(
                    NewTypedNode(binding, new ActivityValuePolicy(true, false, false, null, Storage: ActivityValueStorage.External, StorageProfile: "encrypted-inputs")),
                    "invocation-1",
                    NewResolutionContext(),
                    Now)
                .AsTask());

        Assert.Equal("Locator", exception.ParamName);
        Assert.Contains("locator", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task External_json_source_is_read_converted_and_re_externalized_with_destination_policy()
    {
        var sourcePolicy = SensitiveExternalPolicy();
        var contractPolicy = new ActivityValuePolicy(
            true,
            true,
            true,
            "Full",
            ActivityValueLifecycle.Instance,
            ActivityValueStorage.External,
            "encrypted-inputs",
            "P30D");
        var reference = new DurableValueExternalReference("encrypted-source", "payloads/source-json", new Dictionary<string, string>());
        var binding = new RuntimeInputBinding(
            "message",
            AnyType,
            ValuePolicyCombiner.ToProtectionPolicy(contractPolicy),
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("payload"),
            conversionPlan: JsonPlan(AnyType));
        var store = new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>
        {
            [reference.Locator] = JsonSerializer.SerializeToElement("""{"name":"Ada","tags":["external"]}""")
        });

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
            .MaterializeSnapshotAsync(
                NewTypedNode(binding, contractPolicy, inputType: AnyType),
                "invocation-1",
                NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
                {
                    ["payload"] = ValueEnvelope.External(StringType, reference, sourcePolicy)
                }),
                Now);

        var value = snapshot.Values["message"];
        Assert.Equal(AnyType, value.Type);
        Assert.Null(value.InlineValue);
        Assert.Equal("payloads/activity:invocation-1:input:message", value.ExternalReference!.Locator);
        var write = Assert.Single(store.Writes);
        Assert.Equal(AnyType, write.Type);
        Assert.Equal("encrypted-inputs", write.StorageProfile);
        Assert.Equal("P30D", write.Policy.RetentionPolicy);
        Assert.Equal("Ada", write.Payload.GetProperty("name").GetString());
        Assert.Equal("external", write.Payload.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task External_xml_source_is_read_and_converted_to_registered_typed_alias()
    {
        var reference = new DurableValueExternalReference("encrypted-source", "payloads/source-xml", new Dictionary<string, string>());
        var binding = new RuntimeInputBinding(
            "message",
            CustomerType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("payload"),
            conversionPlan: XmlPlan(CustomerType));
        var store = new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>
        {
            [reference.Locator] = JsonSerializer.SerializeToElement("""
            <customer loyaltyPoints="42">
              <name>Ada</name>
              <address>
                <city>Brussels</city>
              </address>
            </customer>
            """)
        });

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new CustomerTypeRegistry(), store)
            .MaterializeSnapshotAsync(
                NewTypedNode(binding, ActivityValuePolicy.Default, inputType: CustomerType),
                "invocation-1",
                NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
                {
                    ["payload"] = ValueEnvelope.External(StringType, reference, SensitiveExternalPolicy())
                }),
                Now);

        Assert.Null(snapshot.Values["message"].InlineValue);
        Assert.NotNull(snapshot.Values["message"].ExternalReference);
        var value = Assert.Single(store.Writes).Payload;
        Assert.Equal("Ada", value.GetProperty("name").GetString());
        Assert.Equal(42, value.GetProperty("loyaltyPoints").GetInt64());
        Assert.Equal("Brussels", value.GetProperty("address").GetProperty("city").GetString());
    }

    [Fact]
    public async Task External_payload_read_failures_are_distinct_from_conversion_failures()
    {
        var missingReference = new DurableValueExternalReference("encrypted-source", "payloads/missing", new Dictionary<string, string>());
        var missingBinding = new RuntimeInputBinding(
            "message",
            AnyType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("payload"),
            conversionPlan: JsonPlan(AnyType));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>()))
                .MaterializeSnapshotAsync(
                    NewTypedNode(missingBinding, ActivityValuePolicy.Default, inputType: AnyType),
                    "invocation-1",
                    NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
                    {
                        ["payload"] = ValueEnvelope.External(StringType, missingReference, SensitiveExternalPolicy())
                    }),
                    Now)
                .AsTask());
        Assert.Contains("VF-ACT-005", missing.Message, StringComparison.Ordinal);
        Assert.Contains("could not be read", missing.Message, StringComparison.Ordinal);

        var malformedReference = new DurableValueExternalReference("encrypted-source", "payloads/malformed", new Dictionary<string, string>());
        var malformed = await Assert.ThrowsAsync<RuntimeValueConversionException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>
                {
                    [malformedReference.Locator] = JsonSerializer.SerializeToElement("{not-json")
                }))
                .MaterializeSnapshotAsync(
                    NewTypedNode(missingBinding, ActivityValuePolicy.Default, inputType: AnyType),
                    "invocation-1",
                    NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
                    {
                        ["payload"] = ValueEnvelope.External(StringType, malformedReference, SensitiveExternalPolicy())
                    }),
                    Now)
                .AsTask());
        Assert.Contains("malformed JSON content", malformed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be read", malformed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_json_conversion_enforces_the_pinned_payload_limits_after_dereference()
    {
        var reference = new DurableValueExternalReference("encrypted-source", "payloads/large", new Dictionary<string, string>());
        var binding = new RuntimeInputBinding(
            "message",
            AnyType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("payload"),
            conversionPlan: JsonPlan(AnyType, new ValueConversionLimits(64, 10_000, 8)));
        var store = new RecordingExternalPayloadStore(new Dictionary<string, JsonElement>
        {
            [reference.Locator] = JsonSerializer.SerializeToElement("""{"name":"Ada"}""")
        });

        var exception = await Assert.ThrowsAsync<RuntimeValueConversionException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), store)
                .MaterializeSnapshotAsync(
                    NewTypedNode(binding, ActivityValuePolicy.Default, inputType: AnyType),
                    "invocation-1",
                    NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
                    {
                        ["payload"] = ValueEnvelope.External(StringType, reference, SensitiveExternalPolicy())
                    }),
                    Now)
                .AsTask());

        Assert.Contains("maximum payload size '8' bytes was exceeded", exception.Message, StringComparison.Ordinal);
        Assert.Empty(store.Writes);
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
        var context = NewResolutionContext(
            workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
            {
                ["source"] = ValueEnvelope.External(StringType, reference, dependencyPolicy)
            });

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new StringTypeRegistry(), new EchoPortableEvaluator(), store)
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
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new StringTypeRegistry(), new ThrowingPortableEvaluator(), externalPayloadStore: null)
                .MaterializeSnapshotAsync(
                    NewTypedNode(binding, ActivityValuePolicy.Default),
                    "invocation-1",
                    NewResolutionContext(),
                    Now)
                .AsTask());

        Assert.Contains("portable 'test' expression", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fingerprint 'sha256:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sensitive_expression_failure_does_not_retain_the_evaluator_exception()
    {
        const string secret = "customer-secret-token";
        var sensitivePolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            isSensitive: true,
            redactionMode: "Full");
        var expression = new RuntimeExpressionBinding(
            "test",
            "throwSecret()",
            parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["secret"] = new WorkflowRequestExpressionParameterBinding("secret")
            });
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: expression);
        var context = NewResolutionContext(workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
        {
            ["secret"] = ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(secret), sensitivePolicy)
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(
                    new RuntimeInputBindingResolver(),
                    new StringTypeRegistry(),
                    new SecretThrowingPortableEvaluator(secret),
                    externalPayloadStore: null)
                .MaterializeSnapshotAsync(NewTypedNode(binding, ActivityValuePolicy.Default), "invocation-1", context, Now)
                .AsTask());

        Assert.NotNull(exception.InnerException);
        Assert.Contains(typeof(InvalidOperationException).FullName!, exception.InnerException!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("fingerprint 'sha256:", exception.Message, StringComparison.Ordinal);
    }

    private static RuntimeActivityInputMaterializer NewMaterializer() =>
        new(new RuntimeInputBindingResolver());

    private static RuntimeInputBindingResolutionContext NewResolutionContext(
        IReadOnlyDictionary<string, ValueEnvelope>? workflowInputEnvelopes = null,
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? variableEnvelopes = null) =>
        new(
            "workflow-1",
            "invocation-1",
            workflowInputEnvelopes: workflowInputEnvelopes,
            variableEnvelopes: variableEnvelopes);

    private static ExecutableNode NewTypedNode(
        ValueEnvelope literal,
        ValueProtectionPolicy effectivePolicy,
        ActivityValuePolicy contractPolicy,
        bool isRequired = true,
        bool isNullable = false)
    {
        var binding = new RuntimeInputBinding(
            "message",
            StringType,
            effectivePolicy,
            RuntimeInputBindingSource.Literal,
            literal: literal);

        return NewTypedNode(binding, contractPolicy, isRequired, isNullable);
    }

    private static ExecutableNode NewTypedNode(
        RuntimeInputBinding binding,
        ActivityValuePolicy contractPolicy,
        bool isRequired = true,
        bool isNullable = false,
        ValueTypeDescriptor? inputType = null)
    {
        using var descriptor = JsonDocument.Parse("""{"type":"test"}""");
        var contract = new ActivityContract(
            "test/activity",
            "1.0.0",
            "test",
            descriptor.RootElement,
            [new ActivityInputContract("message", "Message", inputType ?? StringType, isRequired, isNullable, false, null, contractPolicy)],
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

    private static ValueConversionPlan JsonPlan(ValueTypeDescriptor targetType, ValueConversionLimits? limits = null) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            StringType,
            targetType,
            ValueConversionMode.Auto,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"),
            limits: limits ?? ValueConversionLimits.Default,
            options: null);

    private static ValueConversionPlan XmlPlan(ValueTypeDescriptor targetType) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            StringType,
            targetType,
            ValueConversionMode.Xml,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.xml", "1"),
            limits: ValueConversionLimits.Default,
            options: null);

    private sealed class RecordingExternalPayloadStore(
        IReadOnlyDictionary<string, JsonElement> payloads,
        string? returnedProfile = null,
        string? returnedLocator = null) : IExternalPayloadStore
    {
        public List<ExternalPayloadWriteRequest> Writes { get; } = [];

        public ValueTask<DurableValueExternalReference> WriteAsync(
            ExternalPayloadWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(request);
            return ValueTask.FromResult(new DurableValueExternalReference(
                returnedProfile ?? request.StorageProfile,
                returnedLocator ?? $"payloads/{request.OwnerKey}",
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

    private sealed class SecretThrowingPortableEvaluator(string secret) : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromException<JsonElement>(new InvalidOperationException(secret));
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

    private sealed class CustomerTypeRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias)
        {
            alias = "Acme.Customer";
            return type == typeof(CustomerContract);
        }

        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => [typeof(CustomerContract)];
        public string GetAliasOrDefault(Type type) => TryGetAlias(type, out var alias) ? alias : type.FullName!;
        public Type GetTypeOrDefault(string alias) => TryGetTypeOrDefault(alias, out var type) ? type : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            type = typeof(CustomerContract);
            return StringComparer.Ordinal.Equals(alias, "Acme.Customer");
        }
    }

    private sealed record CustomerContract
    {
        public required string Name { get; init; }
        public long LoyaltyPoints { get; init; }
        public AddressContract? Address { get; init; }
    }

    private sealed record AddressContract
    {
        public required string City { get; init; }
    }
}
