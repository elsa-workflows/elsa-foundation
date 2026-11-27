using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using LibInterchange = global::Bpmn.Interchange;
using LibModel = global::Bpmn.Model;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// Retains owned binding JSON without interpreting its version or admitting its executable semantics.
/// Publication validates the binding schema; interchange must also preserve future versions for correction.
/// </summary>
internal static class BpmnDirectBindingMetadata
{
    public const string PropertyKey = "elsa.direct.binding";
    private const string AttributeName = "directBinding";
    private const int MaximumBytes = 16 * 1024;
    private const int MaximumDepth = 32;

    public static bool IsAttribute(LibModel.BpmnForeignAttribute attribute) =>
        attribute.Name.Namespace == LibInterchange.BpmnXmlNames.VendorNamespaceName &&
        attribute.Name.LocalName == AttributeName;

    public static LibModel.BpmnForeignAttribute ToAttribute(string value, string elementId) =>
        new(new LibModel.BpmnQName(LibInterchange.BpmnXmlNames.VendorNamespaceName, AttributeName), Validate(value, elementId));

    /// <summary>Check aliases before namespace translation could overwrite one conflicting value.</summary>
    public static void ValidateSourceAttributes(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            var attributes = element.Attributes().Where(attribute => attribute.Name.LocalName == AttributeName &&
                (attribute.Name.NamespaceName == BpmnVendorNamespace.NamespaceName ||
                 attribute.Name.NamespaceName == LibInterchange.BpmnXmlNames.VendorNamespaceName)).ToArray();
            var subject = element.Attribute("id")?.Value ?? element.Name.LocalName;
            if (attributes.Length > 1)
                throw new BpmnInterchangeException($"Element '{subject}' has ambiguous direct binding attributes.");
            if (attributes.Length == 1)
                Validate(attributes[0].Value, subject);
        }
    }

    public static string Validate(string? value, string elementId)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > MaximumBytes)
            throw new BpmnInterchangeException($"Element '{elementId}' direct binding must be nonblank JSON of at most {MaximumBytes} UTF-8 bytes.");
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new BpmnInterchangeException($"Element '{elementId}' direct binding must be a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new BpmnInterchangeException($"Element '{elementId}' direct binding JSON is invalid.", exception);
        }
        return value;
    }
}
