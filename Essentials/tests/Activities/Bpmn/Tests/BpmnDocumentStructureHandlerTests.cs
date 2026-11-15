using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDocumentStructureHandlerTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly BpmnDocumentStructureHandler _handler = new();

    // The empty Native subprocess invokes an Elsa Sequence. The independent Advanced body,
    // collaboration, unfamiliar extension data and multiple DI planes remain XML-owned.
    private const string FullDocument = """
        <?xml version="1.0" encoding="utf-8"?>
        <b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL"
          xmlns:bd="http://www.omg.org/spec/BPMN/20100524/DI"
          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
          xmlns:v="urn:vendor" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" id="Definitions" targetNamespace="urn:sample" exporter="vendor">
          <!-- preserve comments, prefixes, whitespace, QName text, unknown extensions and DI -->
          <b:message id="Message" name="Review"/>
          <b:process id="Process" isExecutable="true" v:opaque="body">
            <b:extensionElements><v:record id="vendor-id"><![CDATA[body & listener]]></v:record></b:extensionElements>
            <b:laneSet id="Lanes"><b:lane id="Lane"><b:flowNodeRef>Native</b:flowNodeRef></b:lane></b:laneSet>
            <b:startEvent id="Start"/>
            <b:subProcess id="Native" name="Native body"><b:extensionElements><v:body>body</v:body></b:extensionElements></b:subProcess>
            <b:sequenceFlow id="Flow" sourceRef="Start" targetRef="Native"><b:conditionExpression xsi:type="b:tFormalExpression">body == 'body'</b:conditionExpression></b:sequenceFlow>
            <b:subProcess id="Advanced" triggeredByEvent="true">
              <b:startEvent id="NestedStart"><b:messageEventDefinition messageRef="Message"/></b:startEvent>
              <b:complexGateway id="NestedGateway"><b:activationCondition xsi:type="b:tFormalExpression">ready()</b:activationCondition></b:complexGateway>
              <b:userTask id="NestedTask"><b:multiInstanceLoopCharacteristics isSequential="false"><b:loopCardinality>3</b:loopCardinality></b:multiInstanceLoopCharacteristics></b:userTask>
            </b:subProcess>
            <b:dataObject id="Data"/>
          </b:process>
          <b:collaboration id="Collaboration"><b:participant id="Pool" processRef="Process"/></b:collaboration>
          <bd:BPMNDiagram id="Diagram"><bd:BPMNPlane id="Plane" bpmnElement="Process">
            <bd:BPMNShape id="NativeShape" bpmnElement="Native" isExpanded="false" v:color="blue"><dc:Bounds x="140.5" y="70" width="100" height="80"/><bd:BPMNLabel><dc:Bounds x="150" y="160" width="80" height="20"/></bd:BPMNLabel></bd:BPMNShape>
            <bd:BPMNEdge id="FlowEdge" bpmnElement="Flow"><di:waypoint x="20" y="100"/><di:waypoint x="140.5" y="100"/></bd:BPMNEdge>
          </bd:BPMNPlane></bd:BPMNDiagram>
          <bd:BPMNDiagram id="AdvancedDiagram"><bd:BPMNPlane id="AdvancedPlane" bpmnElement="Advanced"/></bd:BPMNDiagram>
        </b:definitions>
        """;

    [Fact]
    public void Registered_versions_dispatch_without_migrating_the_frozen_graph_contract()
    {
        var services = new ServiceCollection();
        var feature = new ActivitiesBpmnDesignFeature();
        feature.ConfigureServices(services);
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IActivityStructureHandler>().ToArray();
        var service = new DefaultActivityStructureService(handlers);
        var document = Owner();
        Assert.True(service.HasHandler(document.Structure!));
        Assert.Equal(3, handlers.Length);
        Assert.Equal("body", service.ProjectChildren(document).Single().Activities.First().NodeId);

        var graph = document with
        {
            Structure = new(BpmnProcess.StructureKind, BpmnProcess.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnAuthoredStructure([new("old-child", "old-version", [], [])]), Web))
        };
        Assert.True(service.HasHandler(graph.Structure));
        Assert.Equal("old-child", service.ProjectChildren(graph).Single().Activities.Single().NodeId);
        Assert.Equal("1.0.0", service.CompileExecutableStructure(graph)!.SchemaVersion);

        var unknown = document with { Structure = new(document.Structure!.Kind, "99.0.0", document.Structure.Payload) };
        Assert.False(service.HasHandler(unknown.Structure));
        Assert.Throws<InvalidOperationException>(() => _handler.ProjectChildren(unknown));
        Assert.Throws<InvalidOperationException>(() => _handler.CompileExecutableStructure(unknown));
    }

    [Fact]
    public void Child_projection_and_replacement_preserve_complete_XML_native_bodies_listeners_and_metadata()
    {
        var owner = Owner();
        var source = owner.Structure!.Payload.Clone();
        var projection = Assert.Single(_handler.ProjectChildren(owner));
        Assert.Equal(BpmnProcess.ActivitiesSlotName, projection.Name);
        var children = projection.Activities.ToArray();
        Assert.Equal(new[] { "body", "listener" }, children.Select(child => child.NodeId));
        AssertNativeBody(children[0], "work", "local");
        AssertNativeBody(children[1], "audit", "audit-local");
        var replaced = _handler.ReplaceChildren(owner, [projection]);
        var payload = replaced.Structure!.Payload;
        Assert.Equal(FullDocument, payload.GetProperty("documentXml").GetString());
        AssertJson(source, payload);
        Assert.Equal("process-variable", Assert.Single(_handler.ProjectScopedVariables(replaced)).ReferenceKey);
        AssertJson(source, owner.Structure.Payload); // original authored node is untouched
    }

    [Fact]
    public void Property_edit_retains_binding_metadata_XML_and_other_native_records()
    {
        var owner = Owner();
        var children = _handler.ProjectChildren(owner).Single().Activities.ToArray();
        var edited = children[0] with { Inputs = [Literal("setting", "changed")] };
        var replaced = _handler.ReplaceChildren(owner, [new(BpmnProcess.ActivitiesSlotName, [edited, children[1]])]);
        var payload = replaced.Structure!.Payload;
        Assert.Equal(FullDocument, payload.GetProperty("documentXml").GetString());
        AssertJson(owner.Structure!.Payload.GetProperty("bindings"), payload.GetProperty("bindings"));
        AssertJson(owner.Structure.Payload.GetProperty("futureMetadata"), payload.GetProperty("futureMetadata"));
        AssertJson(owner.Structure.Payload.GetProperty("activities")[1], payload.GetProperty("activities")[1]);
        Assert.Equal("changed", payload.GetProperty("activities")[0].GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetString());
    }

    [Fact]
    public void Ordinary_subtree_remapper_rewrites_native_links_and_scoped_references_without_rewriting_XML_or_opaque_config()
    {
        var owner = Owner();
        var services = new ServiceCollection();
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var structures = new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>());
        var remapped = new ScopedVariableReferenceRemapper(structures).Remap(owner, new Dictionary<string, string>
        {
            ["owner"] = "owner-copy", ["body"] = "body-copy", ["work"] = "work-copy",
            ["listener"] = "listener-copy", ["audit"] = "audit-copy"
        });

        Assert.Equal("owner-copy", remapped.NodeId);
        var payload = remapped.Structure!.Payload;
        Assert.Equal(FullDocument, payload.GetProperty("documentXml").GetString());
        Assert.Equal("Process", payload.GetProperty("processId").GetString());
        var binding = Assert.Single(payload.GetProperty("bindings").EnumerateArray());
        Assert.Equal("Native", binding.GetProperty("elementId").GetString());
        Assert.Equal("body-copy", binding.GetProperty("childNodeId").GetString());
        Assert.Equal("listener-copy", binding.GetProperty("listenerNodeId").GetString());
        AssertJson(owner.Structure!.Payload.GetProperty("bindings")[0].GetProperty("futureMetadata"), binding.GetProperty("futureMetadata"));
        AssertJson(owner.Structure.Payload.GetProperty("futureMetadata"), payload.GetProperty("futureMetadata"));
        AssertJson(owner.Structure.Payload.GetProperty("variables"), payload.GetProperty("variables"));
        var children = structures.ProjectChildren(remapped).Single().Activities.ToArray();
        var work = structures.ProjectChildren(children[0]).Single().Activities.Single();
        var audit = structures.ProjectChildren(children[1]).Single().Activities.Single();
        Assert.Equal("work-copy", work.NodeId);
        Assert.Equal("audit-copy", audit.NodeId);
        AssertReference(work.Inputs.Single(), "owner-copy");
        AssertReference(work.Outputs.Single(), "body-copy");
        AssertReference(audit.Inputs.Single(), "outside-scope");
        AssertReference(audit.Outputs.Single(), "listener-copy");
        var originalWork = structures.ProjectChildren(structures.ProjectChildren(owner).Single().Activities.First()).Single().Activities.Single();
        AssertJson(originalWork.Structure!.Payload, work.Structure!.Payload);
        Assert.Equal("body", work.Structure.Payload.GetProperty("unrelatedText").GetString());
        Assert.Equal("local", structures.ProjectScopedVariables(children[0]).Single().ReferenceKey);
        Assert.Equal("audit-local", structures.ProjectScopedVariables(children[1]).Single().ReferenceKey);
        Assert.Equal("body", owner.Structure.Payload.GetProperty("bindings")[0].GetProperty("childNodeId").GetString());
    }

    [Fact]
    public void Reordering_existing_children_does_not_retarget_native_bindings()
    {
        var owner = Owner();
        var children = _handler.ProjectChildren(owner).Single().Activities.Reverse().ToArray();
        var replaced = _handler.ReplaceChildren(owner, [new(BpmnProcess.ActivitiesSlotName, children)]);
        AssertJson(owner.Structure!.Payload.GetProperty("bindings"), replaced.Structure!.Payload.GetProperty("bindings"));
        Assert.Equal("listener", _handler.ProjectChildren(replaced).Single().Activities.First().NodeId);
    }

    [Theory]
    [InlineData("missing-slot")]
    [InlineData("duplicate-slot")]
    [InlineData("wrong-slot")]
    [InlineData("deletion")]
    [InlineData("insertion")]
    [InlineData("rename-with-reorder")]
    [InlineData("duplicate-child")]
    public void Ambiguous_child_ownership_changes_refuse_atomically(string mutation)
    {
        var owner = Owner();
        var before = owner.Structure!.Payload.GetRawText();
        var children = _handler.ProjectChildren(owner).Single().Activities.ToArray();
        ActivityChildProjection[] projections = mutation switch
        {
            "missing-slot" => [],
            "duplicate-slot" => [new(BpmnProcess.ActivitiesSlotName, children), new(BpmnProcess.ActivitiesSlotName, children)],
            "wrong-slot" => [new("other", children)],
            "deletion" => [new(BpmnProcess.ActivitiesSlotName, [children[0]])],
            "insertion" => [new(BpmnProcess.ActivitiesSlotName, [.. children, children[0] with { NodeId = "added" }])],
            "rename-with-reorder" => [new(BpmnProcess.ActivitiesSlotName, [children[1], children[0] with { NodeId = "changed" }])],
            "duplicate-child" => [new(BpmnProcess.ActivitiesSlotName, [children[0], children[0]])],
            _ => throw new ArgumentException(mutation)
        };
        Assert.Throws<InvalidOperationException>(() => _handler.ReplaceChildren(owner, projections));
        Assert.Equal(before, owner.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData("elements")]
    [InlineData("sequenceFlows")]
    [InlineData("diagram")]
    [InlineData("pools")]
    [InlineData("lanes")]
    [InlineData("messageFlows")]
    [InlineData("isTransaction")]
    public void Competing_semantic_or_diagram_authorities_are_rejected_before_projection_or_replacement(string field)
    {
        AssertInvalid(payload => payload[field] = new JsonArray());
        AssertInvalid(payload => payload["bindings"]![0]![field] = new JsonArray());
    }

    [Theory]
    [InlineData("<broken>", true)]
    [InlineData("<definitions xmlns='urn:other'><process id='Process'/></definitions>", false)]
    [InlineData("<!DOCTYPE definitions [<!ENTITY body 'expanded'>]><definitions xmlns='http://www.omg.org/spec/BPMN/20100524/MODEL'>&body;</definitions>", true)]
    public void Malformed_or_entity_expanding_XML_refuses_before_lowering(string source, bool parserFailure) =>
        AssertInvalid(payload => payload["documentXml"] = source, parserFailure ? typeof(XmlException) : null);

    [Theory]
    [InlineData("activities")]
    [InlineData("bindings")]
    [InlineData("variables")]
    [InlineData("documentXml")]
    [InlineData("processId")]
    public void Missing_or_null_required_document_fields_refuse(string field)
    {
        AssertInvalid(payload => payload.Remove(field));
        AssertInvalid(payload => payload[field] = null);
    }

    [Theory]
    [InlineData("missing-process")]
    [InlineData("duplicate-XML-id")]
    [InlineData("invalid-XML-id")]
    [InlineData("missing-element")]
    [InlineData("nested-element")]
    [InlineData("non-flow-node")]
    [InlineData("missing-native")]
    [InlineData("empty-binding")]
    [InlineData("duplicate-binding")]
    [InlineData("reused-native")]
    [InlineData("unbound-native")]
    [InlineData("duplicate-native")]
    [InlineData("unknown-native-member")]
    [InlineData("duplicate-variable")]
    public void Invalid_identity_ownership_and_unrepresentable_native_fields_refuse_without_mutating_source(string mutation)
    {
        AssertInvalid(payload =>
        {
            switch (mutation)
            {
                case "missing-process": payload["processId"] = "Advanced"; break;
                case "duplicate-XML-id": payload["documentXml"] = FullDocument.Replace("id=\"AdvancedPlane\"", "id=\"Native\"", StringComparison.Ordinal); break;
                case "invalid-XML-id": payload["documentXml"] = FullDocument.Replace("id=\"AdvancedPlane\"", "id=\"bad id\"", StringComparison.Ordinal); break;
                case "missing-element": payload["bindings"]![0]!["elementId"] = "missing"; break;
                case "nested-element": payload["bindings"]![0]!["elementId"] = "NestedTask"; break;
                case "non-flow-node": payload["bindings"]![0]!["elementId"] = "Data"; break;
                case "missing-native": payload["bindings"]![0]!["childNodeId"] = "missing"; break;
                case "empty-binding": ((JsonObject)payload["bindings"]![0]!).Remove("childNodeId"); ((JsonObject)payload["bindings"]![0]!).Remove("listenerNodeId"); break;
                case "duplicate-binding": ((JsonArray)payload["bindings"]!).Add(payload["bindings"]![0]!.DeepClone()); break;
                case "reused-native": payload["bindings"]![0]!["listenerNodeId"] = "body"; break;
                case "unbound-native": ((JsonObject)payload["bindings"]![0]!).Remove("listenerNodeId"); break;
                case "duplicate-native": payload["activities"]![1]!["nodeId"] = "body"; break;
                case "unknown-native-member": payload["activities"]![0]!["futureSetting"] = "must not vanish"; break;
                case "duplicate-variable": ((JsonArray)payload["variables"]!).Add(payload["variables"]![0]!.DeepClone()); break;
                default: throw new ArgumentException(mutation);
            }
        }, mutation == "unknown-native-member" ? typeof(JsonException) : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_payload_or_binding_members_refuse(bool binding)
    {
        var owner = Owner();
        var raw = owner.Structure!.Payload.GetRawText();
        raw = binding
            ? raw.Replace("\"elementId\":\"Native\"", "\"elementId\":\"Native\",\"elementId\":\"Advanced\"", StringComparison.Ordinal)
            : raw.Insert(1, "\"processId\":\"other\",");
        using var document = JsonDocument.Parse(raw);
        var invalid = owner with { Structure = new(BpmnProcess.StructureKind, BpmnAuthoredDocument.SchemaVersion, document.RootElement) };
        Assert.Throws<InvalidOperationException>(() => _handler.ProjectChildren(invalid));
    }

    [Fact]
    public void Standard_subprocess_scope_projection_refuses_an_independent_duplicate_BPMN_body()
    {
        AssertInvalid(payload => payload["activities"]![0]!["structure"] = JsonSerializer.SerializeToNode(
            new ActivityNodeStructure(BpmnProcess.StructureKind, BpmnProcess.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnAuthoredStructure(), Web)), Web));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Production_compiler_refuses_document_source_even_for_nested_process_with_workflow_start_disabled(bool nested, bool canStartWorkflow)
    {
        var process = EmptyOwner() with
        {
            ActivityVersionId = typeof(BpmnProcess).FullName!,
            Inputs = [Literal(nameof(BpmnProcess.CanStartWorkflow), canStartWorkflow)]
        };
        var root = nested ? Sequence("outer", [process], []) : process;
        var (compiler, structures, rows) = Compiler();
        Assert.Throws<NotSupportedException>(() => compiler.CompileRoot(root, new ActivityTreeProjector(structures).Project(root), rows));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Production_compiler_rejects_unknown_document_schema_for_declared_BPMN_facet_including_nested_nodes(bool nested)
    {
        var owner = EmptyOwner();
        var unknown = owner with
        {
            ActivityVersionId = typeof(BpmnProcess).FullName!,
            Inputs = [Literal(nameof(BpmnProcess.CanStartWorkflow), false)],
            Structure = new(BpmnProcess.StructureKind, "99.0.0", owner.Structure!.Payload)
        };
        var root = nested ? Sequence("outer", [unknown], []) : unknown;
        var (compiler, structures, rows) = Compiler();
        Assert.False(structures.HasHandler(unknown.Structure));
        Assert.Throws<ArgumentException>(() => compiler.CompileRoot(root, new ActivityTreeProjector(structures).Project(root), rows));
    }

    private void AssertInvalid(Action<JsonObject> mutate, Type? parserCause = null)
    {
        var source = Owner();
        var original = source.Structure!.Payload.GetRawText();
        var payload = JsonNode.Parse(original)!.AsObject();
        mutate(payload);
        var invalid = source with { Structure = new(BpmnProcess.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(payload, Web)) };
        var invalidBefore = invalid.Structure.Payload.GetRawText();
        Refuses(() => _handler.ProjectChildren(invalid));
        Refuses(() => _handler.ReplaceChildren(invalid, []));
        Refuses(() => _handler.CompileExecutableStructure(invalid));
        Assert.Equal(original, source.Structure.Payload.GetRawText());
        Assert.Equal(invalidBefore, invalid.Structure.Payload.GetRawText());

        void Refuses(Action action)
        {
            if (parserCause is null) { Assert.Throws<InvalidOperationException>(action); return; }
            var exception = Assert.Throws<BpmnDocumentSourceException>(action);
            Assert.IsAssignableFrom<InvalidOperationException>(exception);
            Assert.Equal(parserCause, exception.InnerException?.GetType());
            Assert.Contains(invalid.NodeId, exception.Message, StringComparison.Ordinal);
            Assert.Contains("Process", exception.Message, StringComparison.Ordinal);
        }
    }

    private static ActivityNode Owner()
    {
        var work = new ActivityNode("work", "native-leaf-version",
            [Reference("source", "owner")], [Reference("target", "body")],
            new("vendor.native.configuration", "7", JsonSerializer.SerializeToElement(new
            {
                unrelatedText = "body", nested = new { arbitrary = new[] { "listener", "work", "Native" }, enabled = true }
            }, Web)));
        var audit = work with
        {
            NodeId = "audit", Inputs = [Reference("source", "outside-scope")], Outputs = [Reference("target", "listener")]
        };
        var body = Sequence("body", [work], [Variable("local")]) with { Inputs = [Literal("setting", new { mode = "body", count = 3 })] };
        var listener = Sequence("listener", [audit], [Variable("audit-local")]);
        var document = new BpmnAuthoredDocument(FullDocument, "Process", [body, listener],
            [new("Native", "body", "listener")
            {
                AdditionalProperties = new() { ["futureMetadata"] = JsonSerializer.SerializeToElement(new { text = "body", flags = new[] { 2, 4 } }, Web) }
            }], [Variable("process-variable")])
        {
            AdditionalProperties = new() { ["futureMetadata"] = JsonSerializer.SerializeToElement(new { source = "body", payload = new { version = 7 } }, Web) }
        };
        return new("owner", "bpmn-version", [], [], new(BpmnProcess.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(document, Web)));
    }

    private static ActivityNode EmptyOwner() => new("owner", "bpmn-version", [], [],
        new(BpmnProcess.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(
            new BpmnAuthoredDocument(FullDocument, "Process", [], [], []), Web)));

    private static ActivityNode Sequence(string id, ActivityNode[] children, VariableDefinition[] variables) =>
        new(id, typeof(SequenceActivity).FullName!, [], [], new(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(children, variables), Web)));

    private static VariableDefinition Variable(string key) => new(key, key, new TypeReference("String"), null,
        new ArgumentValue(JsonSerializer.SerializeToElement("body"), "Literal"));

    private static ArgumentState Reference(string key, string scope) => new(key, new ArgumentValue(new VariableReference("value", scope), "Variable"), null, null, null, null);
    private static ArgumentState Literal(string key, object value) => new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value, Web), "Literal"), null, null, null, null);

    private static void AssertReference(ArgumentState state, string scope)
    {
        Assert.Equal("Variable", state.Value.ExpressionType);
        Assert.True(VariableReference.TryParse(state.Value.Value, out var reference));
        Assert.Equal("value", reference!.ReferenceKey);
        Assert.Equal(scope, reference.DeclaringScopeId);
    }

    private static void AssertNativeBody(ActivityNode node, string childId, string variableKey)
    {
        Assert.Equal(SequenceActivity.StructureKind, node.Structure!.Kind);
        var structure = node.Structure.Payload.Deserialize<SequenceAuthoredStructure>(Web)!;
        Assert.Equal(childId, Assert.Single(structure.Activities).NodeId);
        Assert.Equal(variableKey, Assert.Single(structure.Variables).ReferenceKey);
        Assert.NotNull(structure.Activities.Single().Structure);
    }

    private static void AssertJson(JsonElement expected, JsonElement actual) => Assert.True(JsonElement.DeepEquals(expected, actual),
        $"Expected {expected.GetRawText()}{Environment.NewLine}Actual {actual.GetRawText()}");

    internal static (ExecutableNodeCompiler Compiler, DefaultActivityStructureService Structures, Dictionary<string, ActivityDefinitionVersion> Rows) Compiler()
    {
        var services = new ServiceCollection();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var structures = new DefaultActivityStructureService([new BpmnStructureHandler(), new BpmnDocumentStructureHandler(), new BpmnDocumentScopeViewHandler(), .. provider.GetServices<IActivityStructureHandler>()]);
        var registry = ClrConstruction.RegistryFor(typeof(BpmnProcess), typeof(SequenceActivity));
        var compiler = new ExecutableNodeCompiler(structures, registry, new RuntimeInputBindingCompiler(registry),
            new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()])));
        var rows = new[] { Version(typeof(BpmnProcess)), Version(typeof(SequenceActivity)) }.ToDictionary(version => version.Id);
        return (compiler, structures, rows);

        static ActivityDefinitionVersion Version(Type type) => new("1.0.0", $"definition:{type.Name}")
        {
            Id = type.FullName!,
            Definition = new ActivityDefinition { Id = $"definition:{type.Name}", ActivityTypeKey = type.FullName!, Category = "BPMN" },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = ClrConstruction.Payload(SerializationServices.PayloadSerializer(), type),
            Inputs = type == typeof(BpmnProcess)
                ? [new(nameof(BpmnProcess.CanStartWorkflow), "Start new workflows", new TypeReference("Boolean"), null, nameof(BpmnProcess.CanStartWorkflow), null, IsNullable: false)]
                : [],
            DesignFacets = type == typeof(BpmnProcess)
                ? [new(BpmnProcess.StructureKind, BpmnProcess.StructureSchemaVersion, JsonSerializer.SerializeToElement(new { }, Web))]
                : []
        };
    }
}
