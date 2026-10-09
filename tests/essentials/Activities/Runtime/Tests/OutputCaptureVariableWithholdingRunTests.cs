using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// An activity whose result policy requires encryption (spec 188, FR-010), with its output captured into a workflow
/// variable, end to end through the invoke handler and the commit. The capture writes the withheld marker into the root
/// variable frame, as <c>Set</c> does, and the activity completes; nothing committed carries the value, and a reader of
/// the variable refuses the marker with <c>VF-ACT-010</c>. A hand-built or imported contract can declare such a policy
/// (research R13).
/// </summary>
public sealed class OutputCaptureVariableWithholdingRunTests : IAsyncDisposable
{
    private const string NodeId = "node-issuer";
    private const string ActivityExecutionId = "actexec-issuer";
    private const string VariableKey = "var-token";
    private static readonly ValueTypeDescriptor StringType = new("String");
    private readonly WorkflowExecutionHarness _harness = WorkflowExecutionHarness.Create().Build(ActivityExecutionId);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task A_captured_result_that_requires_encryption_completes_with_the_marker_in_the_variable_and_no_value_committed()
    {
        var run = await _harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            Node(),
            new RuntimeVariableDeclaration(VariableKey, "token", StringType, ValueProtectionPolicy.InstanceInline)));

        run.AssertCompleted(NodeId);
        var variable = run.WorkflowState!.RootVariableFrame!.Values[VariableKey];
        Assert.Equal(ValuePresence.Withheld, variable.Presence);
        Assert.Equal(WithheldValueKind.PolicyRequiresEncryption, variable.WithheldValue!.Kind);
        Assert.True(variable.Policy.RequiresEncryption);
        Assert.DoesNotContain(TokenIssuingActivity.Token, JsonSerializer.Serialize(run.WorkflowState), StringComparison.Ordinal);
        Assert.DoesNotContain(TokenIssuingActivity.Token, JsonSerializer.Serialize(run.ActivityStates), StringComparison.Ordinal);

        var read = new RuntimeInputBinding(
            "token",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(VariableKey, VariableReference.WorkflowScopeId));
        var exception = Assert.Throws<WithheldValueException>(() => new RuntimeInputBindingResolver().Resolve(
            read,
            new RuntimeInputBindingResolutionContext(
                _harness.ExecutionId,
                "actexec-reader",
                variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>
                {
                    [new RuntimeVariableValueAddress(VariableReference.WorkflowScopeId, VariableKey)] = variable
                })));
        Assert.Equal(SecretBindingDiagnostics.WithheldVariableNotResolved(VariableKey).Message, exception.Message);
    }

    // The contract the CLR scanner would build, with a result policy that requires encryption, and its one projection
    // captured into a workflow variable as the publish compiler emits it.
    private static ExecutableNode Node()
    {
        var scanned = ClrActivityContractTestBuilder.BuildContract(typeof(TokenIssuingActivity));
        var contract = new ActivityContract(
            scanned.ActivityTypeKey,
            scanned.ContractVersion,
            scanned.DescriptorKind,
            scanned.DescriptorPayload,
            scanned.Inputs.Values,
            new ActivityResultContract(
                scanned.Result.Type,
                scanned.Result.IsRequired,
                ActivityValuePolicy.Default with { RequiresEncryption = true },
                scanned.Result.Projections.Values),
            scanned.Outcomes,
            scanned.Activation,
            scanned.SideEffectProfile);
        var projection = Assert.Single(contract.Result.Projections).Key;
        var capture = new RuntimeOutputCapture(
            projection,
            $"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}token",
            new RuntimeValueTypeDescriptor(StringType.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = VariableKey,
                [RuntimeMetadataKeys.VariableName] = "token"
            });
        return new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: $"authored-{NodeId}",
            activityType: typeof(TokenIssuingActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { [projection] = capture });
    }
}
