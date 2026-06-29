using Elsa.Activities.Primitives.Exceptions;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Sets the workflow instance correlation id from workflow data. Ported from elsa-core's <c>Correlate</c>
/// activity and adapted to this repo's runtime model: the activity is a CLR leaf (resolved by the existing
/// <c>ClrActivityConstructor</c>, like <see cref="Fault"/>) and records the new correlation id through
/// <see cref="IRuntimeActivityExecutionContext.SetCorrelationId"/>. The runtime engine drains that request on
/// the leaf execution path and folds the value into the activity-completed checkpoint's workflow-execution
/// state change, so the correlation id is durably persisted on the workflow instance.
/// </summary>
/// <remarks>
/// A null or blank <see cref="CorrelationId"/> clears the correlation id. Like the other control leaves
/// (<see cref="Fault"/>), <c>Correlate</c> requires the Elsa runtime activity execution context.
/// </remarks>
public sealed class Correlate : CodeActivity
{
    /// <summary>The correlation id to assign to the workflow instance.</summary>
    public InputArgument<string>? CorrelationId { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var correlationId = context.Get(CorrelationId);

        runtimeContext.SetCorrelationId(correlationId);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new CorrelateActivityException("Correlate requires an Elsa runtime activity execution context.");
    }
}
