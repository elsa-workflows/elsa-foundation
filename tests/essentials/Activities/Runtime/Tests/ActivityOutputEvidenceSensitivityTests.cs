using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The output evidence record of an activity whose result policy requires encryption (spec 188, FR-010), end to end from
/// completion to the inspection projection. Nothing scans that record at commit, so it is flagged sensitive where it is
/// built and the payload capture policy captures nothing for it; a hand-built or imported contract can declare such a
/// policy (research R13).
/// </summary>
public sealed class ActivityOutputEvidenceSensitivityTests : IAsyncDisposable
{
    private const string NodeId = "node-issuer";
    private const string ActivityExecutionId = "actexec-issuer";
    private readonly WorkflowExecutionHarness _harness = WorkflowExecutionHarness.Create().Build(ActivityExecutionId);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task An_output_whose_result_policy_requires_encryption_is_flagged_and_not_captured()
    {
        var output = await RunAndReadOutputAsync(ActivityValuePolicy.Default with { RequiresEncryption = true });

        Assert.True(output.IsSensitive);
        Assert.Equal(RuntimePayloadCaptureMode.None, output.CaptureMode);
        Assert.Null(output.Payload);
        Assert.DoesNotContain(TokenIssuingActivity.Token, JsonSerializer.Serialize(await ProjectionAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ordinary_output_is_captured()
    {
        var output = await RunAndReadOutputAsync(ActivityValuePolicy.Default);

        Assert.False(output.IsSensitive);
        Assert.Contains(TokenIssuingActivity.Token, output.Payload!.Value.GetRawText(), StringComparison.Ordinal);
    }

    private async Task<ActivityExecutionInspectionValueSnapshot> RunAndReadOutputAsync(ActivityValuePolicy resultPolicy)
    {
        var run = await _harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Node(resultPolicy)));
        run.AssertCompleted(NodeId);
        return Assert.Single((await ProjectionAsync())!.ValueSnapshots, snapshot => snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityOutput);
    }

    private Task<ActivityExecutionInspectionProjection?> ProjectionAsync() =>
        _harness.Services.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync(_harness.ExecutionId, ActivityExecutionId).AsTask();

    // The contract the CLR scanner would build, with the result policy a hand-built or imported contract declares.
    private static ExecutableNode Node(ActivityValuePolicy resultPolicy)
    {
        var scanned = ClrActivityContractTestBuilder.BuildContract(typeof(TokenIssuingActivity));
        var contract = new ActivityContract(
            scanned.ActivityTypeKey,
            scanned.ContractVersion,
            scanned.DescriptorKind,
            scanned.DescriptorPayload,
            scanned.Inputs.Values,
            new ActivityResultContract(scanned.Result.Type, scanned.Result.IsRequired, resultPolicy, scanned.Result.Projections.Values),
            scanned.Outcomes,
            scanned.Activation,
            scanned.SideEffectProfile);
        return new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: $"authored-{NodeId}",
            activityType: typeof(TokenIssuingActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }
}

/// <summary>A leaf that returns one token as its only output.</summary>
public sealed class TokenIssuingActivity : Activity<IssuedToken>
{
    public static readonly string Token = $"plain{Guid.NewGuid():N}";

    protected override ValueTask<ActivityTransition<IssuedToken>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new IssuedToken(Token), ActivityOutcomes.Done));
}

public sealed record IssuedToken([property: Output] string Value);
