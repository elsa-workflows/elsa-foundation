using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Validators;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeValueBindingContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryRuntimeActivityOutputRegister _outputs = new();
    private readonly RuntimeInputBindingResolver _resolver = new();

    [Fact]
    public void ExecutableNode_CarriesTypedRuntimeBindingsAndDurableCaptureDeclarations()
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["customer"] = DurableValueBinding("customer", "customer"),
            ["subject"] = ExpressionBinding("subject"),
            ["delivery"] = ActivityOutputBinding("delivery", "actexec-fetch", "status"),
            ["literal"] = LiteralBinding("literal", Json("""{"value":42}""")),
            ["reference"] = ReferenceBinding("reference", "crm.customer", "customer-1")
        };
        var outputCaptures = new Dictionary<string, RuntimeOutputCapture>
        {
            ["customer"] = new(
                outputName: "customer",
                valueId: "customer",
                type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
                lifecycle: DurableValueLifecycle.Instance,
                storage: DurableValueStorage.Inline,
                captureOnSuccessfulCompletion: true)
        };

        var node = new ExecutableNode(
            ExecutableNodeId: "node-send",
            AuthoredActivityId: "activity-send",
            ActivityType: "Elsa.SendEmail",
            ActivityTypeVersion: "1.0.0",
            DescriptorType: "test",
            DescriptorPayload: Json("{}"),
            InputBindings: inputBindings,
            OutputCaptures: outputCaptures,
            Metadata: new Dictionary<string, string>());

        Assert.Equal(RuntimeInputBindingSource.DurableValue, node.InputBindings["customer"].Source);
        Assert.Equal(RuntimeInputBindingSource.Expression, node.InputBindings["subject"].Source);
        Assert.Equal(RuntimeInputBindingSource.ActivityOutput, node.InputBindings["delivery"].Source);
        Assert.Equal(RuntimeInputBindingSource.Literal, node.InputBindings["literal"].Source);
        Assert.Equal(RuntimeInputBindingSource.Reference, node.InputBindings["reference"].Source);
        Assert.Equal("customer", node.OutputCaptures["customer"].ValueId);
        Assert.Equal(DurableValueLifecycle.Instance, node.OutputCaptures["customer"].Lifecycle);
    }

    [Fact]
    public void RuntimeInputBindingResolver_ConsumesActiveActivityOutputByActivityExecutionId()
    {
        _outputs.Set(new ActiveActivityOutput(
            key: new ActiveActivityOutputKey("wfexec-1", "actexec-fetch", "customer"),
            value: Json("""{"id":"customer-1"}"""),
            type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
            recordedAt: _now));
        var binding = ActivityOutputBinding("customer", "actexec-fetch", "customer");

        var resolved = _resolver.Resolve(binding, NewContext());

        Assert.Equal(RuntimeInputBindingSource.ActivityOutput, resolved.Source);
        Assert.Equal("customer-1", resolved.Value!.Value.GetProperty("id").GetString());
    }

    [Fact]
    public void RuntimeInputBindingResolver_FailsAmbiguousActivityOutputReference()
    {
        var binding = ActivityOutputBinding("customer", producerActivityExecutionId: null, outputName: "customer");

        var exception = Assert.Throws<RuntimeInputBindingResolutionException>(() => _resolver.Resolve(binding, NewContext()));

        Assert.Equal(RuntimeInputBindingResolutionFailureReason.AmbiguousActivityOutput, exception.Reason);
        Assert.Equal("customer", exception.OutputName);
    }

    [Fact]
    public void RuntimeInputBindingResolver_RequiresDurableValueAfterActiveOutputScopeIsGone()
    {
        _outputs.Set(new ActiveActivityOutput(
            key: new ActiveActivityOutputKey("wfexec-1", "actexec-fetch", "customer"),
            value: Json("""{"id":"customer-1"}"""),
            type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
            recordedAt: _now));
        _outputs.ClearActivityOutputs("wfexec-1", "actexec-fetch");
        var activityOutputBinding = ActivityOutputBinding("customer", "actexec-fetch", "customer");
        var durableBinding = DurableValueBinding("customer", "customer");

        var exception = Assert.Throws<RuntimeInputBindingResolutionException>(() => _resolver.Resolve(activityOutputBinding, NewContext()));
        var durable = _resolver.Resolve(durableBinding, NewContext());

        Assert.Equal(RuntimeInputBindingResolutionFailureReason.ActivityOutputMissing, exception.Reason);
        Assert.Equal("customer-1", durable.Value!.Value.GetProperty("id").GetString());
        Assert.Equal("durable-customer", durable.DurableValue!.DurableValueId);
    }

    [Fact]
    public void RuntimeInputBindingValidator_ReportsAmbiguousAndCrossSuspensionOutputReferences()
    {
        var binding = ActivityOutputBinding("customer", producerActivityExecutionId: null, outputName: "customer");
        var validator = new RuntimeInputBindingValidator();

        var diagnostics = validator.Validate(binding, new RuntimeInputBindingValidationContext(
            ExecutableNodeId: "node-send",
            CrossesSuspensionBoundary: true,
            HasAmbiguousProducerExecution: true));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == RuntimeInputBindingDiagnosticCode.ActivityOutputCrossesSuspensionBoundary);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == RuntimeInputBindingDiagnosticCode.AmbiguousActivityOutputReference);
    }

    [Fact]
    public void RuntimeInputBindingSources_ExcludeHistoryOutputSnapshots()
    {
        var sourceNames = Enum.GetNames<RuntimeInputBindingSource>();

        Assert.DoesNotContain(sourceNames, name => name.Contains("History", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sourceNames, name => name.Contains("Audit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeValueBindingModels_SnapshotMetadataAndValidateRuntimeIdentities()
    {
        var metadata = new Dictionary<string, string> { ["source"] = "compile" };
        var reference = new RuntimeReferenceValue("crm.customer", "customer-1", metadata: metadata);

        metadata["source"] = "mutated";

        Assert.Equal("compile", reference.Metadata["source"]);
        Assert.Throws<ArgumentException>(() => new RuntimeDurableValueReference(""));
        Assert.Throws<ArgumentException>(() => new RuntimeActivityOutputReference("actexec-1", "node-1", ""));
        Assert.Throws<ArgumentException>(() => new ActiveActivityOutputKey("wfexec-1", "actexec-1", ""));
    }

    [Fact]
    public void ExecutableNode_SnapshotsBindingCaptureAndMetadataDictionaries()
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["customer"] = DurableValueBinding("customer", "customer")
        };
        var outputCaptures = new Dictionary<string, RuntimeOutputCapture>
        {
            ["customer"] = new(
                outputName: "customer",
                valueId: "customer",
                type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
                lifecycle: DurableValueLifecycle.Instance,
                storage: DurableValueStorage.Inline,
                captureOnSuccessfulCompletion: true)
        };
        var metadata = new Dictionary<string, string> { ["source"] = "compile" };

        var node = new ExecutableNode(
            ExecutableNodeId: "node-send",
            AuthoredActivityId: "activity-send",
            ActivityType: "Elsa.SendEmail",
            ActivityTypeVersion: "1.0.0",
            DescriptorType: "test",
            DescriptorPayload: Json("{}"),
            InputBindings: inputBindings,
            OutputCaptures: outputCaptures,
            Metadata: metadata);

        inputBindings["late"] = LiteralBinding("late", Json("""{"value":1}"""));
        outputCaptures.Clear();
        metadata["source"] = "mutated";

        Assert.DoesNotContain("late", node.InputBindings.Keys);
        Assert.Single(node.OutputCaptures);
        Assert.Equal("compile", node.Metadata["source"]);
    }

    private RuntimeInputBindingResolutionContext NewContext() =>
        new(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-send",
            durableValuesByValueId: new Dictionary<string, DurableValueState>
            {
                ["customer"] = new(
                    durableValueId: "durable-customer",
                    workflowExecutionId: "wfexec-1",
                    valueId: "customer",
                    type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    inlineValue: Json("""{"id":"customer-1"}"""),
                    externalReference: null,
                    sourceActivityExecutionId: "actexec-fetch",
                    capturedAt: _now,
                    metadata: new Dictionary<string, string>())
            },
            activityOutputs: _outputs);

    private static RuntimeInputBinding LiteralBinding(string inputName, JsonElement value) =>
        new(inputName, RuntimeInputBindingSource.Literal, literalValue: value);

    private static RuntimeInputBinding ExpressionBinding(string inputName) =>
        new(
            inputName,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                language: "javascript",
                expression: "`Hello ${values.customerName}`",
                resultType: new RuntimeValueTypeDescriptor("primitive", "string", null)));

    private static RuntimeInputBinding ActivityOutputBinding(string inputName, string? producerActivityExecutionId, string outputName) =>
        new(
            inputName,
            RuntimeInputBindingSource.ActivityOutput,
            activityOutput: new RuntimeActivityOutputReference(
                producerActivityExecutionId,
                producerExecutableNodeId: "node-fetch",
                outputName));

    private static RuntimeInputBinding DurableValueBinding(string inputName, string valueId) =>
        new(inputName, RuntimeInputBindingSource.DurableValue, durableValue: new RuntimeDurableValueReference(valueId));

    private static RuntimeInputBinding ReferenceBinding(string inputName, string referenceType, string referenceId) =>
        new(
            inputName,
            RuntimeInputBindingSource.Reference,
            reference: new RuntimeReferenceValue(
                referenceType,
                referenceId));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
