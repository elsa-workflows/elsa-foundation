using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Activities;

/// <summary>Dispatches an exactly pinned Published workflow executable as a child execution.</summary>
public sealed class DispatchWorkflow : CodeActivity
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    public const string ActivityType = DispatchWorkflowConstants.ActivityType;

    public DispatchWorkflow() : base(ActivityType)
    {
    }

    [ActivityInput(
        Order = 0,
        UIHint = ActivityInputUIHints.Dropdown,
        OptionsProvider = DispatchWorkflowConstants.WorkflowDefinitionOptionsKey)]
    public InputArgument<string> WorkflowDefinitionId { get; set; } = null!;

    [ActivityInput(Order = 10)]
    public InputArgument<IReadOnlyDictionary<string, object?>>? Inputs { get; set; }

    [ActivityInput(Order = 20, DefaultValue = "false")]
    public InputArgument<bool>? WaitForCompletion { get; set; }

    [ActivityInput(Order = 30, DefaultValue = "true")]
    public InputArgument<bool>? CancelChildOnParentCancellation { get; set; }

    [ActivityInput(Order = 40)]
    public InputArgument<string>? CorrelationId { get; set; }

    public OutputArgument<string>? ChildWorkflowExecutionId { get; set; }

    public OutputArgument<DispatchWorkflowResult>? Result { get; set; }

    protected override async ValueTask ExecuteAsync(IActivityExecutionContext context)
    {
        if (context.Get(WaitForCompletion))
            throw new NotSupportedException("WaitForCompletion=true is reserved for DispatchWorkflow lifecycle slice #679.");

        if (context is not IRuntimeActivityExecutionContext runtimeContext ||
            context is not IWorkflowDispatchStagingContext dispatchStagingContext)
        {
            throw new InvalidOperationException(
                "DispatchWorkflow requires a Foundation runtime activity execution context with workflow-dispatch staging support.");
        }

        var definitionId = context.Get(WorkflowDefinitionId);
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new InvalidOperationException("DispatchWorkflow requires a nonblank WorkflowDefinitionId.");

        if (!runtimeContext.ExecutableNode.Metadata.TryGetValue(DispatchWorkflowConstants.PinnedTargetMetadataKey, out var serializedPin))
            throw new InvalidOperationException("DispatchWorkflow requires an exact child executable pin created during parent publication.");

        var pin = JsonSerializer.Deserialize<DispatchWorkflowPin>(serializedPin, SerializerOptions)
            ?? throw new InvalidOperationException("DispatchWorkflow child executable pin is invalid.");
        ValidatePin(pin, definitionId.Trim());

        var stateStore = context.GetRequiredService<IWorkflowExecutionStateStore>();
        var parent = await stateStore.FindAsync(runtimeContext.WorkflowExecutionId, context.CancellationToken)
            ?? throw new InvalidOperationException($"DispatchWorkflow parent execution '{runtimeContext.WorkflowExecutionId}' was not found.");
        var partition = parent.Partition
            ?? throw new InvalidOperationException("DispatchWorkflow requires the parent execution's durable partition snapshot.");
        var parentAuthority = parent.Authority
            ?? throw new InvalidOperationException("DispatchWorkflow requires the parent execution's durable authority snapshot.");
        var childAuthority = new WorkflowExecutionAuthoritySnapshot(
            runtimeContext.WorkflowExecutionId,
            parentAuthority.RootInitiator,
            parentAuthority.Metadata);
        var correlationOverride = context.Get(CorrelationId);
        var correlationId = string.IsNullOrWhiteSpace(correlationOverride)
            ? parent.CorrelationId
            : correlationOverride.Trim();
        var inputs = context.Get(Inputs) ?? new Dictionary<string, object?>();
        ValidateInputs(inputs);
        var jsonInputs = WorkflowExecutionStartCommandPayload.ToJsonValues(inputs);
        var identity = new WorkflowDispatchIdentity(runtimeContext.WorkflowExecutionId, runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId);
        var now = context.GetRequiredService<TimeProvider>().GetUtcNow();
        var provenance = pin.Source;
        var record = new WorkflowDispatchRecord(
            dispatchId: identity.DispatchId,
            parentWorkflowExecutionId: runtimeContext.WorkflowExecutionId,
            parentActivityExecutionId: runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            childWorkflowExecutionId: identity.ChildWorkflowExecutionId,
            childExecutable: pin.Executable,
            childSource: provenance,
            mode: WorkflowDispatchMode.FireAndForget,
            status: WorkflowDispatchStatus.Pending,
            correlationId: correlationId,
            tenantId: parent.TenantId,
            partition: partition,
            runKind: parent.RunKind,
            authority: childAuthority,
            inputDescriptors: inputs.Select(item => new WorkflowDispatchInputDescriptor(item.Key, DescribeType(item.Value))).ToArray(),
            createdAt: now,
            updatedAt: now,
            metadata: new Dictionary<string, string>
            {
                ["runtime.sourceReferenceId"] = pin.Source.SourceReferenceId
            });
        var startPayload = new WorkflowDispatchStartPayload(
            identity.DispatchId,
            runtimeContext.WorkflowExecutionId,
            runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            pin.Executable,
            provenance,
            jsonInputs,
            correlationId,
            parent.TenantId,
            partition,
            parent.RunKind,
            childAuthority);
        var startIntent = new RuntimePostCommitIntent(
            intentId: identity.StartIntentId,
            workflowExecutionId: runtimeContext.WorkflowExecutionId,
            kind: DispatchWorkflowConstants.StartChildIntentKind,
            recordedAt: now,
            activityExecutionId: runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            idempotencyKey: identity.StartIdempotencyKey,
            payload: JsonSerializer.SerializeToElement(startPayload, SerializerOptions),
            metadata: new Dictionary<string, string> { ["runtime.dispatchId"] = identity.DispatchId });

        dispatchStagingContext.StageWorkflowDispatch(new WorkflowDispatchCheckpointRequest(record, startIntent));
        context.Set(ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId, nameof(ChildWorkflowExecutionId));
        context.SetOutcomes([DispatchWorkflowOutcomes.Dispatched]);
    }

    private static void ValidatePin(DispatchWorkflowPin pin, string definitionId)
    {
        ArgumentNullException.ThrowIfNull(pin.Executable);
        ArgumentNullException.ThrowIfNull(pin.Source);
        if (!StringComparer.Ordinal.Equals(pin.Source.DefinitionId, definitionId))
            throw new InvalidOperationException("DispatchWorkflow child executable pin does not match the authored workflow definition.");
    }

    private static void ValidateInputs(IReadOnlyDictionary<string, object?> inputs)
    {
        if (inputs.Keys.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("DispatchWorkflow input names cannot be blank.");
    }

    private static string DescribeType(object? value) => value switch
    {
        null => "null",
        JsonElement json => $"json:{json.ValueKind}",
        _ => value.GetType().FullName ?? value.GetType().Name
    };
}
