using Elsa.Activities.Primitives.Exceptions;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Ends the whole workflow run early with a successful outcome, regardless of any remaining scheduled work.
/// Ported from elsa-core's <c>Finish</c>/<c>Complete</c> activity and adapted to this repo's runtime model:
/// the activity is a CLR leaf (resolved by the existing <c>ClrActivityConstructor</c>, like <see cref="Fault"/>)
/// and signals workflow completion through <see cref="IRuntimeActivityExecutionContext.FinishWorkflow"/>. The
/// runtime engine drains that request on the leaf execution path and commits a terminal
/// <c>WorkflowCompleted</c> checkpoint.
/// </summary>
/// <remarks>
/// An unbound <see cref="OutcomeName"/> defaults to <see cref="ActivityOutcomes.Done"/>. Like the other control
/// leaves (<see cref="Fault"/>), <c>Finish</c> requires the Elsa runtime activity execution context; it is not
/// usable outside a runtime execution.
/// </remarks>
public sealed class Finish : CodeActivity
{
    /// <summary>The outcome to complete the workflow with. Defaults to <see cref="ActivityOutcomes.Done"/>.</summary>
    public InputArgument<string>? OutcomeName { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var outcomeName = context.Get(OutcomeName);

        runtimeContext.FinishWorkflow(string.IsNullOrWhiteSpace(outcomeName)
            ? [ActivityOutcomes.Done]
            : [outcomeName]);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new FinishActivityException("Finish requires an Elsa runtime activity execution context.");
    }
}
