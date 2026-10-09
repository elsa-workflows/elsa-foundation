using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Tests.Fixtures;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Producer withholding (spec 188, FR-010, T066). <c>RuntimeExternalEnvelopeStorage.RewriteAsync</c> is the
/// destination-storage decision that input materialization and intrinsic value writes share: it replaces a present value
/// whose effective policy requires encryption with a <see cref="WithheldValueKind.PolicyRequiresEncryption"/> marker, so
/// the value is neither kept inline nor written to external storage. The rewriter is internal, so these rows drive it
/// through the input materializer; <see cref="WorkflowIntrinsicWithholdingTests"/> drives it through the intrinsics.
/// </summary>
public sealed class RuntimeExternalEnvelopeStorageWithholdingTests
{
    private const string InputKey = "token";
    private const string StorageProfile = "payloads";
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private static readonly ValueTypeDescriptor StringType = new("String");
    private readonly RecordingPayloadStore _payloadStore = new();
    private readonly RuntimeActivityInputMaterializer _materializer;

    public RuntimeExternalEnvelopeStorageWithholdingTests() =>
        _materializer = new RuntimeActivityInputMaterializer(
            new RuntimeInputBindingResolver(),
            new StringTypeRegistry(),
            new ConstantPortableExpressionEvaluator(Sentinel),
            _payloadStore);

    [Theory]
    [InlineData(DurableValueStorage.Inline)]
    [InlineData(DurableValueStorage.External)]
    public async Task A_literal_whose_effective_policy_requires_encryption_is_withheld_and_never_stored(DurableValueStorage storage)
    {
        var snapshot = await MaterializeAsync(Literal(Policy(storage, requiresEncryption: true)));

        AssertWithheldForEncryption(snapshot);
    }

    [Fact]
    public async Task An_evaluated_expression_whose_effective_policy_requires_encryption_is_withheld_and_never_stored()
    {
        var binding = new RuntimeInputBinding(
            InputKey,
            StringType,
            Policy(DurableValueStorage.Inline, requiresEncryption: true),
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "token"));

        var snapshot = await MaterializeAsync(binding);

        AssertWithheldForEncryption(snapshot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("payload.token")]
    public async Task An_external_source_whose_policy_requires_encryption_is_withheld_and_never_written_again(string? path)
    {
        // The whole external value (no path) would keep its reference; a projection (a path) would be read and written
        // again under the destination policy. Both are withheld, so neither reference nor payload reaches the snapshot.
        _payloadStore.Payloads["requests/1"] = JsonSerializer.SerializeToElement(new { token = Sentinel });
        var policy = Policy(DurableValueStorage.External, requiresEncryption: true);
        var binding = new RuntimeInputBinding(
            InputKey,
            StringType,
            policy,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("payload", path));

        var snapshot = await MaterializeAsync(binding, new Dictionary<string, ValueEnvelope>
        {
            ["payload"] = ValueEnvelope.External(StringType, new DurableValueExternalReference(StorageProfile, "requests/1", new Dictionary<string, string>()), policy)
        });

        AssertWithheldForEncryption(snapshot);
    }

    [Fact]
    public async Task An_expression_reading_a_value_whose_policy_requires_encryption_is_withheld()
    {
        // The evaluated value takes the strictest policy of what it reads, so it requires encryption too.
        var binding = new RuntimeInputBinding(
            InputKey,
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "test",
                "value",
                parameters: new Dictionary<string, ExpressionParameterBinding> { ["value"] = new WorkflowRequestExpressionParameterBinding("source") }));

        var snapshot = await MaterializeAsync(binding, new Dictionary<string, ValueEnvelope>
        {
            ["source"] = ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(Sentinel), Policy(DurableValueStorage.Inline, requiresEncryption: true))
        });

        AssertWithheldForEncryption(snapshot);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_present_value_whose_policy_does_not_require_encryption_stays_inline(bool isSensitive)
    {
        var snapshot = await MaterializeAsync(Literal(Policy(DurableValueStorage.Inline, requiresEncryption: false, isSensitive)));

        var value = snapshot.Values[InputKey];
        Assert.Equal(ValuePresence.Present, value.Presence);
        Assert.Null(value.WithheldValue);
        Assert.Equal(Sentinel, value.InlineValue!.Value.GetString());
        Assert.Empty(_payloadStore.Writes);
    }

    [Fact]
    public async Task A_present_value_whose_policy_does_not_require_encryption_is_still_written_to_external_storage()
    {
        var snapshot = await MaterializeAsync(Literal(Policy(DurableValueStorage.External, requiresEncryption: false)));

        var value = snapshot.Values[InputKey];
        Assert.Equal(ValuePresence.Present, value.Presence);
        Assert.NotNull(value.ExternalReference);
        Assert.Equal(Sentinel, Assert.Single(_payloadStore.Writes).Payload.GetString());
    }

    [Fact]
    public async Task An_explicit_null_that_requires_encryption_holds_no_value_and_stays_null()
    {
        var policy = Policy(DurableValueStorage.Inline, requiresEncryption: true);
        var binding = new RuntimeInputBinding(InputKey, StringType, policy, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Null(StringType, policy));

        var snapshot = await MaterializeAsync(binding);

        Assert.Equal(ValuePresence.ExplicitNull, snapshot.Values[InputKey].Presence);
        Assert.Null(snapshot.Values[InputKey].WithheldValue);
    }

    private void AssertWithheldForEncryption(ActivityInputSnapshot snapshot)
    {
        var value = snapshot.Values[InputKey];
        Assert.Equal(ValuePresence.Withheld, value.Presence);
        Assert.Equal(WithheldValueKind.PolicyRequiresEncryption, value.WithheldValue!.Kind);
        Assert.Null(value.WithheldValue.Secret);
        Assert.Null(value.InlineValue);
        Assert.Null(value.ExternalReference);
        Assert.True(value.Policy.RequiresEncryption);
        Assert.Equal("String", value.Type.Alias);
        Assert.Empty(_payloadStore.Writes);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
    }

    private ValueTask<ActivityInputSnapshot> MaterializeAsync(
        RuntimeInputBinding binding,
        IReadOnlyDictionary<string, ValueEnvelope>? workflowInputs = null) =>
        _materializer.MaterializeSnapshotAsync(
            Node(binding),
            "consumer",
            new RuntimeInputBindingResolutionContext("workflow-1", "consumer", workflowInputEnvelopes: workflowInputs),
            DateTimeOffset.UnixEpoch);

    private static RuntimeInputBinding Literal(ValueProtectionPolicy policy) =>
        new(
            InputKey,
            StringType,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(Sentinel), policy));

    private static ValueProtectionPolicy Policy(DurableValueStorage storage, bool requiresEncryption, bool isSensitive = false) =>
        new(
            DurableValueLifecycle.Instance,
            storage,
            isSensitive,
            requiresEncryption,
            metadata: new Dictionary<string, string> { [ValuePolicyCombiner.StorageProfileMetadataKey] = StorageProfile });

    private static ExecutableNode Node(RuntimeInputBinding binding)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "consumer" });
        var contract = new ActivityContract(
            "test/consumer",
            "1.0.0",
            "test",
            descriptor,
            [new ActivityInputContract(InputKey, "Token", StringType, false, true, false, null, ActivityValuePolicy.Default)],
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
            new Dictionary<string, RuntimeInputBinding> { [InputKey] = binding },
            new Dictionary<string, string>(),
            activityContract: contract);
    }

    /// <summary>Returns the expression's <c>value</c> parameter when it has one, and the sentinel otherwise.</summary>
    private sealed class ConstantPortableExpressionEvaluator(string value) : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(request.ParameterValues.TryGetValue("value", out var parameter)
                ? parameter.Clone()
                : JsonSerializer.SerializeToElement(value));
    }

    /// <summary>Serves the payloads a row plants and records every payload written, so a row can prove none was.</summary>
    private sealed class RecordingPayloadStore : IExternalPayloadStore
    {
        public Dictionary<string, JsonElement> Payloads { get; } = new(StringComparer.Ordinal);
        public List<ExternalPayloadWriteRequest> Writes { get; } = [];

        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default)
        {
            Writes.Add(request);
            return ValueTask.FromResult(new DurableValueExternalReference(request.StorageProfile, $"locator-{Writes.Count}", new Dictionary<string, string>()));
        }

        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Payloads[reference.Locator]);
    }
}
