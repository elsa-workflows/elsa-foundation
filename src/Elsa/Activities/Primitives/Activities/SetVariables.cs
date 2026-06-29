using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Sets several workflow variables in one step. The multi-variable counterpart of <see cref="SetVariable"/>:
/// each entry of the <see cref="Variables"/> map (variable name → value) is assigned through the visible
/// scope chain (<c>context.ExpressionExecutionContext.SetVariable</c> → <c>VariableScope.TrySetValueByName</c>),
/// so each mutation lands in its declaring scope and the Seam-C keystone write-back (#286) persists the
/// workflow-scope ones durably. Ported from elsa-core and adapted to this repo's model; resolved by the
/// existing <c>ClrActivityConstructor</c> like <see cref="SetVariable"/>.
/// </summary>
/// <remarks>
/// A null/empty map assigns nothing. Entries with a blank name are skipped, mirroring <see cref="SetVariable"/>.
/// </remarks>
public sealed class SetVariables : CodeActivity
{
    /// <summary>The variables to assign, keyed by authored variable name. Each value is assigned in order.</summary>
    public InputArgument<IDictionary<string, object?>>? Variables { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var variables = context.Get(Variables);
        if (variables is null)
            return;

        foreach (var (name, value) in variables)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            context.ExpressionExecutionContext.SetVariable(name, value);
        }
    }
}
