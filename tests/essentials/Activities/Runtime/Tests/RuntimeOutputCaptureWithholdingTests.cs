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
/// withholds such a result, so the capture has no value to write; neither a durable output nor a captured variable write
/// has a withheld form, so the capture is refused with <c>VF-ACT-010</c> before its storage driver encodes anything. The
/// projection reads the whole result (<c>$</c>), the path that hands the driver the activity's raw result object.
/// </summary>
public sealed class RuntimeOutputCaptureWithholdingTests
{
    private const string OutputName = "token";
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private static readonly ValueTypeDescriptor StringType = new("String");
    private readonly RecordingStorageDriver _driver = new();

    [Theory]
    [InlineData("output:token")]
    [InlineData("variable:token")]
    public async Task A_result_that_requires_encryption_is_refused_before_the_storage_driver_encodes_it(string valueId)
    {
        var exception = await Assert.ThrowsAsync<WithheldValueException>(() => CaptureAsync(valueId, requiresEncryption: true));

        Assert.Equal(SecretBindingDiagnostics.WithheldOutputNotCaptured(OutputName).Message, exception.Message);
        Assert.Empty(_driver.Encoded);
    }

    [Fact]
    public async Task An_ordinary_result_is_still_encoded_and_captured()
    {
        var projection = await CaptureAsync("output:token", requiresEncryption: false);

        Assert.Equal(Sentinel, Assert.Single(_driver.Encoded));
        Assert.Equal(Sentinel, Assert.Single(projection.DurableValues).State!.InlineValue!.Value.GetString());
    }

    private Task<RuntimeOutputCaptureProjection> CaptureAsync(string valueId, bool requiresEncryption)
    {
        var policy = ActivityValuePolicy.Default with { IsSensitive = requiresEncryption, RequiresEncryption = requiresEncryption };
        var descriptor = JsonSerializer.SerializeToElement(new { type = "test.token" });
        var contract = new ActivityContract(
            "test.token",
            "1",
            "test",
            descriptor,
            [],
            new ActivityResultContract(
                StringType,
                true,
                policy,
                [new ActivityResultProjectionContract(OutputName, "$", StringType, true, policy)]),
            ["Done"],
            new ActivityActivationRequirement("test", "test.token"));
        var transition = ActivityTransition.Complete(Sentinel);
        var completion = new ActivityCompletionProjector().Project(
            "invocation",
            new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, DateTimeOffset.UnixEpoch),
            contract,
            transition,
            DateTimeOffset.UnixEpoch);
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
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { [OutputName] = capture });

        return new RuntimeOutputCaptureProjector(new RuntimeDurableValueStorageDriverRegistry([_driver]))
            .ProjectAsync("workflow", "activity", node, (IActivityCompletionTransition)transition, completion, DateTimeOffset.UnixEpoch)
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
