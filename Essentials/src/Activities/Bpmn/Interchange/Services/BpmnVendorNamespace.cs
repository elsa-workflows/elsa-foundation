using System.Xml.Linq;
using LibInterchange = global::Bpmn.Interchange;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// Bridges the Elsa vendor namespace to the one <c>Bpmn.Interchange</c> owns.
/// <para>
/// BPMN 2.0 has no standard representation for outcome-matched flow conditions, container-scoped variable
/// declarations, multi-instance collection binding, or a fire-and-forget call, so both this host and the
/// library carry them on a vendor-namespaced attribute — under <b>different</b> namespace URIs. The library
/// hard-codes its own (<c>BpmnXmlNames.VendorNamespaceName</c>) with no way to configure it, and the Elsa wire
/// format is <see cref="NamespaceName"/>. This translates names between the two: inbound so the reader
/// interprets what Elsa authored, outbound so the document Elsa emits keeps the <c>elsa:</c> prefix its
/// consumers (and its own importer) expect.
/// </para>
/// <para>
/// Only element and attribute <em>names</em> are rewritten; attribute values are left alone. Attributes the
/// library does not itself interpret (notably <c>elsa:workflowDefinitionId</c>) survive the round trip as
/// retained foreign attributes in the vendor namespace, and come back out under <c>elsa:</c>.
/// </para>
/// </summary>
internal static class BpmnVendorNamespace
{
    /// <summary>The Elsa vendor namespace, as it appears on the wire.</summary>
    public const string NamespaceName = "https://elsa-workflows.io/schemas/bpmn";

    /// <summary>The conventional prefix emitted for <see cref="NamespaceName"/>.</summary>
    public const string Prefix = "elsa";

    /// <summary>The Elsa vendor namespace as an <see cref="XNamespace"/>.</summary>
    public static readonly XNamespace Elsa = NamespaceName;

    /// <summary>Parses a document, or returns <c>null</c> when it is not well-formed XML (the library reports that).</summary>
    public static XDocument? TryParse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            return XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Rewrites an Elsa-authored document into the vendor namespace the library reads.</summary>
    public static string ToLibrary(XDocument document) =>
        Swap(document, Elsa, LibInterchange.BpmnXmlNames.Vendor, LibInterchange.BpmnXmlNames.VendorPrefix);

    /// <summary>Rewrites a library-written document back into the Elsa vendor namespace.</summary>
    public static string ToElsa(string xml)
    {
        var document = TryParse(xml);
        return document is null ? xml : Swap(document, LibInterchange.BpmnXmlNames.Vendor, Elsa, Prefix);
    }

    private static string Swap(XDocument document, XNamespace from, XNamespace to, string prefix)
    {
        var root = document.Root;
        if (root is null)
            return document.ToString();

        foreach (var element in root.DescendantsAndSelf().ToArray())
        {
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    if (attribute.Value == from.NamespaceName)
                        attribute.Remove();
                    continue;
                }

                if (attribute.Name.Namespace != from)
                    continue;

                attribute.Remove();
                element.SetAttributeValue(to + attribute.Name.LocalName, attribute.Value);
            }

            if (element.Name.Namespace == from)
                element.Name = to + element.Name.LocalName;
        }

        if (root.GetPrefixOfNamespace(to) is null)
            root.SetAttributeValue(XNamespace.Xmlns + prefix, to.NamespaceName);

        return document.ToString();
    }
}
