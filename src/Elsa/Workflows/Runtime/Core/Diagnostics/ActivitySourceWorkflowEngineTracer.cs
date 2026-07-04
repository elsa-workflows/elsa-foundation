using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Diagnostics;

/// <summary>
/// The <see cref="ActivitySource"/>-backed <see cref="IWorkflowEngineTracer"/> registered by
/// <c>WorkflowsRuntimeTracingFeature</c>. It emits engine-phase spans on the <see cref="WorkflowEngineTelemetry.ActivitySourceName"/>
/// source, which a host collects by attaching a listener (an OpenTelemetry SDK <c>AddSource("Elsa.Workflows.Runtime")</c>
/// or a bare <see cref="ActivityListener"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zero cost until a listener attaches.</b> <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns
/// <c>null</c> when no listener is sampling the source, so each <c>Start*</c> call is a single cheap check and returns
/// <c>null</c> before any attribute (tag) is built. Attributes are only set once a non-null <see cref="Activity"/> is in
/// hand. Values are restricted to identifiers, kinds, and counts — never workflow variables, secrets, or fencing tokens.
/// </para>
/// </remarks>
public sealed class ActivitySourceWorkflowEngineTracer : IWorkflowEngineTracer, IDisposable
{
    private readonly ActivitySource _activitySource;
    private readonly bool _ownsActivitySource;

    /// <summary>Creates a tracer owning a fresh <see cref="ActivitySource"/> named <see cref="WorkflowEngineTelemetry.ActivitySourceName"/>.</summary>
    public ActivitySourceWorkflowEngineTracer()
        : this(new ActivitySource(WorkflowEngineTelemetry.ActivitySourceName, GetVersion()), ownsActivitySource: true)
    {
    }

    /// <summary>Creates a tracer over a caller-provided <see cref="ActivitySource"/> (the caller keeps ownership).</summary>
    public ActivitySourceWorkflowEngineTracer(ActivitySource activitySource)
        : this(activitySource, ownsActivitySource: false)
    {
    }

    private ActivitySourceWorkflowEngineTracer(ActivitySource activitySource, bool ownsActivitySource)
    {
        ArgumentNullException.ThrowIfNull(activitySource);
        _activitySource = activitySource;
        _ownsActivitySource = ownsActivitySource;
    }

    /// <inheritdoc />
    public Activity? StartDrainCycle(RuntimeSchedulerDrainRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var activity = _activitySource.StartActivity(WorkflowEngineTelemetry.DrainSpanName, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(WorkflowEngineTelemetry.WorkflowExecutionIdTag, request.WorkflowExecutionId);
        if (request.MaxWorkItems is { } maxWorkItems)
            activity.SetTag(WorkflowEngineTelemetry.DrainMaxWorkItemsTag, maxWorkItems);

        return activity;
    }

    /// <inheritdoc />
    public Activity? StartDispatch(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        var activity = _activitySource.StartActivity(WorkflowEngineTelemetry.DispatchSpanName, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(WorkflowEngineTelemetry.WorkflowExecutionIdTag, workItem.WorkflowExecutionId);
        activity.SetTag(WorkflowEngineTelemetry.WorkItemIdTag, workItem.WorkItemId);
        activity.SetTag(WorkflowEngineTelemetry.CommandKindTag, workItem.CommandKind.ToString());

        return activity;
    }

    /// <inheritdoc />
    public Activity? StartActivityExecution(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        var activity = _activitySource.StartActivity(WorkflowEngineTelemetry.ActivityExecuteSpanName, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(WorkflowEngineTelemetry.WorkflowExecutionIdTag, workItem.WorkflowExecutionId);
        activity.SetTag(WorkflowEngineTelemetry.WorkItemIdTag, workItem.WorkItemId);
        activity.SetTag(WorkflowEngineTelemetry.CommandKindTag, workItem.CommandKind.ToString());

        return activity;
    }

    /// <inheritdoc />
    public Activity? StartCheckpointCommit(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var activity = _activitySource.StartActivity(WorkflowEngineTelemetry.CheckpointCommitSpanName, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(WorkflowEngineTelemetry.WorkflowExecutionIdTag, commit.WorkflowExecutionId);
        activity.SetTag(WorkflowEngineTelemetry.CheckpointIdTag, commit.Checkpoint.CheckpointId);

        return activity;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsActivitySource)
            _activitySource.Dispose();
    }

    private static string GetVersion() =>
        typeof(ActivitySourceWorkflowEngineTracer).Assembly.GetName().Version?.ToString() ?? "1.0.0";
}
