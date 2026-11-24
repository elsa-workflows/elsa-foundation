using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ScopedJsonInputConversionTests
{
    private static readonly ValueTypeDescriptor Text = new("String");
    private static readonly ValueTypeDescriptor OrderType = new(typeof(Order).FullName!);
    private const string Raw = """{ "wire_name":"Ren\u00e9" }""";

    [Theory]
    [InlineData("literal")]
    [InlineData("request")]
    [InlineData("variable")]
    [InlineData("member")]
    [InlineData("expression")]
    [InlineData("external")]
    [InlineData("projection")]
    public async Task Every_profile_input_path_uses_the_required_member_mapping_and_retains_raw_JSON(string source)
    {
        var (snapshot, contract, store, warnings) = await Materialize(source, "mapped", Raw);
        var envelope = snapshot.Values["mapped"];
        var payload = envelope.InlineValue ?? await store.ReadAsync(envelope.ExternalReference!);
        Assert.Equal(Raw, payload.GetRawText());
        Assert.Empty(warnings.Warnings);
        var activity = new Consumer();
        var inputs = new ActivityInputSnapshot(snapshot.InvocationId, snapshot.ContractFingerprint, snapshot.BindingFingerprint,
            new Dictionary<string, ValueEnvelope> { ["mapped"] = ValueEnvelope.Inline(OrderType, payload, envelope.Policy) }, snapshot.MaterializedAt);
        new ActivityInputHydrator().Hydrate(activity, contract, inputs, warnings);
        Assert.Equal("René", activity.Mapped!.Name);
        Assert.Empty(warnings.Warnings);
    }

    [Fact]
    public async Task A_member_override_can_select_case_insensitive_reads_without_changing_other_inputs()
    {
        var (_, _, _, warnings) = await Materialize("literal", "insensitive", "{\"NAME\":\"Ada\"}");
        Assert.Empty(warnings.Warnings);
        await Assert.ThrowsAsync<RuntimeValueConversionException>(() => Materialize("literal", "default", "{\"NAME\":\"Ada\"}"));
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("member")]
    [InlineData("external")]
    public async Task Strict_member_overrides_refuse_unknown_keys_before_a_snapshot_exists(string source)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Materialize(source, "strict", "{\"wire_name\":\"Ada\",\"extra\":1}"));
        Assert.Contains("json-unmatched-member-refused", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selected_member_fit_records_the_successful_warning_once_with_the_sources_protection()
    {
        var (_, _, _, warnings) = await Materialize("member", "mapped", "{\"wire_name\":\"Ada\",\"privateKey\":1}", sensitive: true);
        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal("json-unmatched-member", warning.Code);
        Assert.Equal("args.mapped", warning.Details!["path"]);
        Assert.DoesNotContain("privateKey", warning.Message, StringComparison.Ordinal);
    }

    private static async Task<(ActivityInputSnapshot Snapshot, ActivityContract Contract, PayloadStore Store, ExecutionWarningCollector Warnings)> Materialize(
        string source, string key, string raw, bool sensitive = false)
    {
        var registry = SerializationServices.TypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(Order), OrderType.Alias);
        registry.RegisterType(typeof(Consumer), typeof(Consumer).FullName!);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: sensitive);
        var text = JsonSerializer.SerializeToElement(raw, JsonBaseline.Options);
        var store = new PayloadStore(source == "projection"
            ? JsonSerializer.SerializeToElement(new { selected = text }, JsonBaseline.Options) : text);
        var envelope = source is "external" or "projection"
            ? ValueEnvelope.External(Text, new("external", "source", new Dictionary<string, string>()), policy)
            : ValueEnvelope.Inline(Text, text, policy);
        var plan = new ValueConversionPlan(ValueConversionPlan.CurrentSchemaVersion, ValueRepresentation.FormattedContent,
            Text, OrderType, ValueConversionMode.Json, ValueConversionOperation.Profile, new("elsa.json", "1"), null, null);
        var role = source switch
        {
            "literal" => RuntimeInputBindingSource.Literal,
            "variable" => RuntimeInputBindingSource.VariableRead,
            "member" => RuntimeInputBindingSource.WorkflowRequestMember,
            "expression" => RuntimeInputBindingSource.Expression,
            _ => RuntimeInputBindingSource.WorkflowRequest
        };
        var binding = new RuntimeInputBinding(key, OrderType, ValueProtectionPolicy.InstanceInline, role,
            selection: source == "member" ? new RuntimeReadSelection(1, ["selected"], RuntimeReadSelectionMode.DeferredFit) : null,
            literal: source == "literal" ? envelope : null,
            workflowRequest: role is RuntimeInputBindingSource.WorkflowRequest or RuntimeInputBindingSource.WorkflowRequestMember
                ? new("source", source == "projection" ? "selected" : null) : null,
            variable: source == "variable" ? new("source", "root") : null,
            expression: source == "expression" ? new("Test", "source") : null,
            conversionPlan: plan);
        var descriptor = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(typeof(Consumer).FullName!), JsonBaseline.Options);
        var contract = new ActivityContract(typeof(Consumer).FullName!, "1.0.0", WellKnownRuntimeActivityConsumers.ClrActivity, descriptor,
            [new ActivityInputContract(key, key, OrderType, true, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new("Unit"), false, ActivityValuePolicy.Default, []), ["Done"],
            new ActivityActivationRequirement(WellKnownRuntimeActivityConsumers.ClrActivity, "test"));
        var node = new ExecutableNode("consumer", "consumer", typeof(Consumer).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, descriptor,
            new Dictionary<string, RuntimeInputBinding> { [key] = binding }, new Dictionary<string, string>(), activityContract: contract);
        var warnings = new ExecutionWarningCollector();
        var context = new RuntimeInputBindingResolutionContext("workflow", "invocation",
            workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
            {
                ["source"] = source == "member"
                    ? ValueEnvelope.Inline(new("Any"), JsonSerializer.SerializeToElement(new { selected = text }, JsonBaseline.Options), policy)
                    : envelope
            },
            variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope> { [new("root", "source")] = envelope }) { Warnings = warnings };
        var materializer = new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), [], registry,
            portableExpressionEvaluator: new Expression(text), externalPayloadStore: store);
        return (await materializer.MaterializeSnapshotAsync(node, "invocation", context, DateTimeOffset.UnixEpoch), contract, store, warnings);
    }

    private sealed class Order { public required string Name { get; set; } }
    private sealed class Consumer : StructuralActivity
    {
        [ActivityInput(Key = "mapped"), JsonConversion(MemberMappings = ["wire_name=Name"])] public Order? Mapped { get; set; }
        [ActivityInput(Key = "insensitive"), JsonConversion(CaseInsensitive = true)] public Order? Insensitive { get; set; }
        [ActivityInput(Key = "strict"), JsonConversion(MemberMappings = ["wire_name=Name"], RefuseUnmatchedMembers = true)] public Order? Strict { get; set; }
        [ActivityInput(Key = "default")] public Order? Default { get; set; }
    }
    private sealed class Expression(JsonElement text) : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => ValueTask.FromResult(text);
    }
    private sealed class PayloadStore(JsonElement source) : IExternalPayloadStore
    {
        private JsonElement written;
        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(reference.Locator == "source" ? source : written);
        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default)
        {
            written = request.Payload.Clone();
            return ValueTask.FromResult(new DurableValueExternalReference("external", "written", new Dictionary<string, string>()));
        }
    }
}
