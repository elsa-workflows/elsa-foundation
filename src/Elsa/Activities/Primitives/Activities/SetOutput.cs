using Elsa.Activities.Primitives.Exceptions;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Sets a named workflow output value. Ported from elsa-core's <c>SetOutput</c> activity and adapted to this
/// repo's runtime model: the activity is a CLR leaf (resolved by the existing <c>ClrActivityConstructor</c>,
/// like <see cref="Correlate"/>) and records the output through
/// <see cref="IRuntimeActivityExecutionContext.SetWorkflowOutput"/>. The runtime engine drains that request on
/// the leaf execution path and folds the value into the activity-completed checkpoint as an
/// <c>OutputName</c>-tagged durable value — the same durable/output channel activity outputs use — so the
/// workflow output is durably persisted on the instance, mirroring how <see cref="Correlate"/> folds the
/// correlation id into the same checkpoint.
/// </summary>
/// <remarks>
/// <para>
/// Scope (per #260): <c>SetOutput</c> sets a named workflow <em>output value</em> into the durable/output
/// channel. The value is captured as an <c>Instance</c>-lifecycle inline durable value tagged with the output
/// name, the same shape an activity output capture takes, so it is queryable after the run completes.
/// </para>
/// <para>
/// A blank <see cref="OutputName"/> is a no-op. Like the other control leaves (<see cref="Correlate"/>),
/// <c>SetOutput</c> requires the Elsa runtime activity execution context.
/// </para>
/// </remarks>
public sealed class SetOutput : CodeActivity
{
    /// <summary>The authored name of the workflow output to assign.</summary>
    public InputArgument<string>? OutputName { get; set; }

    /// <summary>The value to assign to the workflow output.</summary>
    public InputArgument<object>? OutputValue { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var outputName = context.Get(OutputName);
        if (string.IsNullOrWhiteSpace(outputName))
            return;

        var value = context.Get(OutputValue);
        runtimeContext.SetWorkflowOutput(outputName, value);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new SetOutputActivityException("SetOutput requires an Elsa runtime activity execution context.");
    }
}
