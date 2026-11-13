using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>Raw-source/direct material and optional promotion assessment; composed API/runtime proof is separate.</summary>
public sealed class BpmnCompleteSourceDirectProjectionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly XNamespace Model = BpmnDocumentStructureHandler.ModelNamespace;
    private static readonly XNamespace Elsa = BpmnVendorNamespace.NamespaceName;
    private static readonly XNamespace Diagram = "http://www.omg.org/spec/BPMN/20100524/DI";
    private static readonly XNamespace Bounds = "http://www.omg.org/spec/DD/20100524/DC";

    [Theory]
    [InlineData(null)]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"claimRequired\":false}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"claimRequired\":true}")]
    public void V1_export_then_complete_source_rename_and_DI_edit_preserves_existing_human_material(string? metadata)
    {
        var legacy = Legacy(metadata); var complete = Complete(legacy);
        AssertMaterialEqual(legacy, complete);
        var xml = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml);
        xml.Descendants(Model + "userTask").Single().SetAttributeValue("name", "Edited task");
        xml.Descendants(Diagram + "BPMNShape").Single(shape => (string?)shape.Attribute("bpmnElement") == "work").Element(Bounds + "Bounds")!.SetAttributeValue("x", 321.5);
        complete = WithXml(complete, xml.ToString());
        var before = complete.Structure!.Payload.GetRawText();
        AssertMaterialEqual(Legacy(metadata, edited: true), complete);
        var result = BpmnAuthoredDirectProjection.Project(Version(complete), null);
        var provenance = Assert.Single(result.Source.Layout, record => record.NodeId == "bpmn-diagram:root");
        Assert.Equal(321.5, provenance.AdditionalProperties!.Value.GetProperty("shapes").GetProperty("work").GetProperty("x").GetDouble());
        Assert.Equal(before, complete.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData("FinalFault", 1)]
    [InlineData("HoldForRetry", 3)]
    public void Complete_service_source_keeps_the_exact_existing_pin_source_error_and_fault_policy(string disposition, int attempts)
    {
        var metadata = Service(disposition, attempts); var legacy = Legacy(metadata, service: true); var complete = Complete(legacy);
        var before = complete.Structure!.Payload.GetRawText();
        AssertMaterialEqual(legacy, complete);
        var result = BpmnAuthoredDirectProjection.Project(Version(complete), null);
        Assert.Equal(3, result.Definition.FormatVersion);
        var service = Assert.Single(result.Services);
        Assert.Equal("worker-source", service.SourceReferenceId); Assert.Equal("worker-artifact", service.Binding.Executable.ArtifactId);
        Assert.Equal("worker-version", service.Binding.Executable.DefinitionVersionId); Assert.Equal(new string('a', 64), service.Binding.Executable.ArtifactHash);
        Assert.Equal("worker-error", service.Binding.BusinessErrorCode); Assert.Equal(attempts, service.Binding.FaultPolicy!.MaxAttempts);
        Assert.Equal(before, complete.Structure.Payload.GetRawText());
    }

    [Fact]
    public void Library_namespace_direct_binding_and_target_namespace_QName_references_keep_the_same_material()
    {
        var legacy = Legacy("{\"formatVersion\":1,\"kind\":\"human\",\"claimRequired\":true}"); var complete = Complete(legacy);
        var xml = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml);
        var task = xml.Descendants(Model + "userTask").Single(); var metadata = task.Attribute(Elsa + "directBinding")!.Value;
        task.Attribute(Elsa + "directBinding")!.Remove(); task.SetAttributeValue(XName.Get("directBinding", global::Bpmn.Interchange.BpmnXmlNames.VendorNamespaceName), metadata);
        xml.Root!.SetAttributeValue(XNamespace.Xmlns + "target", xml.Root.Attribute("targetNamespace")!.Value);
        xml.Descendants(Model + "sequenceFlow").First().SetAttributeValue("sourceRef", "target:start");
        AssertMaterialEqual(legacy, WithXml(complete, xml.ToString()));
    }

    [Fact]
    public void Existing_parallel_human_and_coded_service_boundary_profiles_keep_the_same_material()
    {
        var parallel = Legacy(null) with { Structure = new(BpmnActivityIdentities.StructureKind, BpmnActivityIdentities.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new BpmnAuthoredStructure(elements:
                [new("start", "startEvent"), new("fork", "parallelGateway"), new("first", "userTask"), new("second", "userTask"), new("end", "endEvent")],
                sequenceFlows: [new("enter", "start", "fork"), new("a", "fork", "first"), new("b", "fork", "second"), new("c", "first", "end"), new("d", "second", "end")]), Json)) };
        AssertMaterialEqual(parallel, Complete(parallel));
        var service = Legacy(Service("FinalFault", 1), service: true);
        var original = service.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Json)!;
        service = service with { Structure = new(service.Structure.Kind, service.Structure.SchemaVersion, JsonSerializer.SerializeToElement(new BpmnAuthoredStructure(elements:
            [.. original.Elements, new("boundary", "boundaryEvent", attachedToRef: "work", eventDefinitions:
                [new(BpmnEventDefinitionTypes.Error, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "worker-error" })]), new("failed", "endEvent")],
            sequenceFlows: [.. original.SequenceFlows, new("error-route", "boundary", "failed")]), Json)) };
        AssertMaterialEqual(service, Complete(service));
    }

    [Fact]
    public void Derived_DI_provenance_keeps_idless_native_external_labels_styles_and_namespace_context()
    {
        var complete = Complete(Legacy(null)); var xml = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml);
        xml.Root!.SetAttributeValue(XNamespace.Xmlns + "target", xml.Root.Attribute("targetNamespace")!.Value);
        var diagram = xml.Descendants(Diagram + "BPMNDiagram").Single();
        diagram.Add(new XElement(Diagram + "BPMNLabelStyle", new XAttribute("id", "style"),
            new XElement(Bounds + "Font", new XAttribute("name", "Inter"), new XAttribute("size", 12), new XAttribute("isBold", "true"))));
        var shape = xml.Descendants(Diagram + "BPMNShape").Single(element => (string?)element.Attribute("bpmnElement") == "work");
        shape.SetAttributeValue("isMarkerVisible", "true");
        shape.Add(new XElement(Diagram + "BPMNLabel", new XAttribute("labelStyle", "target:style"),
            new XElement(Bounds + "Bounds", new XAttribute("x", 260.25), new XAttribute("y", 90.5), new XAttribute("width", 60), new XAttribute("height", 14))));
        complete = WithXml(complete, xml.ToString()); var before = complete.Structure!.Payload.GetRawText();
        var result = BpmnAuthoredDirectProjection.Project(Version(complete), null);
        var layout = Assert.Single(result.Source.Layout, record => record.NodeId == "bpmn-diagram:root").AdditionalProperties!.Value;
        var retained = layout.GetProperty("sourceDiagram");
        var canonicalDiagram = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml).Descendants(Diagram + "BPMNDiagram").Single();
        Assert.Equal(canonicalDiagram.ToString(SaveOptions.DisableFormatting), retained.GetProperty("xml").GetString());
        Assert.Equal(xml.Root.Attribute("targetNamespace")!.Value, retained.GetProperty("namespaceBindings").GetProperty("target").GetString());
        Assert.Equal(240, layout.GetProperty("shapes").GetProperty("work").GetProperty("x").GetDouble());
        Assert.Equal(before, complete.Structure.Payload.GetRawText());
        Assert.Equal("definition", result.Definition.Process.GetProperty("processId").GetString());
        Assert.NotEqual("definition", BpmnDocumentStructureHandler.Read(complete).ProcessId); // Preserve the established derived V1 runtime identity.
    }

    [Theory]
    [InlineData("process-shape")]
    [InlineData("flow-shape")]
    [InlineData("invalid-bounds")]
    [InlineData("invalid-boolean")]
    [InlineData("missing-label-style")]
    [InlineData("invalid-font")]
    [InlineData("invalid-edge-endpoint")]
    public void Unqualified_DI_references_and_values_cannot_disappear_into_the_view_projection(string mutation)
    {
        var complete = Complete(Legacy(null)); var xml = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml);
        var shape = xml.Descendants(Diagram + "BPMNShape").First(); var diagram = shape.Parent!.Parent!;
        switch (mutation)
        {
            case "process-shape": shape.SetAttributeValue("bpmnElement", BpmnDocumentStructureHandler.Read(complete).ProcessId); break;
            case "flow-shape": shape.SetAttributeValue("bpmnElement", "first"); break;
            case "invalid-bounds": shape.Element(Bounds + "Bounds")!.SetAttributeValue("width", "NaN"); break;
            case "invalid-boolean": shape.SetAttributeValue("isMarkerVisible", "truthy"); break;
            case "missing-label-style": shape.Add(new XElement(Diagram + "BPMNLabel", new XAttribute("labelStyle", "missing"),
                new XElement(Bounds + "Bounds", new XAttribute("x", 0), new XAttribute("y", 0), new XAttribute("width", 40), new XAttribute("height", 10)))); break;
            case "invalid-font": diagram.Add(new XElement(Diagram + "BPMNLabelStyle", new XAttribute("id", "style"),
                new XElement(Bounds + "Font", new XAttribute("name", "Inter"), new XAttribute("size", "NaN")))); break;
            case "invalid-edge-endpoint":
                shape.Parent.Elements(Diagram + "BPMNEdge").Where(edge => (string?)edge.Attribute("bpmnElement") == "first").Remove();
                shape.Parent.Add(new XElement(Diagram + "BPMNEdge", new XAttribute("id", "bad-edge"), new XAttribute("bpmnElement", "first"), new XAttribute("sourceElement", "missing"),
                    new XElement(XName.Get("waypoint", "http://www.omg.org/spec/DD/20100524/DI"), new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(XName.Get("waypoint", "http://www.omg.org/spec/DD/20100524/DI"), new XAttribute("x", 100), new XAttribute("y", 0)))); break;
        }
        complete = WithXml(complete, xml.ToString());
        var failure = Assert.Throws<BpmnModelValidationException>(() => BpmnAuthoredDirectProjection.Project(Version(complete), null));
        var reason = mutation switch
        {
            "process-shape" or "flow-shape" => "a DI shape must identify", "invalid-bounds" => "finite 'width'", "invalid-boolean" => "invalid 'isMarkerVisible'",
            "missing-label-style" => "labelStyle must resolve", "invalid-font" => "finite 'size'", _ => "sourceElement must identify"
        };
        Assert.Contains(reason, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown-attribute")]
    [InlineData("implementation")]
    [InlineData("loop")]
    [InlineData("second-process")]
    [InlineData("collaboration")]
    [InlineData("subprocess")]
    [InlineData("vendor-wrapper")]
    [InlineData("hidden-model-in-di")]
    [InlineData("duplicate-alias")]
    [InlineData("event-definition")]
    [InlineData("condition-expression")]
    [InlineData("unresolved-qname")]
    [InlineData("missing-target-namespace-qname")]
    [InlineData("duplicate-id")]
    [InlineData("unknown-di")]
    public void Unaccounted_source_is_refused_before_material_projection_and_authoring_is_untouched(string mutation)
    {
        var complete = Complete(Legacy("{\"formatVersion\":1,\"kind\":\"human\"}"));
        var xml = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml); var task = xml.Descendants(Model + "userTask").Single();
        switch (mutation)
        {
            case "unknown-attribute": task.SetAttributeValue(XName.Get("behavior", "urn:vendor"), "retained"); break;
            case "implementation": task.SetAttributeValue("implementation", "custom"); break;
            case "loop": task.Add(new XElement(Model + "standardLoopCharacteristics")); break;
            case "second-process": xml.Root!.Add(new XElement(Model + "process", new XAttribute("id", "Other"))); break;
            case "collaboration": xml.Root!.Add(new XElement(Model + "collaboration", new XAttribute("id", "Collaboration"))); break;
            case "subprocess": task.Name = Model + "subProcess"; break;
            case "vendor-wrapper": task.Add(new XElement(Model + "extensionElements", new XElement(XName.Get("opaque", "urn:vendor"), new XElement(Model + "serviceTask", new XAttribute("id", "Hidden"))))); break;
            case "hidden-model-in-di": xml.Descendants(Diagram + "BPMNShape").First().Add(new XElement(Model + "serviceTask", new XAttribute("id", "Hidden"))); break;
            case "duplicate-alias": task.SetAttributeValue(XName.Get("directBinding", global::Bpmn.Interchange.BpmnXmlNames.VendorNamespaceName), task.Attribute(Elsa + "directBinding")!.Value); break;
            case "event-definition": task.Add(new XElement(Model + "messageEventDefinition")); break;
            case "condition-expression": xml.Descendants(Model + "sequenceFlow").First().Add(new XElement(Model + "conditionExpression", "custom()")); break;
            case "unresolved-qname": xml.Descendants(Model + "sequenceFlow").First().SetAttributeValue("sourceRef", "foreign:start"); break;
            case "missing-target-namespace-qname":
                xml.Root!.SetAttributeValue("targetNamespace", null);
                xml.Descendants(Model + "sequenceFlow").First().SetAttributeValue("sourceRef", "foreign:start"); break;
            case "duplicate-id": task.SetAttributeValue("id", "start"); break;
            case "unknown-di": xml.Descendants(Diagram + "BPMNShape").First().SetAttributeValue(XName.Get("meaning", "urn:vendor"), "not qualified"); break;
        }
        complete = WithXml(complete, xml.ToString()); var before = complete.Structure!.Payload.GetRawText();
        Assert.Throws<BpmnModelValidationException>(() => BpmnAuthoredDirectProjection.Project(Version(complete), null));
        Assert.Equal(before, complete.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData("{\"formatVersion\":99,\"kind\":\"human\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"unknown\":true}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"kind\":\"human\"}")]
    [InlineData("{\"formatVersion\":1,\"kind\":\"human\",\"claimRequired\":\"true\"}")]
    [InlineData("not json")]
    public void Unknown_or_ambiguous_binding_meaning_is_retained_but_not_directly_publishable(string metadata)
    {
        var complete = Complete(Legacy(null)); var xml = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml);
        xml.Descendants(Model + "userTask").Single().SetAttributeValue(Elsa + "directBinding", metadata);
        complete = WithXml(complete, xml.ToString()); var before = complete.Structure!.Payload.GetRawText();
        Assert.Throws<BpmnModelValidationException>(() => BpmnAuthoredDirectProjection.Project(Version(complete), null));
        Assert.Equal(before, complete.Structure.Payload.GetRawText());
    }

    [Fact]
    public void Direct_binding_UTF8_and_depth_budgets_apply_before_the_closed_binding_parser()
    {
        foreach (var metadata in new[] { "{\"value\":\"" + new string('界', 6000) + "\"}", "{\"value\":" + new string('[', 33) + "0" + new string(']', 33) + "}" })
        {
            var complete = Complete(Legacy(null)); var xml = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml);
            xml.Descendants(Model + "userTask").Single().SetAttributeValue(Elsa + "directBinding", metadata);
            complete = WithXml(complete, xml.ToString()); var before = complete.Structure!.Payload.GetRawText();
            var failure = Assert.Throws<BpmnModelValidationException>(() => BpmnAuthoredDirectProjection.Project(Version(complete), null));
            Assert.Contains(metadata.Contains('界') ? "16384 UTF-8 bytes" : "direct binding JSON is invalid", failure.Message, StringComparison.Ordinal);
            Assert.Equal(before, complete.Structure.Payload.GetRawText());
        }
    }

    [Fact]
    public void Malformed_source_and_non_closed_or_nested_physical_roots_do_not_qualify_for_direct_promotion()
    {
        var complete = Complete(Legacy(null)); var admission = new BpmnDirectDocumentAdmission();
        Assert.False(admission.CanPromote(new([], WithXml(complete, "<invalid"), [], [], null)));
        var configured = complete with { Inputs = [new("argument", new("value", "Literal"), null, null, null, null)] };
        Assert.False(admission.CanPromote(new([], configured, [], [], null)));
        var container = new ActivityNode("container", "sequence-version", [], [], new("elsa.sequence", "1.0.0", JsonSerializer.SerializeToElement(new { activities = new[] { complete } }, Json)));
        Assert.False(admission.CanPromote(new([], container, [], [], null)));
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("payload")]
    [InlineData("duplicate-member")]
    [InlineData("duplicate-id")]
    public void Expected_source_reader_failures_are_domain_refusals_with_the_original_cause(string invalid)
    {
        var complete = Complete(Legacy(null)); var payload = complete.Structure!.Payload;
        switch (invalid)
        {
            case "xml": complete = WithXml(complete, "<invalid"); break;
            case "payload": complete = complete with { Structure = new(complete.Structure.Kind, complete.Structure.SchemaVersion, JsonSerializer.SerializeToElement(new[] { 1 })) }; break;
            case "duplicate-member":
                using (var repeated = JsonDocument.Parse(payload.GetRawText().Replace("\"processId\":", "\"processId\":\"other\",\"processId\":", StringComparison.Ordinal)))
                    complete = complete with { Structure = new(complete.Structure.Kind, complete.Structure.SchemaVersion, repeated.RootElement) };
                break;
            case "duplicate-id":
                var xml = XDocument.Parse(BpmnDocumentStructureHandler.Read(complete).DocumentXml); xml.Descendants(Model + "userTask").Single().SetAttributeValue("id", "start");
                complete = WithXml(complete, xml.ToString()); break;
        }
        var before = complete.Structure!.Payload.GetRawText();
        var failure = Assert.Throws<BpmnModelValidationException>(() => BpmnAuthoredDirectProjection.Project(Version(complete), null));
        var cause = Assert.IsAssignableFrom<InvalidOperationException>(failure.InnerException?.InnerException);
        if (invalid == "xml") Assert.IsType<XmlException>(Assert.IsType<BpmnDocumentSourceException>(cause).InnerException);
        Assert.False(new BpmnDirectDocumentAdmission().CanPromote(new([], complete, [], [], null)));
        Assert.Equal(before, complete.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_the_optional_direct_feature_admits_the_whole_closed_source_for_promotion_while_native_compilation_stays_closed(bool enabled)
    {
        var services = new ServiceCollection(); new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        if (enabled) new ActivitiesBpmnDirectPublishingFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var validator = Assert.Single(scope.ServiceProvider.GetServices<IDraftValidator>().OfType<BpmnDocumentAdmissionValidator>());
        var complete = Complete(Legacy("{\"formatVersion\":1,\"kind\":\"human\"}"));
        var errors = (await validator.Validate(new Draft(new([], complete, [], [], null)), default)).ToArray();
        Assert.Equal(enabled, errors.Length == 0);
        Assert.Throws<NotSupportedException>(() => new BpmnDocumentStructureHandler().CompileExecutableStructure(complete));
        var configured = new ActivityNode("native", "native-version", [], []);
        var source = BpmnDocumentStructureHandler.Read(complete) with { Activities = [configured], Bindings = [new("work", "native")] };
        complete = complete with { Structure = new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(source, Json)) };
        Assert.False(new BpmnDirectDocumentAdmission().CanPromote(new([], complete, [], [], null)));
    }

    [Fact]
    public void The_direct_admission_port_does_not_hide_implementation_failures()
    {
        var complete = Complete(Legacy(null)); var validator = new BpmnDocumentAdmissionValidator([new BpmnDocumentStructureHandler()], new BrokenAdmission());
        Assert.Throws<InvalidOperationException>(() => validator.Validate(new Draft(new([], complete, [], [], null)), default));
    }

    private sealed class BrokenAdmission : IBpmnDocumentDirectAdmission
    { public bool CanPromote(WorkflowDefinitionState state) => throw new InvalidOperationException("implementation defect"); }
    private sealed class Draft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft"; public string WorkflowDefinitionId => "definition"; public WorkflowDefinitionState State => state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch; public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
    private static WorkflowDefinitionVersion Version(ActivityNode owner) => new("definition", "1.0.0")
    { Id = "version", State = new([], owner, [], [], null) };
    private static ActivityNode Legacy(string? metadata, bool service = false, bool edited = false) => new("root", "process-version", [], [],
        new(BpmnActivityIdentities.StructureKind, BpmnActivityIdentities.StructureSchemaVersion, JsonSerializer.SerializeToElement(new BpmnAuthoredStructure(elements:
        [new("start", "startEvent"), new("work", service ? "serviceTask" : "userTask", edited ? "Edited task" : "Review task",
            properties: metadata is null ? null : new Dictionary<string, string> { [BpmnDirectBindingMetadata.PropertyKey] = metadata }), new("end", "endEvent")],
            sequenceFlows: [new("first", "start", "work"), new("last", "work", "end")],
            diagram: JsonSerializer.SerializeToElement(new { shapes = new { work = new { x = 240, y = 0, width = 100, height = 80 } } }, Json)), Json)));
    private static ActivityNode Complete(ActivityNode legacy)
    {
        var xml = new BpmnDocumentExporter().Export(legacy); var processId = XDocument.Parse(xml).Root!.Element(Model + "process")!.Attribute("id")!.Value;
        return legacy with { Structure = new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion,
            JsonSerializer.SerializeToElement(new BpmnAuthoredDocument(xml, processId, [], [], []), Json)) };
    }
    private static ActivityNode WithXml(ActivityNode complete, string xml) => complete with { Structure = new(complete.Structure!.Kind, complete.Structure.SchemaVersion,
        JsonSerializer.SerializeToElement(BpmnDocumentStructureHandler.Read(complete) with { DocumentXml = xml }, Json)) };
    private static void AssertMaterialEqual(ActivityNode legacy, ActivityNode complete) => Assert.Equal(
        JsonSerializer.Serialize(BpmnAuthoredDirectProjection.Project(Version(legacy), null).Definition, Json),
        JsonSerializer.Serialize(BpmnAuthoredDirectProjection.Project(Version(complete), null).Definition, Json));
    private static string Service(string disposition, int attempts) => JsonSerializer.Serialize(new
    {
        formatVersion = 1, kind = "nativeService", sourceReferenceId = "worker-source",
        executable = new { artifactId = "worker-artifact", definitionId = "worker-definition", definitionVersionId = "worker-version", artifactVersion = "1.0.0", artifactHash = new string('a', 64) },
        businessErrorCode = "worker-error", faultPolicy = new { formatVersion = 1, disposition, maxAttempts = attempts }
    }, Json);
}
