using Elsa.Activities.Primitives.Exceptions;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Sets the workflow instance name from workflow data. Ported from elsa-core's <c>SetName</c> activity and
/// adapted to this repo's runtime model: the activity is a CLR leaf (resolved by the existing
/// <c>ClrActivityConstructor</c>, like <see cref="Correlate"/>) and records the new instance name through
/// <see cref="IRuntimeActivityExecutionContext.SetInstanceName"/>. The runtime engine drains that request on
/// the leaf execution path and folds the value into the activity-completed checkpoint's workflow-execution
/// state change (under the instance-name system-metadata key), so the name is durably persisted on the
/// workflow instance — exactly as <see cref="Correlate"/> does for the correlation id.
/// </summary>
/// <remarks>
/// A null or blank <see cref="Name"/> clears the instance name. Like the other control leaves
/// (<see cref="Correlate"/>), <c>SetName</c> requires the Elsa runtime activity execution context.
/// </remarks>
public sealed class SetName : CodeActivity
{
    /// <summary>The name to assign to the workflow instance.</summary>
    public InputArgument<string>? Name { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var name = context.Get(Name);

        runtimeContext.SetInstanceName(name);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new SetNameActivityException("SetName requires an Elsa runtime activity execution context.");
    }
}
