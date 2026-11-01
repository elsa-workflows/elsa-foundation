using System.Text.Json;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class BpmnDirectBindingInterchangeTests
{
    private const string Property = "elsa.direct.binding";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly BpmnDocumentExporter _exporter = new();
    private readonly BpmnDocumentImporter _importer = new(new BpmnWorkBinder());

    [Theory]
    [InlineData("{\n  \"formatVersion\":1,\"kind\":\"human\",\"label\":\"Review <order> & confirm ✓\"\n}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"nativeService\",\"sourceReferenceId\":\"worker-source\",\"executable\":{\"artifactId\":\"worker-artifact\"}}")]
    [InlineData("{\"formatVersion\":99,\"kind\":\"future\",\"configuration\":{\"meaning\":true}}")]
    public void Export_import_preserves_exact_owned_json_without_degradation(string metadata)
    {
        var xml = _exporter.Export(Node(metadata));
        var document = XDocument.Parse(xml);
        var attribute = Assert.Single(document.Descendants().Attributes(BpmnVendorNamespace.Elsa + "directBinding"));
        Assert.Equal(metadata, attribute.Value);

        var imported = _importer.Import(xml);
        Assert.Equal(metadata, Human(imported.ProcessNode).Properties[Property]);
        Assert.DoesNotContain(imported.Analysis.Issues, issue => issue.ElementId == "human" &&
            issue.Severity is BpmnImportIssueSeverity.Degraded or BpmnImportIssueSeverity.Dropped);
    }

    [Fact]
    public void Import_still_reports_unrepresented_neighbor_attributes()
    {
        var document = XDocument.Parse(_exporter.Export(Node("{\"formatVersion\":1,\"kind\":\"human\"}")));
        document.Descendants().Single(element => element.Attribute("id")?.Value == "human")
            .SetAttributeValue(XName.Get("businessRule", "urn:other-vendor"), "must not disappear silently");

        var imported = _importer.Import(document.ToString());
        Assert.True(Human(imported.ProcessNode).Properties.ContainsKey(Property));
        Assert.Contains(imported.Analysis.Issues, issue => issue.ElementId == "human" &&
            issue.Severity == BpmnImportIssueSeverity.Degraded);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Export_and_import_refuse_invalid_owned_json_with_element_context(string metadata)
    {
        var exported = Assert.Throws<BpmnInterchangeException>(() => _exporter.Export(Node(metadata)));
        Assert.Contains("human", exported.Message);

        var document = XDocument.Parse(_exporter.Export(Node(null)));
        document.Descendants().Single(element => element.Attribute("id")?.Value == "human")
            .SetAttributeValue(BpmnVendorNamespace.Elsa + "directBinding", metadata);
        var imported = Assert.Throws<BpmnInterchangeException>(() => _importer.Import(document.ToString()));
        Assert.Contains("human", imported.Message);
    }

    [Fact]
    public void Owned_metadata_limit_counts_utf8_bytes_and_applies_in_both_directions()
    {
        var metadata = "{\"value\":\"" + new string('界', 6000) + "\"}";
        Assert.True(metadata.Length < 16 * 1024);
        Assert.Throws<BpmnInterchangeException>(() => _exporter.Export(Node(metadata)));

        var document = XDocument.Parse(_exporter.Export(Node(null)));
        document.Descendants().Single(element => element.Attribute("id")?.Value == "human")
            .SetAttributeValue(BpmnVendorNamespace.Elsa + "directBinding", metadata);
        Assert.Throws<BpmnInterchangeException>(() => _importer.Import(document.ToString()));
    }

    [Fact]
    public void Import_refuses_two_vendor_namespace_aliases_instead_of_overwriting_one()
    {
        var document = XDocument.Parse(_exporter.Export(Node(null)));
        var human = document.Descendants().Single(element => element.Attribute("id")?.Value == "human");
        human.SetAttributeValue(BpmnVendorNamespace.Elsa + "directBinding", "{\"kind\":\"human\"}");
        human.SetAttributeValue(XName.Get("directBinding", global::Bpmn.Interchange.BpmnXmlNames.VendorNamespaceName), "{\"kind\":\"nativeService\"}");
        Assert.Throws<BpmnInterchangeException>(() => _importer.Import(document.ToString()));
    }

    [Fact]
    public void Owned_metadata_depth_is_bounded_in_both_directions()
    {
        var metadata = "{\"value\":" + new string('[', 33) + "0" + new string(']', 33) + "}";
        Assert.IsAssignableFrom<JsonException>(Assert.Throws<BpmnInterchangeException>(() => _exporter.Export(Node(metadata))).InnerException);

        var document = XDocument.Parse(_exporter.Export(Node(null)));
        document.Descendants().Single(element => element.Attribute("id")?.Value == "human")
            .SetAttributeValue(BpmnVendorNamespace.Elsa + "directBinding", metadata);
        Assert.IsAssignableFrom<JsonException>(Assert.Throws<BpmnInterchangeException>(() => _importer.Import(document.ToString())).InnerException);
    }

    [Fact]
    public void Old_documents_add_no_direct_binding_attribute_or_property()
    {
        var xml = _exporter.Export(Node(null));
        Assert.DoesNotContain("directBinding", xml, StringComparison.Ordinal);
        Assert.False(Human(_importer.Import(xml).ProcessNode).Properties.ContainsKey(Property));
    }

    private static BpmnElement Human(ActivityNode node) =>
        node.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Json)!.Elements.Single(element => element.ElementId == "human");

    private static ActivityNode Node(string? metadata)
    {
        var structure = new BpmnAuthoredStructure(elements:
        [
            new BpmnElement("start", BpmnElementTypes.StartEvent),
            new BpmnElement("human", BpmnElementTypes.UserTask,
                properties: metadata is null ? null : new Dictionary<string, string> { [Property] = metadata }),
            new BpmnElement("end", BpmnElementTypes.EndEvent)
        ], sequenceFlows:
        [
            new BpmnSequenceFlow("to-human", "start", "human"),
            new BpmnSequenceFlow("to-end", "human", "end")
        ]);
        return new ActivityNode("process", "Elsa.BpmnProcess", [], [],
            new ActivityNodeStructure(BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Json)));
    }
}
