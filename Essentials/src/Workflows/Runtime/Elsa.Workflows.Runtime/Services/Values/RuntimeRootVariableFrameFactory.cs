using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>One canonical workflow-root declaration/seed materializer used by starts and read-only test preparation.</summary>
internal static class RuntimeRootVariableFrameFactory
{
    internal static VariableFrameState Create(
        string workflowExecutionId,
        IReadOnlyDictionary<string, JsonElement> seedVariables,
        WorkflowExecutable? executable)
    {
        if (executable is null)
            throw new InvalidOperationException($"Workflow execution '{workflowExecutionId}' cannot activate its canonical root variable frame without the pinned executable.");

        // The root frame declares exactly the workflow-scope variables (state.Variables compiled into the
        // executable, #972). The root ACTIVITY's structure variables are a normal container scope owned by the
        // root node itself — they are NOT folded into the workflow scope here.
        var projector = new RuntimeVariableDeclarationProjector();
        var declarations = projector.ProjectDeclarations(executable.WorkflowVariables);
        var initial = projector.ProjectInitialValues(executable.WorkflowVariables);
        var values = new Dictionary<string, ValueEnvelope>(initial, StringComparer.Ordinal);
        foreach (var (referenceKey, declaration) in declarations)
        {
            if (!seedVariables.TryGetValue(referenceKey, out var seed) &&
                !seedVariables.TryGetValue(declaration.Name, out seed))
                continue;

            var declared = initial[referenceKey];
            values[referenceKey] = seed.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? ValueEnvelope.Null(declared.Type, declared.Policy)
                : ValueEnvelope.Inline(declared.Type, seed, declared.Policy);
        }

        return new VariableFrameFactory().CreateRoot(
            workflowExecutionId,
            VariableReference.WorkflowScopeId,
            values);
    }

}
