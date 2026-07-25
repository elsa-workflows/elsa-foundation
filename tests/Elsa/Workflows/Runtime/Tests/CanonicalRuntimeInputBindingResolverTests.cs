using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
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
    public async Task Materialization_applies_formatted_request_and_variable_conversion_plans_once()
    {
        var plan = JsonPlan(new ValueTypeDescriptor("Elsa.Any"));
        var requestBinding = new RuntimeInputBinding(
            "customer-id",
            new ValueTypeDescriptor("Elsa.Any"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("payload"),
            conversionPlan: plan);
        var variableBinding = new RuntimeInputBinding(
            "customer-id",
            new ValueTypeDescriptor("Elsa.Any"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference("customer", VariableReference.WorkflowScopeId),
            conversionPlan: plan);
        var context = NewContext(
            workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
            {
                ["payload"] = FormattedJsonEnvelope("""{"name":"Ada","tags":["json"]}""")
            },
            variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>
            {
                [new RuntimeVariableValueAddress(VariableReference.WorkflowScopeId, "customer")] = FormattedJsonEnvelope("""{"name":"Lin","tags":["variable"]}""")
            });
        var materializer = new RuntimeActivityInputMaterializer(_resolver);

        var request = await materializer.MaterializeSnapshotAsync(
            NewConsumerNode(requestBinding, inputType: new ValueTypeDescriptor("Elsa.Any")),
            "consumer",
            context,
            DateTimeOffset.UnixEpoch);
        var variable = await materializer.MaterializeSnapshotAsync(
            NewConsumerNode(variableBinding, inputType: new ValueTypeDescriptor("Elsa.Any")),
            "consumer",
            context,
            DateTimeOffset.UnixEpoch);

        Assert.Equal("Ada", request.Values["customer-id"].InlineValue!.Value.GetProperty("name").GetString());
        Assert.Equal("Lin", variable.Values["customer-id"].InlineValue!.Value.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Array, request.Values["customer-id"].InlineValue!.Value.GetProperty("tags").ValueKind);
    }

    [Fact]
    public async Task Materialization_applies_expression_conversion_plan_to_the_expression_source_type_once()
    {
        var plan = JsonPlan(new ValueTypeDescriptor("Elsa.Any"));
        var binding = new RuntimeInputBinding(
            "customer-id",
            new ValueTypeDescriptor("Elsa.Any"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("Test", "payload"),
            conversionPlan: plan);
        var materializer = new RuntimeActivityInputMaterializer(
            _resolver,
            new TestTypeRegistry(),
            new ConstantPortableExpressionEvaluator("""{"name":"Ada","tags":["expression"]}"""));

        var snapshot = await materializer.MaterializeSnapshotAsync(
            NewConsumerNode(binding, inputType: new ValueTypeDescriptor("Elsa.Any")),
            "consumer",
            NewContext(),
            DateTimeOffset.UnixEpoch);

        Assert.Equal("Ada", snapshot.Values["customer-id"].InlineValue!.Value.GetProperty("name").GetString());
        Assert.Equal("expression", snapshot.Values["customer-id"].InlineValue!.Value.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Materialization_applies_expression_parameter_conversion_plan_before_evaluation_once()
    {
        var plan = JsonPlan(new ValueTypeDescriptor("Elsa.Any"));
        var binding = new RuntimeInputBinding(
            "customer-id",
            new ValueTypeDescriptor("Elsa.Any"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "Test",
                "payload",
                parameters: new Dictionary<string, ExpressionParameterBinding>
                {
                    ["payload"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement("""{"name":"Grace","tags":["parameter"]}"""))
                },
                parameterConversionPlans: new Dictionary<string, ValueConversionPlan>
                {
                    ["payload"] = plan
                }));
        var materializer = new RuntimeActivityInputMaterializer(
            _resolver,
            new TestTypeRegistry(),
            new EchoParameterPortableExpressionEvaluator("payload"));

        var snapshot = await materializer.MaterializeSnapshotAsync(
            NewConsumerNode(binding, inputType: new ValueTypeDescriptor("Elsa.Any")),
            "consumer",
            NewContext(),
            DateTimeOffset.UnixEpoch);

        Assert.Equal("Grace", snapshot.Values["customer-id"].InlineValue!.Value.GetProperty("name").GetString());
        Assert.Equal("parameter", snapshot.Values["customer-id"].InlineValue!.Value.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Materialization_allows_transient_result_to_nonpersistable_direct_input()
    {
        await using var stream = new MemoryStream();
        var streamType = new ValueTypeDescriptor("Stream");
        var producer = CompletedProducer(ValueEnvelope.Transient(streamType, stream));
        var consumer = RunningConsumer(producer.InvocationId);
        var binding = new RuntimeInputBinding(
            "customer-id",
            streamType,
            ValueProtectionPolicy.Transient,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "customer-id", "scope:root"));
        var context = NewContext(
            consumer: consumer,
            runtimeView: [producer, consumer],
            executable: NewProducerExecutable(
                projectionPolicy: ActivityValuePolicy.Default with { IsPersistable = false },
                projectionType: streamType,
                projectionPath: "$"));

        var snapshot = await new RuntimeActivityInputMaterializer(_resolver)
            .MaterializeSnapshotAsync(
                NewConsumerNode(
                    binding,
                    inputType: streamType,
                    inputPolicy: ActivityValuePolicy.Default with { IsPersistable = false }),
                consumer.InvocationId,
                context,
                DateTimeOffset.UnixEpoch);

        Assert.Same(stream, snapshot.Values["customer-id"].TransientResource);
        Assert.Equal(ValueProtectionPolicy.Transient, snapshot.Values["customer-id"].Policy);
        Assert.Null(snapshot.Values["customer-id"].InlineValue);
        Assert.Null(snapshot.Values["customer-id"].ExternalReference);
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
                NewConsumerNode(binding, isRequired: false, isNullable: true),
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
        IReadOnlyDictionary<string, ValueEnvelope>? workflowInputEnvelopes = null,
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope>? variableEnvelopes = null,
        ActivityExecutionState? consumer = null,
        IReadOnlyCollection<ActivityExecutionState>? runtimeView = null,
        WorkflowExecutable? executable = null) =>
        new(
            "workflow-1",
            consumer?.InvocationId ?? "consumer",
            workflowInputEnvelopes: workflowInputEnvelopes ?? workflowInputs?.ToDictionary(
                item => item.Key,
                item => ValueEnvelope.Inline(
                    StringType,
                    item.Value is JsonElement json ? json : JsonSerializer.SerializeToElement(item.Value),
                    ValueProtectionPolicy.InstanceInline),
                StringComparer.Ordinal),
            variableEnvelopes: variableEnvelopes,
            consumerInvocation: consumer,
            runtimeView: runtimeView,
            executable: executable);

    private static ValueEnvelope FormattedJsonEnvelope(string json) =>
        ValueEnvelope.Inline(
            new ValueTypeDescriptor("String"),
            JsonSerializer.SerializeToElement(json),
            ValueProtectionPolicy.InstanceInline);

    private static ValueConversionPlan JsonPlan(ValueTypeDescriptor targetType) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            new ValueTypeDescriptor("String"),
            targetType,
            ValueConversionMode.Auto,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"),
            limits: ValueConversionLimits.Default,
            options: null);

    private static WorkflowExecutable NewProducerExecutable(
        ActivityValuePolicy? projectionPolicy = null,
        ValueTypeDescriptor? projectionType = null,
        string projectionPath = "customerId")
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
                [new ActivityResultProjectionContract("customer-id", projectionPath, projectionType ?? StringType, true, projectionPolicy ?? ActivityValuePolicy.Default)]),
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
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableNode NewConsumerNode(
        RuntimeInputBinding binding,
        bool isRequired = true,
        bool isNullable = false,
        ValueTypeDescriptor? inputType = null,
        ActivityValuePolicy? inputPolicy = null)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "consumer" });
        var contract = new ActivityContract(
            "test/consumer",
            "1.0.0",
            "test",
            descriptor,
            [new ActivityInputContract("customer-id", "Customer ID", inputType ?? StringType, isRequired, isNullable, false, null, inputPolicy ?? ActivityValuePolicy.Default)],
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

    private sealed class ConstantPortableExpressionEvaluator(string value) : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement(value));
    }

    private sealed class EchoParameterPortableExpressionEvaluator(string parameterName) : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(request.ParameterValues[parameterName].Clone());
    }

    private sealed class TestTypeRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias)
        {
            alias = type == typeof(string) ? "String" : type.FullName!;
            return type == typeof(string);
        }

        public bool TryGetType(string alias, out Type type)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(alias, "String"))
            {
                type = typeof(string);
                return true;
            }

            type = null!;
            return false;
        }

        public IEnumerable<Type> ListTypes() => [typeof(string)];
        public string GetAliasOrDefault(Type type) => TryGetAlias(type, out var alias) ? alias : type.FullName!;
        public Type GetTypeOrDefault(string alias) => TryGetType(alias, out var type) ? type : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }

}
