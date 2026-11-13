using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class BpmnCompleteSourceExportTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:v="urn:vendor" targetNamespace="urn:source">
          <!-- complete unsupported source is still exportable -->
          <b:process id="P" isExecutable="true"><b:serviceTask id="Work"/><b:complexGateway id="Advanced"/>
            <b:subProcess id="Sub"><b:startEvent id="NestedStart"/></b:subProcess>
            <b:extensionElements><v:mixed>before <v:child/> after <![CDATA[raw < text]]></v:mixed></b:extensionElements>
          </b:process><b:process id="Other"/>
        </b:definitions>
        """;

    [Fact]
    public async Task Canonical_export_keeps_exact_complete_XML_and_reports_disjoint_native_configuration_without_serializing_it()
    {
        var owner = Owner();
        var before = owner.Structure!.Payload.Clone();
        var exporter = new BpmnDocumentExporter();
        Assert.Equal(Xml, exporter.Export(owner));
        var result = await exporter.ExportAsync(owner);
        Assert.Equal(Xml, result.Xml);
        Assert.Contains(result.Omissions, omission => omission.ElementId == "Work" && omission.Code == "native-configuration-not-exported");
        Assert.Contains(result.Omissions, omission => omission.ElementId == "Sub" && omission.Code == "scope-configuration-not-exported");
        Assert.Contains(result.Omissions, omission => omission.ElementId == "P" && omission.Code == "process-configuration-not-exported");
        Assert.DoesNotContain("secret-native-value", result.Xml, StringComparison.Ordinal);
        Assert.True(JsonElement.DeepEquals(before, owner.Structure.Payload));
    }

    [Fact]
    public async Task Canonical_export_refuses_implicit_process_identity_replacement()
    {
        var exporter = new BpmnDocumentExporter();
        Assert.Equal(Xml, exporter.Export(Owner(), new() { ProcessId = "P" }));
        Assert.Throws<BpmnInterchangeException>(() => exporter.Export(Owner(), new() { ProcessId = "renamed" }));
        await Assert.ThrowsAsync<BpmnInterchangeException>(() => exporter.ExportAsync(Owner(), new() { ProcessId = "renamed" }));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("99.0.0")]
    public async Task Complete_source_cannot_fall_back_to_a_legacy_or_unknown_schema(string schema)
    {
        var owner = Owner();
        owner = owner with { Structure = new(owner.Structure!.Kind, schema, owner.Structure.Payload) };
        var before = owner.Structure.Payload.GetRawText();
        var exporter = new BpmnDocumentExporter();
        Assert.Throws<BpmnInterchangeException>(() => exporter.Export(owner));
        await Assert.ThrowsAsync<BpmnInterchangeException>(() => exporter.ExportAsync(owner));
        Assert.Equal(before, owner.Structure.Payload.GetRawText());
    }

    private static ActivityNode Owner()
    {
        var native = new ActivityNode("native", "native-exact-version", [new("secret", new("secret-native-value", "Literal"), null, null, null, null)], []);
        var variable = new VariableDefinition("local", "Local", new TypeReference("String"), null, null);
        var source = new BpmnAuthoredDocument(Xml, "P", [native], [new("Work", native.NodeId)], [variable])
        { ScopeHeaders = [new("Sub", "sub-native", "process-exact-version", [], [], [variable])] };
        return new("source-owner", "process-exact-version", [], [], new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion,
            JsonSerializer.SerializeToElement(source, Json)));
    }
}
