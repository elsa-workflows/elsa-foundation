namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Exposes the live, post-seed execution-time workflow state — identity, workflow inputs, workflow
/// variables, and prior activity outputs — that an expression evaluated <em>during</em> activity
/// execution (e.g. a <c>Run JavaScript</c> activity) may reference. The execution-time
/// <see cref="Elsa.Expressions.Core.Contracts.IExpressionExecutionContext"/> implements this so that
/// language-specific pre/post-processors can surface <c>getWorkflowInstanceId()</c>, named accessors
/// (<c>getGreeting()</c>), execution-time output accessors, and JavaScript variable write-back without
/// depending on a DI-registered live workflow execution context (ADR 0030 D1).
/// </summary>
/// <remarks>
/// This is the execution-time counterpart to <see cref="IMaterializationExpressionState"/>, which serves
/// the input-materialization evaluation point. The two narrow markers coexist along the same
/// parameter-threading seam; a general transient-properties carrier is deferred (ADR 0030 Q3). All members
/// are sourced from Runtime-owned state (execution state, durable-value projections, the pinned executable),
/// so the carrier introduces no dependency on <c>Elsa.Workflows.Design.*</c> (constitution §E2.2 / §E2.6).
/// </remarks>
public interface IExecutionExpressionState : Elsa.Expressions.Core.Contracts.IExpressionTenantContext
{
    /// <summary>The runtime workflow execution (instance) id.</summary>
    string WorkflowInstanceId { get; }

    /// <summary>The current correlation id, if any.</summary>
    string? CorrelationId { get; }

    /// <summary>The current workflow instance name, if any.</summary>
    string? WorkflowName { get; }

    /// <summary>The pinned executable's workflow definition id.</summary>
    string WorkflowDefinitionId { get; }

    /// <summary>The pinned executable's workflow definition version id.</summary>
    string WorkflowDefinitionVersionId { get; }

    /// <summary>The pinned executable's workflow definition version number.</summary>
    int WorkflowDefinitionVersion { get; }

    /// <summary>Workflow input values keyed by input name.</summary>
    IReadOnlyDictionary<string, object?> WorkflowInputs { get; }

    /// <summary>
    /// The payload of the stimulus that started this execution, projected from its reserved durable channel
    /// (spec 089 A); a <see cref="System.Text.Json.JsonElement"/> after the durable round-trip. Null when the
    /// execution was not started by a stimulus carrying a payload. Deliberately separate from
    /// <see cref="WorkflowInputs"/>: it cannot collide with author-declared inputs and cannot be supplied
    /// through the execute API's inputs bag.
    /// </summary>
    object? StimulusInput { get; }

    /// <summary>
    /// The executable node id of the trigger that started this execution, projected from its reserved durable
    /// channel (spec 089 D). Null on a direct (non-stimulus) run and on resume-only paths. Lets an activity that
    /// can act as both a start trigger and a mid-flow node (e.g. <c>HttpEndpoint</c>) tell whether it is the node
    /// that triggered this run — completing with the stimulus input in that case, otherwise suspending. Like
    /// <see cref="StimulusInput"/> it rides a dedicated channel and cannot be supplied through the execute API's
    /// inputs bag.
    /// </summary>
    string? TriggerNodeId { get; }

    /// <summary>
    /// The resume stimulus input for the current resume-callback invocation (spec 089 D), a
    /// <see cref="System.Text.Json.JsonElement"/> carrying the payload of the request that resumed the bookmark.
    /// Populated only on the resume path (from the resume dispatch's input); null on start, invoke, and
    /// parent-completion paths. Unlike <see cref="StimulusInput"/> and <see cref="WorkflowInputs"/> it is a live,
    /// per-invocation carrier value (never durable state), so a context-shaped <c>[ResumeTarget]</c> can read the
    /// request payload while keeping full <c>Set</c>/output access to the context.
    /// </summary>
    System.Text.Json.JsonElement? ResumeInput { get; }

    /// <summary>Current workflow variable values keyed by variable name.</summary>
    IReadOnlyDictionary<string, object?> WorkflowVariables { get; }

    /// <summary>Prior activity output values keyed by output name.</summary>
    IReadOnlyDictionary<string, object?> ActivityOutputValues { get; }
}
