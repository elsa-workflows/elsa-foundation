using System.Text.Json;
using System.Reflection;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeContractTests
{
    [Fact]
    public void WorkflowExecutionState_PinsExactExecutableArtifactSnapshot()
    {
        var executableIdentity = new WorkflowExecutableIdentity(
            ArtifactId: "artifact-42",
            DefinitionId: "orders",
            DefinitionVersionId: "version-7",
            ArtifactVersion: "7.0.0",
            ArtifactHash: "sha256:abc",
            Source: new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", "version-7", "7.0.0"));

        var state = new WorkflowExecutionState(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: executableIdentity,
            Status: WorkflowExecutionStatus.Pending,
            SubStatus: null,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: null,
            UpdatedAt: null,
            CompletedAt: null,
            CorrelationId: "order-123",
            ParentWorkflowExecutionId: null,
            TenantId: "tenant-a",
            SystemMetadata: new Dictionary<string, string> { ["host"] = "unit-test" });

        Assert.Equal("artifact-42", state.PinnedExecutable.ArtifactId);
        Assert.Equal("7.0.0", state.PinnedExecutable.ArtifactVersion);
        Assert.Equal("sha256:abc", state.PinnedExecutable.ArtifactHash);
        Assert.Equal("version-7", state.PinnedExecutable.DefinitionVersionId);
    }

    [Fact]
    public void ExecutableNode_SeparatesRuntimeNodeIdentityFromAuthoredActivityIdentity()
    {
        var node = new ExecutableNode(
            executableNodeId: "node-runtime-1",
            authoredActivityId: "activity-authored-1",
            activityType: "Elsa.SendEmail",
            activityTypeVersion: "1.0.0",
            descriptorType: "Elsa.Activities.SendEmailDescriptor",
            descriptorPayload: Json("{}"),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["to"] = new(
                    inputName: "to",
                    source: RuntimeInputBindingSource.DurableValue,
                    durableValue: new RuntimeDurableValueReference("customerEmail"))
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        Assert.Equal("node-runtime-1", node.ExecutableNodeId);
        Assert.Equal("activity-authored-1", node.AuthoredActivityId);
        Assert.NotEqual(node.ExecutableNodeId, node.AuthoredActivityId);
    }

    [Fact]
    public void ActivityExecution_IdentifiesConcreteRunsOfTheSameExecutableNode()
    {
        var first = new ActivityExecution(
            ActivityExecutionId: "actexec-1",
            WorkflowExecutionId: "wfexec-1",
            ExecutableNodeId: "node-runtime-1",
            AuthoredActivityId: "activity-authored-1",
            ActivityType: "Elsa.SendEmail",
            ActivityTypeVersion: "1.0.0");
        var second = first with { ActivityExecutionId = "actexec-2" };

        Assert.Equal(first.ExecutableNodeId, second.ExecutableNodeId);
        Assert.Equal(first.AuthoredActivityId, second.AuthoredActivityId);
        Assert.NotEqual(first.ActivityExecutionId, second.ActivityExecutionId);
    }

    [Fact]
    public void SchedulerState_ReferencesExecutableNodesAndActivityExecutions()
    {
        var scheduler = new SchedulerState(
            WorkflowExecutionId: "wfexec-1",
            Version: 3,
            PendingWork:
            [
                new ScheduledActivityWorkItem(
                    WorkItemId: "work-1",
                    WorkflowExecutionId: "wfexec-1",
                    ExecutableNodeId: "node-runtime-1",
                    ActivityExecutionId: "actexec-1",
                    SchedulingActivityExecutionId: "actexec-root",
                    BranchId: "branch-a",
                    IterationId: "iteration-4",
                    EnqueuedAt: DateTimeOffset.UtcNow,
                    Reason: "ActivityScheduled")
            ],
            VolatileWaits:
            [
                new VolatileWaitRegistration(
                    WaitId: "wait-1",
                    WorkflowExecutionId: "wfexec-1",
                    ActivityExecutionId: "actexec-1",
                    BranchId: "branch-a",
                    RegisteredAt: DateTimeOffset.UtcNow,
                    ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(5),
                    AwaitableKind: "timer")
            ]);

        var workItem = Assert.Single(scheduler.PendingWork);
        Assert.Equal("node-runtime-1", workItem.ExecutableNodeId);
        Assert.Equal("actexec-1", workItem.ActivityExecutionId);
        Assert.DoesNotContain("authored", workItem.ExecutableNodeId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DurableValueState_UsesDeclaredLifecycleAndStorageBoundary()
    {
        var state = new DurableValueState(
            durableValueId: "durable-1",
            workflowExecutionId: "wfexec-1",
            valueId: "customer",
            type: new RuntimeValueTypeDescriptor("reference", "crm.customer", Json("""{"type":"object"}""")),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: Json("""{"id":"cust-123"}"""),
            externalReference: null,
            sourceActivityExecutionId: "actexec-fetch-customer",
            capturedAt: DateTimeOffset.UtcNow,
            metadata: new Dictionary<string, string>());

        Assert.Equal(DurableValueLifecycle.Instance, state.Lifecycle);
        Assert.Equal(DurableValueStorage.Inline, state.Storage);
        Assert.Equal("actexec-fetch-customer", state.SourceActivityExecutionId);
        Assert.Equal("cust-123", state.InlineValue!.Value.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(DurableValueLifecycle.None, DurableValueStorage.Inline)]
    [InlineData(DurableValueLifecycle.Instance, DurableValueStorage.None)]
    public void DurableValueState_RejectsInvalidLifecycleAndStorageCombinations(
        DurableValueLifecycle lifecycle,
        DurableValueStorage storage)
    {
        Assert.Throws<ArgumentException>(() => NewDurableValueState(lifecycle, storage, Json("""{"value":1}"""), null));
    }

    [Fact]
    public void DurableValueState_RejectsExternalStorageWithoutReference()
    {
        Assert.Throws<ArgumentException>(() => NewDurableValueState(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            inlineValue: null,
            externalReference: null));
    }

    [Fact]
    public void DurableValueState_RejectsMixedInlineAndExternalStorage()
    {
        var externalReference = new DurableValueExternalReference(
            StorageProfile: "documents",
            Locator: "doc-1",
            Metadata: new Dictionary<string, string>());

        Assert.Throws<ArgumentException>(() => NewDurableValueState(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            Json("""{"value":1}"""),
            externalReference));
    }

    [Fact]
    public async Task DefaultCheckpointPolicy_DoesNotChangeCheckpointSemantics()
    {
        var checkpoint = new RuntimeCheckpoint(
            CheckpointId: "checkpoint-1",
            Name: RuntimeCheckpointNames.WorkflowStarted,
            WorkflowExecutionId: "wfexec-1",
            OccurredAt: DateTimeOffset.UtcNow,
            ActivityExecutionIds: [],
            Metadata: new Dictionary<string, string>());
        var policy = new ImmediateRuntimeCheckpointPersistencePolicy();

        var decision = await policy.DecideAsync(checkpoint);

        Assert.Contains(RuntimeCheckpointNames.WorkflowStarted, RuntimeCheckpointNames.All);
        Assert.Contains(RuntimeCheckpointNames.PostCommitIntentRecorded, RuntimeCheckpointNames.All);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, decision.Mode);
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, checkpoint.Name);
    }

    [Fact]
    public void CheckpointNameCollection_ContainsEveryDeclaredCheckpointConstant()
    {
        var declaredNames = typeof(RuntimeCheckpointNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(declaredNames.Order(StringComparer.Ordinal), RuntimeCheckpointNames.All);
    }

    [Fact]
    public void WorkflowExecutionCommand_CarriesStructuredPayload()
    {
        var payload = Json("""{"bookmarkId":"bookmark-1","input":{"status":"approved"}}""");
        var command = new WorkflowExecutionCommand(
            CommandId: "command-1",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.ResumeBookmark,
            EnqueuedAt: DateTimeOffset.UtcNow,
            Payload: payload,
            Metadata: new Dictionary<string, string>());

        Assert.Equal("bookmark-1", command.Payload!.Value.GetProperty("bookmarkId").GetString());
        Assert.Equal("approved", command.Payload.Value.GetProperty("input").GetProperty("status").GetString());
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static DurableValueState NewDurableValueState(
        DurableValueLifecycle lifecycle,
        DurableValueStorage storage,
        JsonElement? inlineValue,
        DurableValueExternalReference? externalReference) =>
        new(
            durableValueId: "durable-1",
            workflowExecutionId: "wfexec-1",
            valueId: "customer",
            type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
            lifecycle: lifecycle,
            storage: storage,
            inlineValue: inlineValue,
            externalReference: externalReference,
            sourceActivityExecutionId: null,
            capturedAt: DateTimeOffset.UtcNow,
            metadata: new Dictionary<string, string>());
}
