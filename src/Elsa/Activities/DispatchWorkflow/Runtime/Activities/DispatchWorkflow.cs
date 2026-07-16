using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

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

        var executableStore = context.GetRequiredService<IWorkflowExecutableStore>();
        var childExecutable = await executableStore.FindAsync(pin.Executable.ArtifactId, context.CancellationToken)
            ?? throw new InvalidOperationException("DispatchWorkflow pinned child executable is no longer available.");
        if (!StringComparer.Ordinal.Equals(childExecutable.Identity.ArtifactHash, pin.Executable.ArtifactHash))
            throw new InvalidOperationException("DispatchWorkflow pinned child executable identity is inconsistent.");
        var inputContract = childExecutable.InputContract;
        if (inputContract is null || inputContract.Version != WorkflowExecutableInputContract.CurrentVersion)
            throw new InvalidOperationException("DispatchWorkflow pinned child executable does not carry a supported input contract.");

        var stateStore = context.GetRequiredService<IWorkflowExecutionStateStore>();
        var parent = await stateStore.FindAsync(runtimeContext.WorkflowExecutionId, context.CancellationToken)
            ?? throw new InvalidOperationException($"DispatchWorkflow parent execution '{runtimeContext.WorkflowExecutionId}' was not found.");
        var maxNestingDepth = context.GetRequiredService<IOptions<DispatchWorkflowOptions>>().Value.MaxNestingDepth;
        DispatchWorkflowOptions.ValidateMaxNestingDepth(maxNestingDepth, nameof(DispatchWorkflowOptions.MaxNestingDepth));
        int childNestingDepth;
        try
        {
            childNestingDepth = checked(parent.DispatchNestingDepth + 1);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("DispatchWorkflow nesting depth is invalid.", exception);
        }
        if (childNestingDepth > maxNestingDepth)
        {
            throw new InvalidOperationException(
                $"DispatchWorkflow child nesting depth {childNestingDepth} exceeds the configured maximum of {maxNestingDepth}.");
        }
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
        var inputValidation = context.GetRequiredService<IWorkflowExecutableInputValidator>().Validate(
            inputContract,
            WorkflowExecutionStartCommandPayload.ToJsonValues(inputs));
        if (!inputValidation.IsValid)
        {
            throw new InvalidOperationException(
                "DispatchWorkflow inputs are invalid: " +
                string.Join(" ", inputValidation.Findings.Select(finding => finding.Message)));
        }
        var jsonInputs = inputValidation.NormalizedInputs;
        var identity = new WorkflowDispatchIdentity(runtimeContext.WorkflowExecutionId, runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId);
        var waitForCompletion = context.Get(WaitForCompletion);
        // Wait mode needs the durable invoke event time for replay stability. Fire-and-forget retains its
        // established wall-clock timestamp contract.
        var now = waitForCompletion
            ? runtimeContext.SchedulerWorkItem.RecordedAt
            : context.GetRequiredService<TimeProvider>().GetUtcNow();
        var provenance = pin.Source
            ?? throw new InvalidOperationException("DispatchWorkflow child executable pin does not carry source provenance.");
        var dispatchNodeId = runtimeContext.ExecutableNode.ExecutableNodeId;
        var parentExecutable = await executableStore.FindAsync(parent.PinnedExecutable.ArtifactId, context.CancellationToken);
        var hasRetainedEdge = parentExecutable is not null &&
            StringComparer.Ordinal.Equals(parentExecutable.Identity.ArtifactHash, parent.PinnedExecutable.ArtifactHash) &&
            parentExecutable.Dependencies.Any(dependency =>
                StringComparer.Ordinal.Equals(dependency.ArtifactId, pin.Executable.ArtifactId) &&
                StringComparer.Ordinal.Equals(dependency.ArtifactHash, pin.Executable.ArtifactHash) &&
                dependency.DispatchNodeIds.Contains(dispatchNodeId, StringComparer.Ordinal));
        var dispatchMode = waitForCompletion
            ? WorkflowDispatchMode.WaitForCompletion
            : WorkflowDispatchMode.FireAndForget;
        var dispatchMetadata = new Dictionary<string, string>
        {
            ["runtime.sourceReferenceId"] = provenance.SourceReferenceId
        };
        var cancelChildOnParentCancellation = CancelChildOnParentCancellation is null ||
                                              context.Get(CancelChildOnParentCancellation);
        WorkflowDispatchLifecycle.SetEffectiveCancellationPolicy(
            dispatchMetadata,
            dispatchMode,
            cancelChildOnParentCancellation);
        var record = new WorkflowDispatchRecord(
            dispatchId: identity.DispatchId,
            parentWorkflowExecutionId: runtimeContext.WorkflowExecutionId,
            parentActivityExecutionId: runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            childWorkflowExecutionId: identity.ChildWorkflowExecutionId,
            childExecutable: pin.Executable,
            childSource: provenance,
            mode: dispatchMode,
            status: WorkflowDispatchStatus.Pending,
            correlationId: correlationId,
            tenantId: parent.TenantId,
            partition: partition,
            runKind: parent.RunKind,
            authority: childAuthority,
            inputDescriptors: jsonInputs.Select(item => new WorkflowDispatchInputDescriptor(item.Key, DescribeType(item.Value))).ToArray(),
            createdAt: now,
            updatedAt: now,
            metadata: dispatchMetadata,
            dispatchNestingDepth: childNestingDepth,
            testScope: parent.TestScope);
        var startPayload = new WorkflowDispatchStartPayload(
            identity.DispatchId,
            runtimeContext.WorkflowExecutionId,
            runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            pin.Executable,
            hasRetainedEdge ? null : provenance,
            jsonInputs,
            correlationId,
            parent.TenantId,
            partition,
            parent.RunKind,
            childAuthority,
            hasRetainedEdge ? parent.PinnedExecutable : null,
            hasRetainedEdge ? dispatchNodeId : null,
            childNestingDepth,
            parent.TestScope);
        var startIntent = new RuntimePostCommitIntent(
            intentId: identity.StartIntentId,
            workflowExecutionId: runtimeContext.WorkflowExecutionId,
            kind: DispatchWorkflowConstants.StartChildIntentKind,
            recordedAt: now,
            activityExecutionId: runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            idempotencyKey: identity.StartIdempotencyKey,
            payload: JsonSerializer.SerializeToElement(startPayload, SerializerOptions),
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = identity.DispatchId });

        var waitBookmark = waitForCompletion
            ? new ActivityBookmarkRequest(
                bookmarkId: identity.WaitBookmarkId,
                resumeTargetId: DispatchWorkflowConstants.CompletionResumeTargetId,
                stimulusType: DispatchWorkflowConstants.WaitStimulusType,
                stimulusHash: identity.WaitStimulusHash,
                expiresAt: null,
                metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = identity.DispatchId })
            : null;

        dispatchStagingContext.StageWorkflowDispatch(waitBookmark is null
            ? new WorkflowDispatchCheckpointRequest(record, startIntent)
            : new WorkflowDispatchCheckpointRequest(
                record,
                startIntent,
                waitBookmark,
                DispatchWorkflowConstants.CompletionResumeTargetId,
                DispatchWorkflowConstants.WaitStimulusType));
        context.Set(ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId, nameof(ChildWorkflowExecutionId));
        if (!waitForCompletion)
            context.SetOutcomes([DispatchWorkflowOutcomes.Dispatched]);
    }

    [ResumeTarget(DispatchWorkflowConstants.CompletionResumeTargetId)]
    private void OnChildCompletedAsync(IActivityExecutionContext context)
    {
        if (context is not IRuntimeActivityExecutionContext runtimeContext ||
            context is not IExecutionExpressionState { ResumeInput: { } resumeInput })
        {
            throw new InvalidOperationException("DispatchWorkflow completion resume requires the runtime execution identity and resume input.");
        }

        var payload = resumeInput.Deserialize<WorkflowDispatchParentResumePayload>(SerializerOptions)
            ?? throw new InvalidOperationException("DispatchWorkflow completion resume payload is invalid.");
        var parentActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var identity = new WorkflowDispatchIdentity(runtimeContext.WorkflowExecutionId, parentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(payload.ParentWorkflowExecutionId, runtimeContext.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(payload.ParentActivityExecutionId, parentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(payload.DispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(payload.ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(payload.BookmarkId, identity.WaitBookmarkId) ||
            !StringComparer.Ordinal.Equals(payload.StimulusType, DispatchWorkflowConstants.WaitStimulusType) ||
            !StringComparer.Ordinal.Equals(payload.StimulusHash, identity.WaitStimulusHash) ||
            !DispatchWorkflowResult.SupportsParentResume(payload.Result.Status))
        {
            throw new InvalidOperationException("DispatchWorkflow completion resume payload does not match the current activity execution.");
        }

        context.Set(ChildWorkflowExecutionId, payload.ChildWorkflowExecutionId, nameof(ChildWorkflowExecutionId));
        context.Set(Result, payload.Result, nameof(Result));
        context.SetOutcomes([payload.Result.Status switch
        {
            WorkflowDispatchStatus.Completed => DispatchWorkflowOutcomes.Completed,
            WorkflowDispatchStatus.Faulted => DispatchWorkflowOutcomes.Faulted,
            WorkflowDispatchStatus.Cancelled => DispatchWorkflowOutcomes.Cancelled,
            WorkflowDispatchStatus.DispatchFailed => DispatchWorkflowOutcomes.DispatchFailed,
            _ => throw new InvalidOperationException("DispatchWorkflow completion resume payload has an unsupported child terminal status.")
        }]);
    }

    private static void ValidatePin(DispatchWorkflowPin pin, string definitionId)
    {
        ArgumentNullException.ThrowIfNull(pin.Executable);
        ArgumentNullException.ThrowIfNull(pin.Source);
        if (!StringComparer.Ordinal.Equals(pin.Source.DefinitionId, definitionId))
            throw new InvalidOperationException("DispatchWorkflow child executable pin does not match the authored workflow definition.");
    }

    private static string DescribeType(object? value) => value switch
    {
        null => "null",
        JsonElement json => $"json:{json.ValueKind}",
        _ => value.GetType().FullName ?? value.GetType().Name
    };
}
