using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Models;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// SetVariable (R7): the Elsa 3 <c>variable</c> object becomes the <c>set</c> intrinsic's variable target, a
/// <see cref="VariableReference"/> whose declaring scope is the nearest ancestor container that declares the variable,
/// else the workflow. The variable's type pins the intrinsic's value type.
/// </summary>
internal sealed class Elsa3VariableReferenceRewrite : IElsa3NodeRewrite
{
    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.VariableReference;

    public ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        if (!context.TryGetProperty("Variable", out var property, out var variable) ||
            variable.ValueKind != JsonValueKind.Object ||
            !variable.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            context.Error(Elsa3ActivityDiagnosticCodes.RequiredInputUnbound,
                "SetVariable names no variable.",
                "Select the variable the activity sets.",
                property);
            return ValueTask.CompletedTask;
        }

        var id = idElement.GetString()!;
        foreach (var ancestor in context.Ancestors())
        {
            if (ancestor.Variables.FirstOrDefault(declared => StringComparer.Ordinal.Equals(declared.ReferenceKey, id)) is { } scoped)
            {
                Pin(context, new VariableReference(id, Elsa3ConversionDiagnostics.NodeId(ancestor.Node)), scoped.Type);
                return ValueTask.CompletedTask;
            }
        }

        if (context.Plan.Definition.Variables.FirstOrDefault(declared => StringComparer.Ordinal.Equals(declared.Id, id)) is { } workflowVariable)
        {
            Pin(context, new VariableReference(id, VariableReference.WorkflowScopeId), context.ResolveType(workflowVariable.TypeName ?? "Object"));
            return ValueTask.CompletedTask;
        }

        context.Error(Elsa3ImportDiagnosticCodes.DanglingReference,
            $"SetVariable sets variable '{id}', which neither the workflow nor an enclosing container declares.",
            "Declare the variable, or point the activity at a declared one.",
            property);
        return ValueTask.CompletedTask;
    }

    private static void Pin(Elsa3RewriteContext context, VariableReference reference, TypeReference type) =>
        context.Node.Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Set, type, reference);
}
