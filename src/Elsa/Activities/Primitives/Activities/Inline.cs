using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Runs a small inline expression/code step and surfaces its value as the activity result. The inline
/// step is authored as an expression binding on <see cref="Expression"/> (Literal, JavaScript, Liquid,
/// …); the runtime evaluates it through the existing activity-input expression path — the same path that
/// feeds <see cref="WriteLine"/>'s <c>Text</c> — so no new expression engine is introduced. The
/// evaluated value is read back via <see cref="IActivityExecutionContext.Get{T}"/> and emitted on
/// <see cref="CodeActivity{T}.Result"/> for downstream capture/binding.
/// </summary>
/// <remarks>
/// Ported from elsa-core's <c>Inline</c>/<c>RunInlineActivity</c> concept and adapted to this repo's
/// model: rather than carrying a host-side delegate, the inline step rides the standard
/// <see cref="InputArgument{T}"/> expression-binding pipeline (evaluated and seeded before
/// <c>Execute</c>), keeping the activity a pure CLR leaf resolved by the existing
/// <c>ClrActivityConstructor</c>. The result type is <see cref="object"/> so any expression-produced
/// value flows through unchanged.
/// </remarks>
public sealed class Inline : CodeActivity<object>
{
    /// <summary>The inline expression/code to evaluate. Bound and evaluated via the existing expression path.</summary>
    public InputArgument<object>? Expression { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var value = context.Get(Expression);
        context.Set(Result, value);
    }
}
