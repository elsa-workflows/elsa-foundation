using System.Xml.Linq;

namespace Elsa.Activities.Bpmn.Internal;

internal static class BpmnDocumentReference
{
    internal static string? Resolve(XElement context, string? value)
    {
        if (value is null) return null;
        var parts = value.Trim().Split(':');
        var target = (string?)context.Document?.Root?.Attribute("targetNamespace");
        return parts.Length == 1 ? parts[0] : parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0 &&
            !string.IsNullOrEmpty(target) && context.GetNamespaceOfPrefix(parts[0])?.NamespaceName == target ? parts[1] : null;
    }
}
