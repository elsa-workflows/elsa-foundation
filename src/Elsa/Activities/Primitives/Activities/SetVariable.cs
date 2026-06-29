using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Sets a single workflow variable to a value. Ported from elsa-core's <c>SetVariable</c> activity and
/// adapted to this repo's runtime model: the activity is a CLR leaf (resolved by the existing
/// <c>ClrActivityConstructor</c>, like <see cref="WriteLine"/>) and assigns the variable through the visible
/// scope chain (<c>context.ExpressionExecutionContext.SetVariable</c> →
/// <c>VariableScope.TrySetValueByName</c>). The assignment lands in the declaring (workflow or container)
/// scope; the Seam-C keystone write-back (#286) re-emits a mutated workflow-scope variable as a durable
/// value, so the new value is durable and re-projected into <c>variables.*</c> for downstream activities.
/// </summary>
/// <remarks>
/// A blank <see cref="Variable"/> name is a no-op (nothing is assigned). The variable must be declared in a
/// visible scope; an assignment to an undeclared name falls back to local activity memory and is not durably
/// persisted (it has no declaring scope to land in), mirroring <c>VariableScope.TrySetValueByName</c>'s
/// nearest-scope-by-name resolution.
/// </remarks>
public sealed class SetVariable : CodeActivity
{
    /// <summary>The authored name of the workflow variable to assign.</summary>
    public InputArgument<string>? Variable { get; set; }

    /// <summary>The value to assign to the variable.</summary>
    public InputArgument<object>? Value { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var name = context.Get(Variable);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var value = context.Get(Value);
        context.ExpressionExecutionContext.SetVariable(name, value);
    }
}
