using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
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
            ArtifactHash: "sha256:abc");

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
    public void WorkflowExecutionState_DeserializesLegacyHistoryWithoutClassificationOrSourceProvenance()
    {
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: "wfexec-legacy",
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: WorkflowExecutionStatus.Completed,
            SubStatus: null,
            CreatedAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: DateTimeOffset.UnixEpoch,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());
        var legacyJson = JsonSerializer.SerializeToNode(state)!.AsObject();
        Assert.True(legacyJson.Remove(nameof(WorkflowExecutionState.RunKind)));
        Assert.True(legacyJson.Remove(nameof(WorkflowExecutionState.PinnedSource)));

        var restored = legacyJson.Deserialize<WorkflowExecutionState>()!;

        Assert.Equal(WorkflowRunKind.Unknown, restored.RunKind);
        Assert.Null(restored.PinnedSource);
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
                    inputKey: "to",
                    targetType: new ValueTypeDescriptor("String"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.WorkflowRequest,
                    workflowRequest: new RuntimeWorkflowRequestReference("customerEmail"))
            },
            metadata: new Dictionary<string, string>());

        Assert.Equal("node-runtime-1", node.ExecutableNodeId);
        Assert.Equal("activity-authored-1", node.AuthoredActivityId);
        Assert.NotEqual(node.ExecutableNodeId, node.AuthoredActivityId);
    }

    [Fact]
    public void ExecutableNode_has_no_generic_Composition_property()
    {
        var members = typeof(ExecutableNode)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.DoesNotContain("Composition", members);
    }

    [Fact]
    public void ExecutableNode_exposes_activity_owned_structure()
    {
        var property = typeof(ExecutableNode)
            .GetProperty(nameof(ExecutableNode.Structure), BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.Equal(typeof(ExecutableActivityStructure), property!.PropertyType);
    }

    [Fact]
    public void WorkflowsRuntimeCore_defines_no_activity_specific_child_slot_name_or_metadata_catalog()
    {
        var assembly = typeof(ExecutableNode).Assembly;

        Assert.Null(assembly.GetType("Elsa.Workflows.Runtime.Core.Models.ExecutableChildSlotNames"));
        Assert.Null(assembly.GetType("Elsa.Workflows.Runtime.Core.Models.ExecutableChildSlotMetadataKeys"));
        Assert.Null(assembly.GetType("Elsa.Workflows.Runtime.Core.Models.ExecutableEdge"));
    }

    [Fact]
    public void ExecutableChildSlot_is_projection_only()
    {
        var members = typeof(ExecutableChildSlot)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.Contains(nameof(ExecutableChildSlot.Name), members);
        Assert.Contains(nameof(ExecutableChildSlot.Activities), members);
        Assert.DoesNotContain("Metadata", members);
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
            workflowExecutionId: "wfexec-1",
            version: 3,
            pendingWork:
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
            pendingContinuations: [],
            volatileWaits:
            [
                new VolatileWaitRegistration(
                    waitId: "wait-1",
                    workflowExecutionId: "wfexec-1",
                    activityExecutionId: "actexec-1",
                    branchId: "branch-a",
                    registeredAt: DateTimeOffset.UtcNow,
                    expiresAt: DateTimeOffset.UtcNow.AddSeconds(5),
                    awaitableKind: "timer",
                    status: VolatileWaitStatus.Registered,
                    hostShutdownBehavior: VolatileWaitHostShutdownBehavior.CancelWait,
                    cancellationBehavior: VolatileWaitCancellationBehavior.CancelWait)
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

    [Fact]
    public void DurableValueContracts_ReplaceLegacyRuntimeStorageDrivers()
    {
        var runtimeCoreAssembly = typeof(IDurableValueStateStore).Assembly;

        Assert.Null(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IStorageDriver"));
        Assert.Null(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IStorageDriverContext"));
        Assert.NotNull(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IDurableValueStateStore"));
        Assert.NotNull(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Models.DurableValueState"));
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
