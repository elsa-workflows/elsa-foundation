using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using NativeSequence = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDocumentAdmissionTests
{
    private static readonly JsonSerializerOptions Json = BpmnDocumentStructureHandler.SerializerOptions;
    private const string Xml = """
        <b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:tns="urn:test" xmlns:elsa="https://elsa-workflows.io/schemas/bpmn" targetNamespace="urn:test">
          <b:process id="P" isExecutable="true"><b:startEvent id="Start"/><b:task id="Work"/><b:endEvent id="End"/>
            <b:sequenceFlow id="First" sourceRef="tns:Start" targetRef="tns:Work"/><b:sequenceFlow id="Last" sourceRef="Work" targetRef="End"/>
          </b:process>
        </b:definitions>
        """;

    [Fact]
    public async Task Internal_lowering_and_existing_schema1_compiler_proof_preserve_source_without_enabling_source2()
    {
        var owner = Owner();
        var before = owner.Structure!.Payload.Clone();
        var (compiler, structures, rows) = BpmnDocumentStructureHandlerTests.Compiler();
        var derived = DerivedSchema1(owner, structures);
        var compiled = compiler.CompileRoot(derived, new ActivityTreeProjector(structures).Project(derived), rows);
        Assert.Equal("1.0.0", compiled.Structure!.SchemaVersion);
        var graph = compiled.Structure.Payload.Deserialize<BpmnStructure>(Json)!;
        Assert.Equal(new[] { "Start", "Work", "End" }, graph.Elements.Select(element => element.ElementId));
        Assert.Equal("Start", graph.SequenceFlows.First().SourceRef);
        var contribution = await Verify(Context(owner, compiled));
        Assert.Empty(contribution.NodeMetadata);
        Assert.Equal("2.0.0", owner.Structure.SchemaVersion);
        Assert.True(JsonElement.DeepEquals(before, owner.Structure.Payload));
    }

    [Fact]
    public async Task Internal_standard_scope_projection_proof_keeps_exact_native_scope_identity_in_derived_schema1()
    {
        var nestedXml = Xml.Replace("<b:task id=\"Work\"/>", "<b:subProcess id=\"Work\"><b:startEvent id=\"InnerStart\"/><b:task id=\"InnerTask\"/><b:endEvent id=\"InnerEnd\"/><b:sequenceFlow id=\"InnerFirst\" sourceRef=\"InnerStart\" targetRef=\"InnerTask\"/><b:sequenceFlow id=\"InnerLast\" sourceRef=\"InnerTask\" targetRef=\"InnerEnd\"/></b:subProcess>", StringComparison.Ordinal);
        var owner = Owner(nestedXml, headers: [new("Work", "scope-work", typeof(BpmnProcess).FullName!, [Literal(false)], [], [])]);
        var (compiler, structures, rows) = BpmnDocumentStructureHandlerTests.Compiler();
        var derived = DerivedSchema1(owner, structures);
        var projection = new ActivityTreeProjector(structures).Project(derived);
        var compiled = compiler.CompileRoot(derived, projection, rows);
        var child = Assert.Single(compiled.ChildSlots.Single().Activities);
        Assert.Equal("scope-work", child.ExecutableNodeId);
        Assert.Equal(BpmnActivityIdentities.ProcessType, child.ActivityType);
        Assert.Equal("scope-work", compiled.Structure!.Payload.Deserialize<BpmnStructure>(Json)!.Elements.Single(element => element.ElementId == "Work").ChildNodeId);
        Assert.Equal("InnerTask", child.Structure!.Payload.Deserialize<BpmnStructure>(Json)!.Elements.Single(element => element.ElementType == "task").ElementId);
        await Verify(Context(owner, compiled));
        Assert.Equal(nestedXml, BpmnDocumentStructureHandler.Read(owner).DocumentXml);
    }

    [Fact]
    public void Bound_Decision_outcomes_and_defaults_are_projected_from_actual_XML_including_qualified_source_references()
    {
        var xml = Xml.Replace("<b:task id=\"Work\"/>", "<b:exclusiveGateway id=\"Work\" default=\"Last\"/><b:endEvent id=\"HighEnd\"/>", StringComparison.Ordinal)
            .Replace("</b:process>", "<b:sequenceFlow id=\"High\" sourceRef=\"tns:Work\" targetRef=\"HighEnd\" elsa:conditionOutcome=\"High\"/></b:process>", StringComparison.Ordinal);
        var owner = Owner(xml);
        var source = BpmnDocumentStructureHandler.Read(owner);
        var decision = new ActivityNode("decision", "exact-decision-version", [], []);
        owner = BpmnDocumentScopeProjection.Write(owner, source with { Activities = [decision], Bindings = [new("Work", decision.NodeId)] });
        Assert.Empty(BpmnDocumentAdmission.Assess(owner));
        var projection = new BpmnDocumentStructureHandler().ProjectChildren(owner).Single();
        var outcomes = Assert.Single(projection.OutcomeProjections);
        Assert.Equal(BpmnActivityIdentities.DecisionType, outcomes.ActivityTypeKey);
        Assert.Equal("High", Assert.Single(outcomes.OutcomeNames));
        var graph = BpmnDocumentAdmission.Lower(owner, "P").Payload.Deserialize<BpmnStructure>(Json)!;
        Assert.Equal("decision", graph.Elements.Single(element => element.ElementId == "Work").ChildNodeId);
        Assert.True(graph.SequenceFlows.Single(flow => flow.FlowId == "Last").IsDefault);
        Assert.Equal("High", graph.SequenceFlows.Single(flow => flow.FlowId == "High").ConditionOutcome);
    }

    [Fact]
    public async Task Internal_native_Sequence_lowering_proof_keeps_its_actual_structure_and_identity()
    {
        var owner = Owner(Xml.Replace("<b:task id=\"Work\"/>", "<b:subProcess id=\"Work\"/>", StringComparison.Ordinal));
        var native = new ActivityNode("native-body", typeof(NativeSequence).FullName!, [], [], new(NativeSequence.StructureKind,
            NativeSequence.StructureSchemaVersion, JsonSerializer.SerializeToElement(new SequenceAuthoredStructure([], []), Json)));
        var source = BpmnDocumentStructureHandler.Read(owner);
        owner = BpmnDocumentScopeProjection.Write(owner, source with { Activities = [native], Bindings = [new("Work", native.NodeId)] });
        var before = owner.Structure!.Payload.Clone();
        var (compiler, structures, rows) = BpmnDocumentStructureHandlerTests.Compiler();
        var derived = DerivedSchema1(owner, structures);
        var compiled = compiler.CompileRoot(derived, new ActivityTreeProjector(structures).Project(derived), rows);
        var child = Assert.Single(compiled.ChildSlots.Single().Activities);
        Assert.Equal(native.NodeId, child.ExecutableNodeId);
        Assert.Equal(NativeSequence.StructureKind, child.Structure!.Kind);
        Assert.Equal(native.NodeId, compiled.Structure!.Payload.Deserialize<BpmnStructure>(Json)!.Elements.Single(element => element.ElementId == "Work").ChildNodeId);
        await Verify(Context(owner, compiled));
        Assert.True(JsonElement.DeepEquals(before, owner.Structure.Payload));
    }

    [Theory]
    [InlineData("conditionExpression")]
    [InlineData("standardLoopCharacteristics")]
    [InlineData("complexGateway")]
    [InlineData("foreign-attribute")]
    [InlineData("foreign-extension")]
    [InlineData("independent-process")]
    [InlineData("unbound-service")]
    [InlineData("invalid-flow")]
    [InlineData("hidden-process")]
    public void Unsupported_or_unrepresented_full_content_receives_source_diagnostics_before_reduction_and_stays_authored(string mutation)
    {
        var source = mutation switch
        {
            "conditionExpression" => Xml.Replace("targetRef=\"End\"/>", "targetRef=\"End\"><b:conditionExpression>secretExpression()</b:conditionExpression></b:sequenceFlow>", StringComparison.Ordinal),
            "standardLoopCharacteristics" => Xml.Replace("<b:task id=\"Work\"/>", "<b:task id=\"Work\"><b:standardLoopCharacteristics/></b:task>", StringComparison.Ordinal),
            "complexGateway" => Xml.Replace("<b:task id=\"Work\"/>", "<b:complexGateway id=\"Work\"/>", StringComparison.Ordinal),
            "foreign-attribute" => Xml.Replace("<b:task id=\"Work\"/>", "<b:task xmlns:v=\"urn:vendor\" id=\"Work\" v:behavior=\"secret-value\"/>", StringComparison.Ordinal),
            "foreign-extension" => Xml.Replace("<b:task id=\"Work\"/>", "<b:task id=\"Work\"><b:extensionElements><v:script xmlns:v=\"urn:vendor\">secret-code</v:script></b:extensionElements></b:task>", StringComparison.Ordinal),
            "independent-process" => Xml.Replace("</b:definitions>", "<b:process id=\"Other\"/></b:definitions>", StringComparison.Ordinal),
            "unbound-service" => Xml.Replace("<b:task id=\"Work\"/>", "<b:serviceTask id=\"Work\"/>", StringComparison.Ordinal),
            "invalid-flow" => Xml.Replace("targetRef=\"End\"", "targetRef=\"Missing\"", StringComparison.Ordinal),
            _ => Xml.Replace("<b:task id=\"Work\"/>", "<b:task id=\"Work\"><b:process id=\"Hidden\"/></b:task>", StringComparison.Ordinal)
        };
        var owner = Owner(source);
        var diagnostics = BpmnDocumentAdmission.Assess(owner);
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic => Assert.Contains("definitions", diagnostic.SourcePath, StringComparison.Ordinal));
        var failure = Assert.Throws<NotSupportedException>(() => new BpmnDocumentStructureHandler().CompileExecutableStructure(owner));
        Assert.Contains("source-owner", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-code", failure.Message, StringComparison.Ordinal);
        Assert.Equal(source, BpmnDocumentStructureHandler.Read(owner).DocumentXml);
        Assert.True(JsonElement.DeepEquals(owner.Structure!.Payload, new BpmnDocumentStructureHandler().ReplaceChildren(owner,
            new BpmnDocumentStructureHandler().ProjectChildren(owner)).Structure!.Payload));
    }

    [Theory]
    [InlineData("misplaced-bounds", "UnsupportedPresentation")]
    [InlineData("model-in-plane", "SemanticContentInPresentation")]
    [InlineData("model-in-vendor-extension", "SemanticContentInPresentation")]
    [InlineData("unknown-presentation", "UnsupportedPresentation")]
    public void Presentation_namespaces_cannot_hide_content_from_execution_admission(string mutation, string expectedCode)
    {
        const string namespaces = "xmlns:d=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\"";
        var xml = Xml.Replace("targetNamespace=\"urn:test\"", namespaces + " targetNamespace=\"urn:test\"", StringComparison.Ordinal);
        xml = mutation switch
        {
            "misplaced-bounds" => xml.Replace("</b:process>", "<dc:Bounds><b:serviceTask id=\"Hidden\"/></dc:Bounds></b:process>", StringComparison.Ordinal),
            "model-in-plane" => xml.Replace("</b:definitions>", "<d:BPMNDiagram id=\"Diagram\"><d:BPMNPlane id=\"Plane\" bpmnElement=\"P\"><b:serviceTask id=\"Hidden\"/></d:BPMNPlane></d:BPMNDiagram></b:definitions>", StringComparison.Ordinal),
            "model-in-vendor-extension" => xml.Replace("</b:definitions>", "<d:BPMNDiagram id=\"Diagram\"><d:BPMNPlane id=\"Plane\" bpmnElement=\"P\"><di:extension><v:metadata xmlns:v=\"urn:vendor\"><b:serviceTask id=\"Hidden\"/></v:metadata></di:extension></d:BPMNPlane></d:BPMNDiagram></b:definitions>", StringComparison.Ordinal),
            _ => xml.Replace("</b:definitions>", "<dc:Unrepresented><b:serviceTask id=\"Hidden\"/></dc:Unrepresented></b:definitions>", StringComparison.Ordinal)
        };
        var owner = Owner(xml);
        Assert.Contains(BpmnDocumentAdmission.Assess(owner), diagnostic => diagnostic.Code == expectedCode);
        Assert.Throws<NotSupportedException>(() => BpmnDocumentAdmission.Lower(owner, "P"));
        Assert.Equal(xml, BpmnDocumentStructureHandler.Read(owner).DocumentXml);
    }

    [Fact]
    public void Legal_DI_vendor_presentation_metadata_and_mixed_documentation_remain_opaque_and_authored()
    {
        const string diagram = """
            <d:BPMNDiagram xmlns:d="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" id="Diagram">
              <d:BPMNPlane id="Plane" bpmnElement="P"><di:extension><v:metadata xmlns:v="urn:vendor"><v:setting>retained</v:setting></v:metadata></di:extension>
                <d:BPMNShape id="Shape" bpmnElement="Work"><dc:Bounds x="100.25" y="50.5" width="100" height="80"/><d:BPMNLabel labelStyle="Style"><dc:Bounds x="100" y="140" width="100" height="20"/></d:BPMNLabel></d:BPMNShape>
                <d:BPMNEdge id="Edge" bpmnElement="First"><di:waypoint x="10" y="20"/><di:waypoint x="100.25" y="50.5"/></d:BPMNEdge>
              </d:BPMNPlane><d:BPMNLabelStyle id="Style"><dc:Font name="Arial" size="12" isBold="true"/></d:BPMNLabelStyle>
            </d:BPMNDiagram>
            """;
        var xml = Xml.Replace("<b:task id=\"Work\"/>", "<b:task id=\"Work\"><b:documentation xml:lang=\"en\">Mixed <h:em xmlns:h=\"http://www.w3.org/1999/xhtml\">documentation</h:em><!--kept--><![CDATA[ <literal> ]]></b:documentation></b:task>", StringComparison.Ordinal)
            .Replace("</b:definitions>", diagram + "</b:definitions>", StringComparison.Ordinal);
        var owner = Owner(xml);
        Assert.Empty(BpmnDocumentAdmission.Assess(owner));
        Assert.Equal(3, BpmnDocumentAdmission.Lower(owner, "P").Payload.Deserialize<BpmnStructure>(Json)!.Elements.Count);
        Assert.Equal(xml, BpmnDocumentStructureHandler.Read(owner).DocumentXml);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Actual_node_compiler_refuses_unsupported_source_including_nested_and_CanStartWorkflow_false(bool nested, bool canStart)
    {
        var owner = Owner(Xml.Replace("<b:task id=\"Work\"/>", "<b:complexGateway id=\"Work\"/>", StringComparison.Ordinal)) with { Inputs = [Literal(canStart)] };
        var root = nested ? new ActivityNode("outer", typeof(NativeSequence).FullName!, [], [], new(NativeSequence.StructureKind,
            NativeSequence.StructureSchemaVersion, JsonSerializer.SerializeToElement(new SequenceAuthoredStructure([owner], []), Json))) : owner;
        var (compiler, structures, rows) = BpmnDocumentStructureHandlerTests.Compiler();
        var failure = Assert.Throws<NotSupportedException>(() => compiler.CompileRoot(root, new ActivityTreeProjector(structures).Project(root), rows));
        Assert.Contains("complexGateway", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draft_admission_reports_unsupported_execution_without_throwing_away_the_complete_draft()
    {
        var owner = Owner(Xml.Replace("<b:task id=\"Work\"/>", "<b:scriptTask id=\"Work\"/>", StringComparison.Ordinal));
        var before = owner.Structure!.Payload.Clone();
        var errors = (await new BpmnDocumentAdmissionValidator(Handlers()).Validate(new Draft(owner), default)).ToArray();
        Assert.Contains(errors, error => error.Type == "Bpmn/ExecutionCapability" && error.Severity == ValidationSeverities.Error && error.Message.Contains("UnboundWork", StringComparison.Ordinal));
        Assert.True(JsonElement.DeepEquals(before, owner.Structure.Payload));
    }

    [Fact]
    public async Task Existing_compilation_contribution_refuses_a_different_catalog_type_or_replaced_graph()
    {
        var owner = Owner();
        var graph = BpmnDocumentAdmission.Lower(owner, "P");
        foreach (var wrongType in new[] { true, false })
        {
            var payload = wrongType ? graph.Payload : JsonSerializer.SerializeToElement(new BpmnStructure([], []), Json);
            var compiled = new ExecutableNode(owner.NodeId, owner.NodeId, wrongType ? "Unrelated.Activity" : BpmnActivityIdentities.ProcessType,
                "1.0.0", "test", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                structure: new(graph.Kind, graph.SchemaVersion, payload));
            await Assert.ThrowsAsync<ArgumentException>(async () => await Verify(Context(owner, compiled)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Semantic_verification_refuses_missing_or_ambiguous_canonical_normalization(int normalizerCount)
    {
        var owner = Owner();
        var (compiler, structures, rows) = BpmnDocumentStructureHandlerTests.Compiler();
        var derived = DerivedSchema1(owner, structures);
        var compiled = compiler.CompileRoot(derived, new ActivityTreeProjector(structures).Project(derived), rows);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Verify(Context(owner, compiled), normalizerCount));
        Assert.Contains("exactly one", error.Message, StringComparison.Ordinal);
        Assert.Equal(Xml, BpmnDocumentStructureHandler.Read(owner).DocumentXml);
    }

    [Theory]
    [InlineData("drop-variables")]
    [InlineData("change-variable-key")]
    [InlineData("change-variable-name")]
    [InlineData("change-variable-type")]
    [InlineData("change-variable-policy")]
    [InlineData("change-variable-default")]
    [InlineData("drop-variable-default")]
    [InlineData("change-conversion-plan")]
    [InlineData("change-transaction")]
    [InlineData("drop-transaction")]
    [InlineData("extra-semantic-field")]
    public async Task Existing_compilation_contribution_checks_all_semantic_fields_after_Core_variable_normalization(string mutation)
    {
        var owner = Owner();
        var source = BpmnDocumentStructureHandler.Read(owner);
        owner = BpmnDocumentScopeProjection.Write(owner, source with
        { Variables = [new("local", "Local", new TypeReference("String"), WellKnownRuntimeDurableValueStorageDrivers.Json, new("initial", "Literal"))] });
        var (compiler, structures, rows) = BpmnDocumentStructureHandlerTests.Compiler();
        var derived = DerivedSchema1(owner, structures);
        var compiled = compiler.CompileRoot(derived, new ActivityTreeProjector(structures).Project(derived), rows);
        // The actual Core normalization is accepted before each isolated semantic mutation.
        await Verify(Context(owner, compiled));
        var payload = JsonNode.Parse(compiled.Structure!.Payload.GetRawText())!.AsObject();
        switch (mutation)
        {
            case "drop-variables": payload["variables"] = new JsonArray(); break;
            case "change-variable-key": payload["variables"]![0]!["variableKey"] = "different"; break;
            case "change-variable-name": payload["variables"]![0]!["name"] = "Different"; break;
            case "change-variable-type": payload["variables"]![0]!["type"]!["alias"] = "Int32"; break;
            case "change-variable-policy": payload["variables"]![0]!["policy"]!["isSensitive"] = true; break;
            case "change-variable-default": payload["variables"]![0]!["initialBinding"]!["literal"]!["inlineValue"] = "different"; break;
            case "drop-variable-default": payload["variables"]![0]!["initialBinding"] = null; break;
            case "change-conversion-plan": payload["variables"]![0]!["initialBinding"]!["conversionPlan"] = null; break;
            case "change-transaction": payload["isTransaction"] = true; break;
            case "drop-transaction": payload.Remove("isTransaction"); break;
            case "extra-semantic-field": payload["unexpectedMeaning"] = true; break;
        }
        var changed = new ExecutableNode(compiled.ExecutableNodeId, compiled.AuthoredActivityId, compiled.ActivityType, compiled.ActivityTypeVersion,
            compiled.DescriptorType, compiled.DescriptorPayload, compiled.InputBindings, compiled.Metadata, compiled.ChildSlots,
            new(compiled.Structure.Kind, compiled.Structure.SchemaVersion, JsonSerializer.SerializeToElement(payload, Json)));
        await Assert.ThrowsAsync<ArgumentException>(async () => await Verify(Context(owner, changed)));
        Assert.Equal("initial", JsonSerializer.SerializeToElement(BpmnDocumentStructureHandler.Read(owner).Variables.Single().Default!.Value, Json).GetString());
    }

    [Fact]
    public async Task Genuine_derived_scope_view_cannot_be_published_as_a_physical_source_root()
    {
        var xml = Xml.Replace("<b:task id=\"Work\"/>", "<b:subProcess id=\"Work\"><b:startEvent id=\"InnerStart\"/></b:subProcess>", StringComparison.Ordinal);
        var owner = Owner(xml, [new("Work", "scope-work", typeof(BpmnProcess).FullName!, [Literal(false)], [], [])]);
        var view = Assert.Single(new BpmnDocumentStructureHandler().ProjectChildren(owner).Single().Activities);
        Assert.Throws<NotSupportedException>(() => new BpmnDocumentScopeViewHandler().CompileExecutableStructure(view));
        var graph = BpmnDocumentAdmission.Lower(owner, "Work");
        var compiled = new ExecutableNode(view.NodeId, view.NodeId, BpmnActivityIdentities.ProcessType, "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), structure: new(graph.Kind, graph.SchemaVersion, graph.Payload));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Verify(Context(view, compiled)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Registered_source2_compilation_remains_closed_for_supported_source_until_every_real_route_is_qualified(bool nested, bool canStart)
    {
        var owner = Owner() with { Inputs = [Literal(canStart)] };
        Assert.Empty(BpmnDocumentAdmission.Assess(owner));
        var root = nested ? new ActivityNode("outer", typeof(NativeSequence).FullName!, [], [], new(NativeSequence.StructureKind,
            NativeSequence.StructureSchemaVersion, JsonSerializer.SerializeToElement(new SequenceAuthoredStructure([owner], []), Json))) : owner;
        var (compiler, structures, rows) = BpmnDocumentStructureHandlerTests.Compiler();
        var failure = Assert.Throws<NotSupportedException>(() => compiler.CompileRoot(root, new ActivityTreeProjector(structures).Project(root), rows));
        Assert.Contains("all real", failure.Message, StringComparison.Ordinal);
    }

    // Test-only compatibility projection exercises the existing schema1 compiler's normalization.
    // It is not registered as source2 execution authority or persisted as another authored graph.
    private static ActivityNode DerivedSchema1(ActivityNode node, IActivityStructureService structures)
    {
        if (node.Structure is { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion } || BpmnDocumentScopeProjection.IsView(node))
        {
            var view = BpmnDocumentScopeProjection.IsView(node) ? BpmnDocumentScopeProjection.ReadView(node) : null;
            var owner = view?.Owner ?? node;
            var scope = view?.ScopeElementId ?? BpmnDocumentStructureHandler.Read(owner).ProcessId;
            var graph = BpmnDocumentAdmission.Lower(owner, scope).Payload.Deserialize<BpmnStructure>(Json)!;
            var children = structures.ProjectChildren(node).Single().Activities.Select(child => DerivedSchema1(child, structures)).ToArray();
            return node with { Structure = new(BpmnActivityIdentities.StructureKind, BpmnActivityIdentities.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnAuthoredStructure(children, graph.Elements, graph.SequenceFlows, variables: graph.Variables, isTransaction: graph.IsTransaction), Json)) };
        }
        var slots = structures.ProjectChildren(node);
        return slots.Count == 0 ? node : structures.ReplaceChildren(node, slots.Select(slot => new ActivityChildProjection(slot.Name,
            slot.Activities.Select(child => DerivedSchema1(child, structures)).ToArray())).ToArray());
    }

    private static ActivityNode Owner(string xml = Xml, BpmnDocumentScopeHeader[]? headers = null) => new("source-owner", typeof(BpmnProcess).FullName!,
        [Literal(false)], [], new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion,
            JsonSerializer.SerializeToElement(new BpmnAuthoredDocument(xml, "P", [], [], []) { ScopeHeaders = headers ?? [] }, Json)));
    private static ArgumentState Literal(bool value) => new(BpmnActivityIdentities.CanStartWorkflowInput, new(value, "Literal"), null, null, null, null);
    private static IActivityStructureHandler[] Handlers() => [new BpmnStructureHandler(), new BpmnDocumentStructureHandler(), new BpmnDocumentScopeViewHandler()];
    private static async ValueTask<ExecutableCompilationContribution> Verify(ExecutableCompilationContext context, int normalizerCount = 1)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RuntimeInputBindingCompiler(ClrConstruction.RegistryFor(typeof(BpmnProcess), typeof(NativeSequence))));
        new WorkflowsPublishingFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<IExecutableStructureNormalizer>();
        return await new BpmnDocumentCompilationSource(Handlers(), Enumerable.Repeat(normalizer, normalizerCount))
            .GetContributionAsync(context);
    }

    private static ExecutableCompilationContext Context(ActivityNode owner, ExecutableNode compiled) => new(
        new("version", WorkflowExecutableReferenceScope.Published, DateTimeOffset.UnixEpoch, null, null, "test"),
        new("definition", "version", "1.0.0", new([], owner, [], [], null), "WorkflowDefinition", "definition", "1.0.0"), compiled);
    private sealed class Draft(ActivityNode root) : IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "definition";
        public WorkflowDefinitionState State => new([], root, [], [], null);
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
