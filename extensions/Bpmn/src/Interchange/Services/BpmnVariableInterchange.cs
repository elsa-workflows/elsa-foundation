using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using LibModel = global::Bpmn.Model;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>Native scalar hints and explicit refusal of settings the pinned XML profile cannot carry.</summary>
internal static class BpmnVariableInterchange
{
    private static readonly HashSet<string> ScalarAliases = new(StringComparer.Ordinal)
        { "Object", "String", "Boolean", "Int32", "Int64", "Decimal", "Double", "Single", "Elsa.Any" };

    public static bool TryExport(VariableDefinition variable, out LibModel.BpmnVariableDeclaration declaration, out string? reason)
    {
        declaration = new(variable.Name);
        reason = variable.ReferenceKey != variable.Name ? "This XML profile has one variable name; different reference keys and display names cannot travel losslessly."
            : variable.Type.CollectionKind != CollectionKind.Single || !ScalarAliases.Contains(variable.Type.Alias) ? "This variable type is outside the supported native scalar XML profile."
            : !string.IsNullOrWhiteSpace(variable.StorageDriverType) ? "This variable's storage driver belongs to the originating resource."
            : variable.Default is not null ? "The pinned BPMN XML profile cannot retain a variable default; keep it in its native authoring resource."
            : null;
        if (reason is not null) return false;
        declaration = new(variable.Name, variable.Type.Alias);
        return true;
    }

    public static VariableDefinition Import(LibModel.BpmnVariableDeclaration variable, string processId, List<BpmnImportIssue> issues)
    {
        if (variable.TypeHint is { } hint && !ScalarAliases.Contains(hint))
        {
            issues.Add(new(BpmnImportIssueSeverity.Degraded,
                $"Variable '{variable.Name}' has an unsupported native scalar type hint; it imports as Object without its default.", processId));
            return new(variable.Name, variable.Name, new TypeReference("Object"), null, null);
        }
        return new(variable.Name, variable.Name, new TypeReference(variable.TypeHint ?? "Object"), null,
            variable.DefaultValue is { } value ? new ArgumentValue(value.Clone(), "Literal") : null);
    }
}
