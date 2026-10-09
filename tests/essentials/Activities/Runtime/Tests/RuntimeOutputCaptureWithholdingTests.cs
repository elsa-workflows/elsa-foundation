using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// An output capture of an activity result whose policy requires encryption (spec 188, FR-010). The completion projector
/// withholds such a result, so the capture has no value to write, and no storage driver encodes anything. A capture into a
/// workflow variable writes the withheld marker into the variable, as <c>Set</c> does; a capture into a durable output,
/// which has no withheld form, is refused with <c>VF-ACT-010</c>. A present projection whose policy requires encryption,
/// which only a projection built outside the completion projector carries, is treated the same. The projection reads the
/// whole result (<c>$</c>), the path that hands the driver the activity's raw result object.
/// </summary>
public sealed class RuntimeOutputCaptureWithholdingTests
{
    private const string OutputName = "token";
    private const string VariableTarget = "variable:token";
    private const string OutputTarget = "output:token";
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly ValueProtectionPolicy EncryptionRequired =
        new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);
    private readonly RecordingStorageDriver _driver = new();

    [Fact]
    public async Task A_withheld_result_captured_into_a_variable_is_written_as_its_marker_without_reaching_the_storage_driver()
    {
        var projection = await CaptureAsync(VariableTarget, ProjectedCompletion(requiresEncryption: true));

        AssertWithheldVariableWrite(projection);
    }

    [Fact]
    public async Task A_present_projection_that_requires_encryption_captured_into_a_variable_is_withheld_too()
    {
        var projection = await CaptureAsync(VariableTarget, HandBuiltPresentCompletion());

        AssertWithheldVariableWrite(projection);
    }

    [Fact]
    public async Task A_withheld_result_captured_into_a_durable_output_is_refused_before_the_storage_driver_encodes_it() =>
        await AssertRefusedAsync(ProjectedCompletion(requiresEncryption: true));

    [Fact]
    public async Task A_present_projection_that_requires_encryption_captured_into_a_durable_output_is_refused_too() =>
        await AssertRefusedAsync(HandBuiltPresentCompletion());

    [Fact]
    public async Task An_ordinary_result_is_still_encoded_and_captured()
    {
        var projection = await CaptureAsync(OutputTarget, ProjectedCompletion(requiresEncryption: false));

        Assert.Equal(Sentinel, Assert.Single(_driver.Encoded));
        Assert.Equal(Sentinel, Assert.Single(projection.DurableValues).State!.InlineValue!.Value.GetString());
    }

    private void AssertWithheldVariableWrite(RuntimeOutputCaptureProjection projection)
    {
        var withheld = Assert.Single(projection.WorkflowVariableWrites).Value.Withheld;
        Assert.NotNull(withheld);
        Assert.Equal(WithheldValueKind.PolicyRequiresEncryption, withheld.WithheldValue!.Kind);
        Assert.True(withheld.Policy.RequiresEncryption);
        Assert.Empty(projection.DurableValues);
        Assert.Empty(_driver.Encoded);
    }

    private async Task AssertRefusedAsync(ActivityCompletionProjection completion)
    {
        var exception = await Assert.ThrowsAsync<WithheldValueException>(() => CaptureAsync(OutputTarget, completion));

        Assert.Equal(SecretBindingDiagnostics.WithheldOutputNotCaptured(OutputName).Message, exception.Message);
        Assert.Empty(_driver.Encoded);
    }

    private static ActivityContract Contract(bool requiresEncryption)
    {
        var policy = ActivityValuePolicy.Default with { IsSensitive = requiresEncryption, RequiresEncryption = requiresEncryption };
        return new ActivityContract(
            "test.token",
            "1",
            "test",
            JsonSerializer.SerializeToElement(new { type = "test.token" }),
            [],
            new ActivityResultContract(
                StringType,
                true,
                policy,
                [new ActivityResultProjectionContract(OutputName, "$", StringType, true, policy)]),
            ["Done"],
            new ActivityActivationRequirement("test", "test.token"));
    }

    private static ActivityCompletionProjection ProjectedCompletion(bool requiresEncryption) =>
        new ActivityCompletionProjector().Project(
            "invocation",
            new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, DateTimeOffset.UnixEpoch),
            Contract(requiresEncryption),
            ActivityTransition.Complete(Sentinel),
            DateTimeOffset.UnixEpoch);

    // Built outside the completion projector, which would have withheld it: the projection is present, holds the value,
    // and its policy requires encryption.
    private static ActivityCompletionProjection HandBuiltPresentCompletion() =>
        ProjectedCompletion(requiresEncryption: false) with
        {
            Projections = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal)
            {
                [OutputName] = ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(Sentinel), EncryptionRequired)
            }
        };

    private Task<RuntimeOutputCaptureProjection> CaptureAsync(string valueId, ActivityCompletionProjection completion)
    {
        var contract = Contract(requiresEncryption: true);
        var capture = new RuntimeOutputCapture(
            OutputName,
            valueId,
            new RuntimeValueTypeDescriptor("String", WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.TargetVariableReferenceKey] = "token" });
        var node = new ExecutableNode(
            "token-node",
            "token-node",
            "test.token",
            "1",
            "test",
            contract.DescriptorPayload,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { [OutputName] = capture });

        return new RuntimeOutputCaptureProjector(new RuntimeDurableValueStorageDriverRegistry([_driver]))
            .ProjectAsync("workflow", "activity", node, (IActivityCompletionTransition)ActivityTransition.Complete(Sentinel), completion, DateTimeOffset.UnixEpoch)
            .AsTask();
    }

    /// <summary>The built-in JSON driver, recording every value it is handed to encode.</summary>
    private sealed class RecordingStorageDriver : IRuntimeDurableValueStorageDriver
    {
        private readonly JsonRuntimeDurableValueStorageDriver _inner = new();

        public List<object?> Encoded { get; } = [];

        public string DriverKey => _inner.DriverKey;

        public ValueTask<RuntimeDurableValueEncoding> EncodeAsync(object? value, RuntimeValueTypeDescriptor type, CancellationToken cancellationToken = default)
        {
            Encoded.Add(value);
            return _inner.EncodeAsync(value, type, cancellationToken);
        }

        public ValueTask<object?> DecodeAsync(DurableValueState state, CancellationToken cancellationToken = default) =>
            _inner.DecodeAsync(state, cancellationToken);
    }
}
