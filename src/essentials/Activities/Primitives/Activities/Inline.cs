using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Runs a small inline expression/code step and surfaces its value as the activity result. The inline
/// step is authored as an expression binding on <see cref="Expression"/> (Literal, JavaScript, Liquid,
/// …); the runtime evaluates it through the existing activity-input expression path — the same path that
/// feeds <see cref="WriteLine"/>'s <c>Text</c> — so no new expression engine is introduced. The
/// evaluated value is hydrated onto the ordinary <see cref="Expression"/> property and returned as one
/// atomic typed result for downstream capture/binding.
/// </summary>
/// <remarks>
/// Ported from elsa-core's <c>Inline</c>/<c>RunInlineActivity</c> concept and adapted to this repo's
/// model: rather than carrying a host-side delegate, the inline step rides the canonical portable
/// expression input-binding pipeline. The runtime evaluates the binding before activation, pins the
/// materialized value in the invocation snapshot, and hydrates this transient activity exactly once.
/// The result type is <see cref="object"/> so any expression-produced value flows through unchanged.
/// </remarks>
public sealed class Inline : Activity<object?>
{
    /// <summary>The already-materialized inline expression/code value.</summary>
    [ActivityInput(Key = nameof(Expression))]
    public object? Expression { get; set; }

    protected override ValueTask<ActivityTransition<object?>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(Expression));
}
