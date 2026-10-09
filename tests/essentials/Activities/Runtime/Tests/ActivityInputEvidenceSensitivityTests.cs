using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The evaluated-value evidence record of an activity input, end to end from materialization to the run inspector's
/// view (spec 188, T070). Studio shows an evaluated value only when that record is not flagged sensitive, so an input
/// whose effective policy is sensitive, or requires encryption, must be flagged, and the value must reach neither the
/// view nor the inspection projection. A sensitive input that is not a credential may hold its value in the input
/// snapshot (spec FR-009); a value that requires encryption is persisted nowhere (FR-010).
/// </summary>
public sealed class ActivityInputEvidenceSensitivityTests : IAsyncDisposable
{
    private const string NodeId = "node-token";
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private readonly SecretValueRecorder _recorder = new();
    private readonly WorkflowExecutionHarness _harness;

    public ActivityInputEvidenceSensitivityTests() =>
        _harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services => services.AddSingleton(_recorder))
            .Build("actexec-token");

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task An_input_whose_effective_policy_is_sensitive_is_flagged_and_its_value_stays_out_of_the_evidence()
    {
        var run = await _harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Node(isSensitive: true, requiresEncryption: false)));

        run.AssertCompleted(NodeId);
        Assert.Equal([Sentinel], _recorder.Values);
        var input = await InputEvidenceAsync();
        Assert.True(input.IsSensitive);
        Assert.Null(input.WithheldKind);
        await AssertValueAbsentFromEvidenceAsync(input);
    }

    [Fact]
    public async Task An_input_whose_effective_policy_requires_encryption_is_withheld_flagged_and_never_hydrated()
    {
        var run = await _harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Node(isSensitive: false, requiresEncryption: true)));

        var state = run.State(NodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal(SecretBindingDiagnostics.WithheldInputNotResolved(SecretResolutionTestSupport.InputKey).Message, state.Fault!.Message);
        Assert.Empty(_recorder.Values);
        var input = await InputEvidenceAsync();
        Assert.True(input.IsSensitive);
        Assert.Equal(nameof(WithheldValueKind.PolicyRequiresEncryption), input.WithheldKind);
        Assert.Null(input.SecretReferenceName);
        await AssertValueAbsentFromEvidenceAsync(input);
        await SecretResolutionTestSupport.AssertNotPersistedAsync(_harness, [Sentinel]);
    }

    private Task<ActivityExecutionInspectionProjection?> ProjectionAsync() =>
        _harness.Services.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync(_harness.ExecutionId, "actexec-token").AsTask();

    private async Task<ActivityExecutionInspectionValueSnapshotView> InputEvidenceAsync()
    {
        var view = ActivityExecutionInspectionView.From((await ProjectionAsync())!, canInspectSensitiveValues: false);
        return Assert.Single(view.ValueSnapshots, snapshot => snapshot.InputKey == SecretResolutionTestSupport.InputKey);
    }

    private async Task AssertValueAbsentFromEvidenceAsync(ActivityExecutionInspectionValueSnapshotView input)
    {
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(input), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(await ProjectionAsync()), StringComparison.Ordinal);
    }

    // Pinned as publication pins it, so the harness keeps the binding's own policy instead of normalizing it.
    private static ExecutableNode Node(bool isSensitive, bool requiresEncryption)
    {
        var type = new ValueTypeDescriptor("String");
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive, requiresEncryption);
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(TokenReadingActivity));
        return new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: $"authored-{NodeId}",
            activityType: typeof(TokenReadingActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                // The effective policy as publication compiles it, which carries any declaration the activity makes.
                [SecretResolutionTestSupport.InputKey] = new(
                    SecretResolutionTestSupport.InputKey,
                    type,
                    policy,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(Sentinel), policy))
            },
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }
}

/// <summary>A leaf that records the value its input was hydrated with and completes.</summary>
public sealed class TokenReadingActivity(SecretValueRecorder recorder) : Activity<ActivityUnit>
{
    [ActivityInput(Key = SecretResolutionTestSupport.InputKey)]
    public string Token { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        recorder.Add(SecretValueRecorder.Execute, Token);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}
