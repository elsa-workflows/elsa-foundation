namespace Elsa.Workflows.Runtime.Core.Diagnostics;

/// <summary>
/// Stable naming conventions for the workflow runtime engine's own distributed-tracing spans (MS-9). These are the
/// <b>engine self-instrumentation</b> conventions — the names a listener (an OpenTelemetry SDK <c>AddSource</c> or a
/// bare <see cref="System.Diagnostics.ActivityListener"/>) subscribes to. They are unrelated to the
/// <c>Elsa.Diagnostics.OpenTelemetry</c> ingestion domain, which receives OTLP telemetry pushed by <i>other</i>
/// processes. See <c>docs/reference/engine-telemetry.md</c> for the engine-telemetry vs. ingestion-domain distinction.
/// </summary>
/// <remarks>
/// The names follow OpenTelemetry semantic-convention style (dotted, lowercase, <c>elsa.*</c> namespaced). Treat them
/// as a stable contract: renaming a span or tag is a breaking change for anyone building dashboards or alerts on them.
/// Attribute values are restricted to identifiers, kinds, and counts — never workflow variables, expression results,
/// secrets, or fencing tokens.
/// </remarks>
public static class WorkflowEngineTelemetry
{
    /// <summary>The <see cref="System.Diagnostics.ActivitySource"/> name hosts subscribe to (e.g. OpenTelemetry <c>AddSource</c>).</summary>
    public const string ActivitySourceName = "Elsa.Workflows.Runtime";

    // Span (Activity) names — one per engine phase.

    /// <summary>Span for a single scheduler drain cycle (drains queued work for one workflow execution).</summary>
    public const string DrainSpanName = "elsa.runtime.drain";

    /// <summary>Span for dispatching one drained work item to its scheduler work handler.</summary>
    public const string DispatchSpanName = "elsa.runtime.dispatch";

    /// <summary>Span for running the work item's handler in the activity pipeline's Invoke slot (activity execution).</summary>
    public const string ActivityExecuteSpanName = "elsa.runtime.activity.execute";

    /// <summary>Span for committing a single runtime checkpoint (through the committer's fenced commit path).</summary>
    public const string CheckpointCommitSpanName = "elsa.runtime.checkpoint.commit";

    // Tag (attribute) names.

    /// <summary>The workflow execution id the phase is operating on.</summary>
    public const string WorkflowExecutionIdTag = "elsa.workflow.execution_id";

    /// <summary>The drain cycle's requested work-item cap (absent when unbounded).</summary>
    public const string DrainMaxWorkItemsTag = "elsa.drain.max_work_items";

    /// <summary>The number of work items the drain cycle processed.</summary>
    public const string DrainItemsProcessedTag = "elsa.drain.items_processed";

    /// <summary>Why the drain cycle stopped (e.g. workflow terminated), when a non-default reason applies.</summary>
    public const string DrainStopReasonTag = "elsa.drain.stop_reason";

    /// <summary>The dispatched work item's id.</summary>
    public const string WorkItemIdTag = "elsa.work_item.id";

    /// <summary>The work item's command kind (e.g. StartActivity, CompleteActivity).</summary>
    public const string CommandKindTag = "elsa.command.kind";

    /// <summary>The name of the scheduler work handler selected for the work item.</summary>
    public const string HandlerNameTag = "elsa.handler.name";

    /// <summary>The phase outcome (<c>completed</c> or <c>faulted</c>).</summary>
    public const string OutcomeTag = "elsa.outcome";

    /// <summary>The committed checkpoint's id.</summary>
    public const string CheckpointIdTag = "elsa.checkpoint.id";

    /// <summary>The persistence mode the policy decided for the checkpoint (e.g. Persist, Skip).</summary>
    public const string CheckpointPersistenceModeTag = "elsa.checkpoint.persistence_mode";

    /// <summary>Whether the checkpoint is mandatory (cannot be skipped by the persistence policy).</summary>
    public const string CheckpointMandatoryTag = "elsa.checkpoint.mandatory";

    /// <summary>The number of post-commit intents folded into the checkpoint commit.</summary>
    public const string CheckpointPostCommitIntentsTag = "elsa.checkpoint.post_commit_intents";

    /// <summary>Outcome value: the phase completed successfully.</summary>
    public const string OutcomeCompleted = "completed";

    /// <summary>Outcome value: the phase faulted.</summary>
    public const string OutcomeFaulted = "faulted";
}
