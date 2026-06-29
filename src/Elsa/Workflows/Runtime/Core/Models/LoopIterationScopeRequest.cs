namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The per-iteration variable state a loop activity (<c>ForEach</c>/<c>For</c>/<c>While</c>/<c>Do</c>,
/// #264–#267) declares for one pass of its body (#259). One request describes the values the loop
/// owner publishes for a single iteration: the engine-assigned <see cref="IterationId"/> that
/// correlates the pass, the current item, and the zero-based index.
/// </summary>
/// <remarks>
/// The reference keys are stable per loop owner — every iteration of the same loop reuses the same
/// <see cref="ItemReferenceKey"/>/<see cref="IndexReferenceKey"/>. Passes are kept apart by the factory
/// building a distinct scope instance (with its own value store) per call, <b>not</b> by the
/// <see cref="IterationId"/>: the iteration id is recorded as the produced scope's <c>ExecutionId</c>
/// for correlation/inspection and provenance routing only, and scope value resolution never reads it.
/// Supply a distinct <see cref="IterationId"/> per pass so the body execution's iteration identity is
/// meaningful, but do not rely on it for isolation.
/// </remarks>
/// <param name="OwnerNodeId">The loop activity's executable node id; becomes the produced scope's declaring <c>ScopeId</c>.</param>
/// <param name="IterationId">The engine iteration identity for this pass (the value the loop owner threads through <c>ActivitySchedulingProvenance.IterationId</c>). Distinct per pass; recorded as the scope's <c>ExecutionId</c> for correlation, not used as the isolation key.</param>
/// <param name="ItemReferenceKey">Reference key of the current-item variable. Stable across iterations of the same loop.</param>
/// <param name="ItemName">Bare name of the current-item variable (e.g. <c>currentItem</c>), used by name-based access.</param>
/// <param name="Item">The current item value for this pass.</param>
/// <param name="Index">The zero-based iteration index for this pass.</param>
/// <param name="IndexReferenceKey">Optional reference key of the iteration-index variable. When null, no index variable is declared.</param>
/// <param name="IndexName">Bare name of the iteration-index variable (e.g. <c>index</c>). Required when <see cref="IndexReferenceKey"/> is set.</param>
public sealed record LoopIterationScopeRequest(
    string OwnerNodeId,
    string IterationId,
    string ItemReferenceKey,
    string ItemName,
    object? Item,
    int Index,
    string? IndexReferenceKey = null,
    string? IndexName = null);
