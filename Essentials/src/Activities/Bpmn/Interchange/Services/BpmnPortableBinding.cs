using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// The only bound work that travels in a portable BPMN document: an exact activity version with literal,
/// non-sensitive inputs and no engine-owned shape. Anything else (expression or secret bindings, sensitive values,
/// intrinsics, background placement, nested structure) stays out of exported XML and is refused on import, so a
/// shared file can neither leak authored secrets nor smuggle bindings past the importing author.
/// </summary>
internal static class BpmnPortableBinding
{
    /// <summary>The task elements whose bound child the library imports as unbound work.</summary>
    public static bool IsPortableHost(string elementType) => elementType is "task" or "serviceTask" or "userTask" or "manualTask" or "businessRuleTask";

    /// <summary>True when a retained attribute value parses to a portable binding the importer will honour.</summary>
    public static bool IsReadable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return JsonSerializer.Deserialize<ActivityNode>(value, BpmnDocumentImporter.BoundActivitySerializerOptions) is { } node && Problem(node) is null; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) { return false; }
    }

    public static string? Problem(ActivityNode node)
    {
        if (string.IsNullOrWhiteSpace(node.ActivityVersionId))
            return "The retained Elsa activity binding names no activity version.";
        if (node.Structure is not null || node.Intrinsic is not null || node.ExecutionPlacement != default)
            return "The retained Elsa activity binding carries engine-owned structure or placement.";
        if (node.Inputs is null || node.Outputs is null || node.Inputs.Any(x => x is null) || node.Outputs.Any(x => x is null))
            return "The retained Elsa activity binding is incomplete.";
        foreach (var input in node.Inputs)
            if (input.Value is null || input.IsSensitive == true || input.StorageDriverType is not null || input.EvaluatorType is not null ||
                !string.Equals(input.Value.ExpressionType ?? "Literal", "Literal", StringComparison.Ordinal))
                return $"Input '{input.ReferenceKey}' is not a portable literal; bind it explicitly after import.";
        foreach (var output in node.Outputs)
            if (output.IsSensitive == true || output.StorageDriverType is not null)
                return $"Output '{output.ReferenceKey}' is not portable; map it explicitly after import.";
        return null;
    }
}
