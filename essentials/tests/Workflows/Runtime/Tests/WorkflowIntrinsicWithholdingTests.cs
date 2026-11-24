using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The intrinsics that receive a withheld value from producer withholding. Publication
/// refuses every binding that could hand an intrinsic a value whose policy requires encryption, so only an artifact that
/// skipped publication reaches these paths. <c>Set</c> and <c>Return</c> keep the marker, which holds no value; the
/// intrinsics that read the value, <c>Control</c>, the identity intrinsics and <c>SetOutput</c>, refuse it with
/// <c>VF-ACT-010</c> before reading it.
/// </summary>
public sealed class WorkflowIntrinsicWithholdingTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private const string IntrinsicExecutionId = "actexec-intrinsic";
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly WorkflowExecutableIdentity Identity = new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
    private readonly InMemoryWorkflowExecutionStateStore _workflowStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStore = new();
    private readonly WorkflowIntrinsicExecutor _executor;

    public WorkflowIntrinsicWithholdingTests() =>
        _executor = new WorkflowIntrinsicExecutor(
            _workflowStore,
            _activityStore,
            new RuntimeInputBindingResolver(),
            new InMemoryDurableValueStateStore(),
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            new FakeTimeProvider(Now));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_protected_null_is_checked_before_substitution_and_withholding(bool policy, bool output)
    {
        var type = new ValueTypeDescriptor("Int32");
        var protection = EncryptionRequired(DurableValueStorage.Inline);
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["value"] = new("value", type, protection, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Null(type, protection))
        };
        if (output) bindings["name"] = Literal("name", "result", ValueProtectionPolicy.InstanceInline);
        var node = new ExecutableNode("node-null", "node-null", "test.null", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            bindings, new Dictionary<string, string>(), intrinsicKind: output ? WorkflowIntrinsicKind.SetOutput : WorkflowIntrinsicKind.Set,
            intrinsicVariable: output ? null : new("greeting", VariableReference.WorkflowScopeId));
        if (!policy)
        {
            Assert.Contains("VF-ACT-004", (await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ExecuteAsync(node, new(false, false), type).AsTask())).Message);
        }
        else if (output)
            await Assert.ThrowsAsync<WithheldValueException>(() => ExecuteAsync(node, new(true, false), type).AsTask());
        else
        {
            var commit = await ExecuteAsync(node, new(true, false), type);
            AssertWithheldForEncryption(commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["greeting"]);
            Assert.Contains(Assert.Single(commit.StateChanges.ActivityExecutions).State!.Warnings!, warning => warning.Code == "VF-NULL-001");
        }
    }

    [Theory]
    [InlineData(DurableValueStorage.Inline)]
    [InlineData(DurableValueStorage.External)]
    [InlineData(DurableValueStorage.Custom)]
    public async Task SetOutput_persists_explicit_JSON_null_in_every_storage_kind(DurableValueStorage storage)
    {
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, storage);
        var value = new RuntimeInputBinding("value", StringType, policy, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Null(StringType, policy));
        var node = new ExecutableNode("null-output", "null-output", "test", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = value, ["name"] = Literal("name", "result", ValueProtectionPolicy.InstanceInline) },
            new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.SetOutput);
        var commit = await ExecuteAsync(node);
        var output = Assert.Single(commit.StateChanges.DurableValues).State!;
        Assert.Equal(DurableValueStorage.Inline, output.Storage);
        // Both ordinary serialization and the reader must retain presence and the original minimum policy.
        var reloaded = JsonSerializer.Deserialize<DurableValueState>(JsonSerializer.Serialize(output))!;
        var envelope = RuntimeInputBindingStateProjection.ProjectEnvelopesByMetadataKey([reloaded], Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.OutputName)["result"];
        Assert.Equal(ValuePresence.ExplicitNull, envelope.Presence);
        Assert.Equal(storage, envelope.Policy.Storage);
        Assert.Equal(JsonValueKind.Null, output.InlineValue!.Value.ValueKind);
    }

    [Fact]
    public async Task Set_writes_the_variable_as_a_withheld_marker_that_holds_no_value()
    {
        var node = Node(WorkflowIntrinsicKind.Set, intrinsicVariable: new RuntimeVariableReference("greeting", VariableReference.WorkflowScopeId));

        var commit = await ExecuteAsync(node);

        AssertWithheldForEncryption(commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["greeting"]);
        ValidateAsCommitted(commit);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(commit.StateChanges.WorkflowExecution.State), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Return_completes_with_the_withheld_marker_as_its_result_and_a_reader_of_that_result_refuses_it()
    {
        var node = Node(WorkflowIntrinsicKind.Return);

        var commit = await ExecuteAsync(node);

        var completed = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        AssertWithheldForEncryption(completed.Completion!.Result);
        ValidateAsCommitted(commit);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(completed), StringComparison.Ordinal);

        var consumer = ActivityState("actexec-consumer", "node-consumer", ActivityExecutionStatus.Running, schedulingActivityExecutionId: completed.InvocationId);
        var read = new RuntimeInputBinding(
            "customer-id",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference(node.ExecutableNodeId, "$result", VariableReference.WorkflowScopeId));
        var exception = Assert.Throws<WithheldValueException>(() => new RuntimeInputBindingResolver().Resolve(
            read,
            new RuntimeInputBindingResolutionContext(WorkflowExecutionId, consumer.InvocationId, consumerInvocation: consumer, runtimeView: [completed, consumer])));
        Assert.Equal(SecretBindingDiagnostics.WithheldSourceNotResolved("customer-id").Message, exception.Message);
    }

    [Theory]
    [InlineData(WorkflowIntrinsicKind.Control, WorkflowIntrinsicInputKeys.Outcome)]
    [InlineData(WorkflowIntrinsicKind.SetCorrelationId, WorkflowIntrinsicInputKeys.Value)]
    [InlineData(WorkflowIntrinsicKind.SetInstanceName, WorkflowIntrinsicInputKeys.Value)]
    public async Task An_intrinsic_that_reads_the_value_refuses_a_withheld_one_with_the_fixed_code(WorkflowIntrinsicKind kind, string inputKey)
    {
        var exception = await Assert.ThrowsAsync<WithheldValueException>(() => ExecuteAsync(Node(kind, inputKey: inputKey)).AsTask());

        Assert.Equal(SecretBindingDiagnostics.WithheldInputNotResolved(inputKey).Message, exception.Message);
    }

    /// <summary>
    /// A durable output has no withheld form. Under inline storage the payload-less row would be refused by its own
    /// constructor with a generic error; under custom storage it would be written, and a parent would read the output as
    /// absent. Both are refused with the fixed code instead.
    /// </summary>
    [Theory]
    [InlineData(DurableValueStorage.Inline)]
    [InlineData(DurableValueStorage.Custom)]
    public async Task SetOutput_refuses_a_withheld_value_instead_of_writing_an_output_without_its_payload(DurableValueStorage storage)
    {
        var node = Node(
            WorkflowIntrinsicKind.SetOutput,
            policy: EncryptionRequired(storage),
            extraBindings: [Literal(WorkflowIntrinsicInputKeys.Name, "result", ValueProtectionPolicy.InstanceInline)]);

        var exception = await Assert.ThrowsAsync<WithheldValueException>(() => ExecuteAsync(node).AsTask());

        Assert.Equal(SecretBindingDiagnostics.WithheldInputNotResolved(WorkflowIntrinsicInputKeys.Value).Message, exception.Message);
    }

    // The committer folds a commit's intents into its outbox before validating it; the marker passes the backstop.
    private static void ValidateAsCommitted(RuntimeCheckpointCommit commit) =>
        RuntimeCheckpointCommitValidator.Validate(commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit))
        });

    private static void AssertWithheldForEncryption(ValueEnvelope value)
    {
        Assert.Equal(ValuePresence.Withheld, value.Presence);
        Assert.Equal(WithheldValueKind.PolicyRequiresEncryption, value.WithheldValue!.Kind);
        Assert.Null(value.InlineValue);
        Assert.Null(value.ExternalReference);
        Assert.True(value.Policy.RequiresEncryption);
    }

    private async ValueTask<RuntimeCheckpointCommit> ExecuteAsync(ExecutableNode node, WorkflowValuePolicies? policies = null, ValueTypeDescriptor? variableType = null)
    {
        var scheduled = ActivityState(IntrinsicExecutionId, node.ExecutableNodeId, ActivityExecutionStatus.Scheduled) with { Attempts = [] };
        var workflow = WorkflowState();
        if (variableType is not null)
            workflow = workflow with { RootVariableFrame = new VariableFrameFactory().CreateRoot(WorkflowExecutionId, VariableReference.WorkflowScopeId,
                new Dictionary<string, ValueEnvelope> { ["greeting"] = ValueEnvelope.Inline(variableType, JsonSerializer.SerializeToElement(0), ValueProtectionPolicy.InstanceInline) }) };
        await _workflowStore.SaveAsync(workflow);
        await _activityStore.SaveAsync(scheduled);
        var payload = new RuntimeStartActivityCommandPayload(Identity, node.ExecutableNodeId, IntrinsicExecutionId, RuntimeStartActivityCommandPayload.ScheduledActivityReason);
        var executable = new WorkflowExecutable(
            Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(),
            inputContract: null, dependencies: null, runtimeRequirements: null, storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference, persistenceMode: null, workflowVariables: null, valuePolicies: policies);
        var workItem = new RuntimeSchedulerWorkItem(
            "start-intrinsic",
            WorkflowExecutionId,
            "command-intrinsic",
            WorkflowExecutionCommandKind.StartActivity,
            "envelope-1",
            $"{WorkflowExecutionId}:start:{IntrinsicExecutionId}",
            Now,
            Now,
            1,
            JsonSerializer.SerializeToElement(payload),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        return await _executor.ExecuteAsync(workItem, payload, executable, node, scheduled);
    }

    private static ExecutableNode Node(
        WorkflowIntrinsicKind kind,
        string inputKey = WorkflowIntrinsicInputKeys.Value,
        ValueProtectionPolicy? policy = null,
        RuntimeVariableReference? intrinsicVariable = null,
        IReadOnlyCollection<RuntimeInputBinding>? extraBindings = null)
    {
        var bindings = (extraBindings ?? []).Append(Literal(inputKey, Sentinel, policy ?? EncryptionRequired(DurableValueStorage.Inline)))
            .ToDictionary(binding => binding.InputName, StringComparer.Ordinal);
        var name = kind.ToString().ToLowerInvariant();
        return new ExecutableNode(
            $"node-{name}",
            $"authored-{name}",
            $"elsa.intrinsic.{name}",
            "1.0.0",
            "intrinsic",
            JsonSerializer.SerializeToElement(new { }),
            bindings,
            new Dictionary<string, string>(),
            intrinsicKind: kind,
            intrinsicVariable: intrinsicVariable);
    }

    // A literal whose policy requires encryption: only a hand-built or imported artifact carries one, because publication
    // refuses it (VF-ACT-011) and refuses every secret binding on an intrinsic (VF-ACT-012).
    private static RuntimeInputBinding Literal(string inputKey, string value, ValueProtectionPolicy policy) =>
        new(inputKey, StringType, policy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), policy));

    private static ValueProtectionPolicy EncryptionRequired(DurableValueStorage storage) =>
        new(DurableValueLifecycle.Instance, storage, isSensitive: true, requiresEncryption: true);

    private static WorkflowExecutionState WorkflowState() =>
        new(WorkflowExecutionId, Identity, WorkflowExecutionStatus.Running, null, Now, Now, Now, null, null, null, null, new Dictionary<string, string>())
        {
            RootVariableFrame = new VariableFrameFactory().CreateRoot(
                WorkflowExecutionId,
                VariableReference.WorkflowScopeId,
                new Dictionary<string, ValueEnvelope>
                {
                    ["greeting"] = ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("initial"), ValueProtectionPolicy.InstanceInline)
                })
        };

    private static ActivityExecutionState ActivityState(
        string invocationId,
        string nodeId,
        ActivityExecutionStatus status,
        string? schedulingActivityExecutionId = null) =>
        new(
            new ActivityExecution(invocationId, WorkflowExecutionId, nodeId, nodeId, $"test/{nodeId}", "1.0.0"),
            status,
            null,
            Now.AddSeconds(-1),
            null,
            null,
            schedulingActivityExecutionId,
            null,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());
}
