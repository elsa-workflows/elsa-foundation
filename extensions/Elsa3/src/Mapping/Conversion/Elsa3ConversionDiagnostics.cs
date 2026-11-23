using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Builds node-level conversion diagnostics carrying the definition id and version, the node id, the JSON path, the
/// property where relevant, the Elsa 3 type and guidance (spec FR-012).
/// </summary>
internal static class Elsa3ConversionDiagnostics
{
    public static Elsa3MigrationDiagnostic Error(
        Elsa3ConversionPlan plan,
        Elsa3NodePlan node,
        string code,
        string message,
        string guidance,
        string? property = null,
        string? withinProperty = null) =>
        Create(Elsa3MigrationDiagnosticSeverity.Error, plan, node, code, message, guidance, property, withinProperty);

    public static Elsa3MigrationDiagnostic Warning(
        Elsa3ConversionPlan plan,
        Elsa3NodePlan node,
        string code,
        string message,
        string guidance,
        string? property = null,
        string? withinProperty = null) =>
        Create(Elsa3MigrationDiagnosticSeverity.Warning, plan, node, code, message, guidance, property, withinProperty);

    /// <summary>A definition-level diagnostic (no node), for reusable-workflow outcomes and the like.</summary>
    public static Elsa3MigrationDiagnostic ForDefinition(
        Elsa3ConversionPlan plan,
        Elsa3MigrationDiagnosticSeverity severity,
        string code,
        string message,
        string guidance,
        string path,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        var metadata = Metadata(plan);
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            metadata[key] = value;
        return new Elsa3MigrationDiagnostic(severity, code, message, path, guidance, metadata);
    }

    private static Elsa3MigrationDiagnostic Create(
        Elsa3MigrationDiagnosticSeverity severity,
        Elsa3ConversionPlan plan,
        Elsa3NodePlan node,
        string code,
        string message,
        string guidance,
        string? property,
        string? withinProperty)
    {
        var metadata = Metadata(plan);
        metadata["ActivityNodeId"] = NodeId(node.Node);
        metadata["Elsa3Type"] = node.Node.Type ?? string.Empty;
        if (property is not null)
            metadata["Property"] = property;
        // withinProperty continues the path inside the property's value, such as "[1].condition" for a switch case.
        var path = property is null ? node.JsonPath : $"{node.JsonPath}{FormatProperty(property)}{withinProperty}";
        return new Elsa3MigrationDiagnostic(severity, code, message, path, guidance, metadata);
    }

    private static Dictionary<string, string> Metadata(Elsa3ConversionPlan plan)
    {
        var metadata = plan.Request.DiagnosticMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["DefinitionId"] = plan.Request.Definition.DefinitionId ?? string.Empty;
        metadata["DefinitionVersion"] = plan.Request.Definition.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return metadata;
    }

    public static string NodeId(Elsa3Activity node) =>
        !string.IsNullOrWhiteSpace(node.NodeId) ? node.NodeId : node.Id ?? string.Empty;

    /// <summary>A JSON path segment for a property, as the value-flow lowerer writes it.</summary>
    public static string FormatProperty(string propertyName)
    {
        if (propertyName.Length > 0 &&
            (char.IsLetter(propertyName[0]) || propertyName[0] is '_' or '$') &&
            propertyName.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$'))
            return $".{propertyName}";
        var escaped = propertyName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
        return $"['{escaped}']";
    }

    /// <summary>The camelCase JSON name Elsa 3 serializes a member under.</summary>
    public static string JsonName(string memberName) =>
        memberName.Length == 0 ? memberName : char.ToLowerInvariant(memberName[0]) + memberName[1..];
}
