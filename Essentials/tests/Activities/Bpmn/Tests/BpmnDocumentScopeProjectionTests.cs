using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Handlers;
using Elsa.Workflows.Publishing.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Process = Elsa.Activities.Bpmn.Core.BpmnActivityIdentities;
using NativeSequence = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDocumentScopeProjectionTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = BpmnDocumentStructureHandler.SerializerOptions;
    private readonly ServiceProvider _provider;
    private readonly DefaultActivityStructureService _structures;

    public BpmnDocumentScopeProjectionTests()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new EventsFeature().ConfigureServices(services);
        services.AddEventHandler<DraftValidating, ExecuteValidations>();
        _provider = services.BuildServiceProvider();
        _structures = new(_provider.GetServices<IActivityStructureHandler>());
    }

    public void Dispose() => _provider.Dispose();

    private const string Xml = """
        <b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:v="urn:vendor" targetNamespace="urn:sample">
          <!-- source is retained once, including opaque XML -->
          <b:message id="Message"/>
          <b:process id="P1"><b:extensionElements><v:config ref="scope-a">opaque</v:config></b:extensionElements>
            <b:subProcess id="A"><b:task id="TaskA"/></b:subProcess>
            <b:transaction id="B"><b:task id="TaskB"/></b:transaction>
            <b:subProcess id="Event" triggeredByEvent="true"><b:startEvent id="EventStart"><b:messageEventDefinition messageRef="Message"/></b:startEvent><b:task id="EventTask"/></b:subProcess>
            <b:subProcess id="Native"/>
          </b:process>
          <b:process id="P2"><b:task id="OtherTask"/></b:process>
        </b:definitions>
        """;

    [Fact]
    public void Projection_is_pure_and_no_op_replacement_stores_one_XML_document_and_compact_headers_only()
    {
        var root = Owner();
        var snapshot = root.Structure!.Payload.Clone();
        var first = _structures.ProjectChildren(root);
        var second = _structures.ProjectChildren(root);
        Assert.Equal(3, first.Single().Activities.Count(BpmnDocumentScopeProjection.IsView));
        AssertJson(JsonSerializer.SerializeToElement(first, Json), JsonSerializer.SerializeToElement(second, Json));
        var replaced = _structures.ReplaceChildren(root, first);
        AssertJson(snapshot, replaced.Structure!.Payload);
        Assert.Equal(Xml, replaced.Structure.Payload.GetProperty("documentXml").GetString());
        var storage = JsonSerializer.SerializeToElement(replaced, Json);
        Assert.Equal(1, CountProperties(storage, "documentXml"));
        Assert.DoesNotContain(BpmnDocumentScopeView.Kind, storage.GetRawText(), StringComparison.Ordinal);
        Assert.All(replaced.Structure.Payload.GetProperty("scopeHeaders").EnumerateArray(), header => Assert.False(header.TryGetProperty("structure", out _)));
        AssertJson(snapshot, root.Structure.Payload);
    }

    [Fact]
    public void Sibling_scope_edits_compose_without_a_whole_snapshot_winner_and_repeated_folding_is_stable()
    {
        var root = Owner();
        var children = Children(root);
        var a = View(children, "A");
        var b = View(children, "B");
        var editedA = EditChild(a, "a-native", "changed-a");
        var editedB = EditChild(b, "b-native", "changed-b");
        var combined = _structures.ReplaceChildren(root, [new(Process.ActivitiesSlotName, children.Select(child =>
            child.NodeId == a.NodeId ? editedA : child.NodeId == b.NodeId ? editedB : child).ToArray())]);
        Assert.Equal("changed-a", Value(Document(combined).Activities.Single(child => child.NodeId == "a-native")));
        Assert.Equal("changed-b", Value(Document(combined).Activities.Single(child => child.NodeId == "b-native")));
        Assert.Equal(Xml, Document(combined).DocumentXml);
        AssertJson(root.Structure!.Payload.GetProperty("futureMetadata"), combined.Structure!.Payload.GetProperty("futureMetadata"));
        AssertJson(root.Structure.Payload.GetProperty("scopeHeaders"), combined.Structure.Payload.GetProperty("scopeHeaders"));
        AssertJson(combined.Structure.Payload, _structures.ReplaceChildren(combined, _structures.ProjectChildren(combined)).Structure!.Payload);
    }

    [Fact]
    public void Scope_header_configuration_is_folded_without_copying_its_transient_structure()
    {
        var root = Owner();
        var children = Children(root);
        var a = View(children, "A");
        var context = BpmnDocumentScopeProjection.ReadView(a);
        var edited = a with
        {
            ActivityVersionId = "exact-new-process-version", Inputs = [Literal("setting", "header-change")],
            Structure = new(BpmnDocumentScopeView.Kind, BpmnDocumentScopeView.SchemaVersion,
                JsonSerializer.SerializeToElement(context with { Variables = [Variable("new-local")] }, Json))
        };
        var updated = _structures.ReplaceChildren(root, [new(Process.ActivitiesSlotName, children.Select(child => child.NodeId == a.NodeId ? edited : child).ToArray())]);
        var header = Document(updated).ScopeHeaders.Single(item => item.ScopeElementId == "A");
        Assert.Equal("exact-new-process-version", header.ActivityVersionId);
        Assert.Equal("header-change", header.Inputs.Single().Value.Value is JsonElement json ? json.GetString() : header.Inputs.Single().Value.Value);
        Assert.Equal("new-local", header.Variables.Single().ReferenceKey);
        Assert.Equal("scope-a", header.AdditionalProperties!["futureMetadata"].GetProperty("text").GetString());
        Assert.Equal(1, CountProperties(JsonSerializer.SerializeToElement(updated, Json), "documentXml"));
    }

    [Fact]
    public void Deep_standard_scope_edits_fold_through_each_owner_while_sibling_edits_compose()
    {
        var original = Owner();
        var document = Document(original);
        var xml = Xml.Replace("<b:task id=\"TaskA\"/>", "<b:subProcess id=\"Inner\"><b:task id=\"TaskA\"/></b:subProcess>", StringComparison.Ordinal);
        var root = BpmnDocumentScopeProjection.Write(original, document with
        {
            DocumentXml = xml,
            ScopeHeaders = [.. document.ScopeHeaders, new("Inner", "scope-inner", "bpmn-exact-version", [], [], [Variable("inner-local")])]
        });
        var children = Children(root);
        var a = View(children, "A");
        var inner = View(Children(a), "Inner");
        var editedInner = EditChild(inner, "a-native", "deep-change");
        var editedA = _structures.ReplaceChildren(a, [new(Process.ActivitiesSlotName, [editedInner])]);
        var b = View(children, "B");
        var editedB = EditChild(b, "b-native", "sibling-change");
        var combined = _structures.ReplaceChildren(root, [new(Process.ActivitiesSlotName, children.Select(child =>
            child.NodeId == a.NodeId ? editedA : child.NodeId == b.NodeId ? editedB : child).ToArray())]);
        Assert.Equal("deep-change", Value(Document(combined).Activities.Single(child => child.NodeId == "a-native")));
        Assert.Equal("sibling-change", Value(Document(combined).Activities.Single(child => child.NodeId == "b-native")));
        Assert.Equal(xml, Document(combined).DocumentXml);
        var visibility = new ScopedVariableResolver(_structures).Resolve([Variable("workflow-var")], combined, 100);
        Assert.Equal(new[] { "inner-local", "a-local", "root", "workflow-var" }, visibility.GetVisibleVariables("a-native").Select(item => item.Variable.ReferenceKey));
        Assert.Equal(1, CountProperties(JsonSerializer.SerializeToElement(combined, Json), "documentXml"));
        AssertJson(combined.Structure!.Payload, _structures.ReplaceChildren(combined, _structures.ProjectChildren(combined)).Structure!.Payload);
    }

    [Fact]
    public void Standard_scope_configuration_and_a_native_Sequence_body_retain_both_intents_without_implicit_selection()
    {
        var original = Owner();
        var document = Document(original);
        var root = BpmnDocumentScopeProjection.Write(original, document with
        {
            ScopeHeaders = [.. document.ScopeHeaders, new("Native", "scope-native", "bpmn-exact-version", [], [], [Variable("standard-body-local")])]
        });
        var children = Children(root);
        Assert.Contains(children, child => child.NodeId == "native-sequence");
        Assert.Equal("scope-native", View(children, "Native").NodeId);
        AssertJson(root.Structure!.Payload, _structures.ReplaceChildren(root, _structures.ProjectChildren(root)).Structure!.Payload);
        Assert.Throws<NotSupportedException>(() => _structures.CompileExecutableStructure(root));
    }

    [Fact]
    public void Event_listener_stays_in_enclosing_scope_and_native_Sequence_keeps_its_real_slots_and_scopes()
    {
        var root = Owner();
        var children = Children(root);
        Assert.Contains(children, child => child.NodeId == "event-listener");
        var eventScope = View(children, "Event");
        Assert.DoesNotContain(Children(eventScope), child => child.NodeId == "event-listener");
        Assert.Equal("event-native", Assert.Single(Children(eventScope)).NodeId);
        var native = children.Single(child => child.NodeId == "native-sequence");
        Assert.Equal(NativeSequence.StructureKind, native.Structure!.Kind);
        Assert.Equal(NativeSequence.ActivitiesSlotName, _structures.ProjectChildren(native).Single().Name);
        Assert.Equal("native-leaf", Assert.Single(Children(native)).NodeId);
        var visibility = new ScopedVariableResolver(_structures).Resolve([Variable("workflow-var")], root, 100);
        Assert.Equal(new[] { "root", "workflow-var" }, visibility.GetVisibleVariables("event-listener").Select(item => item.Variable.ReferenceKey));
        Assert.Equal(new[] { "event-local", "root", "workflow-var" }, visibility.GetVisibleVariables("event-native").Select(item => item.Variable.ReferenceKey));
        Assert.Equal(new[] { "native-local", "root", "workflow-var" }, visibility.GetVisibleVariables("native-leaf").Select(item => item.Variable.ReferenceKey));
        var projection = new ActivityTreeProjector(_structures).Project(root);
        ActivityTreeProjector.Validate(projection.Nodes);
        Assert.Equal(projection.Nodes.Count, projection.Nodes.Select(node => node.NodeId).Distinct().Count());
    }

    [Fact]
    public void Existing_scoped_reference_remapper_folds_all_selected_standard_and_native_scopes_without_XML_loss()
    {
        var root = Owner();
        var projection = new ActivityTreeProjector(_structures).Project(root);
        var map = projection.Nodes.ToDictionary(node => node.NodeId, node => node.NodeId + "-copy");
        var copy = new ScopedVariableReferenceRemapper(_structures).Remap(root, map);
        Assert.Equal("owner-copy", copy.NodeId);
        Assert.Equal(Xml, Document(copy).DocumentXml);
        Assert.DoesNotContain(BpmnDocumentScopeView.Kind, JsonSerializer.Serialize(copy, Json), StringComparison.Ordinal);
        var copied = new ActivityTreeProjector(_structures).Project(copy);
        ActivityTreeProjector.Validate(copied.Nodes);
        Assert.Equal(map.Values.Order(), copied.Nodes.Select(node => node.NodeId).Order());
        var native = Document(copy).Activities.Single(child => child.NodeId == "a-native-copy");
        Assert.True(VariableReference.TryParse(native.Outputs.Single().Value.Value, out var reference));
        Assert.Equal("scope-a-copy", reference!.DeclaringScopeId);
        var listener = Document(copy).Activities.Single(child => child.NodeId == "event-listener-copy");
        Assert.True(VariableReference.TryParse(listener.Outputs.Single().Value.Value, out var listenerReference));
        Assert.Equal("owner-copy", listenerReference!.DeclaringScopeId);
        var binding = Document(copy).Bindings.Single(item => item.ElementId == "Event");
        Assert.Equal("event-listener-copy", binding.ListenerNodeId);
        Assert.Equal("scope-a-copy", Document(copy).ScopeHeaders.Single(item => item.ScopeElementId == "A").NodeId);
        Assert.Equal("scope-a", Document(copy).ScopeHeaders.Single(item => item.ScopeElementId == "A").AdditionalProperties!["futureMetadata"].GetProperty("text").GetString());
    }

    [Fact]
    public void Independent_process_query_view_isolates_variables_and_retains_other_native_configuration_without_persisting_another_source()
    {
        var root = Owner(otherProcess: true);
        Assert.DoesNotContain(new ActivityTreeProjector(_structures).Project(root).Nodes, node => node.NodeId == "other-native");
        var other = BpmnDocumentScopeProjection.CreateProcessView(root, "P2");
        Assert.Equal("scope-other", other.NodeId);
        Assert.Equal("other-native", Assert.Single(Children(other)).NodeId);
        var visibility = new ScopedVariableResolver(_structures).Resolve([Variable("workflow-var")], other, 100);
        Assert.Equal(new[] { "other-local", "workflow-var" }, visibility.GetVisibleVariables("other-native").Select(item => item.Variable.ReferenceKey));
        Assert.False(visibility.IsReferenceVisible("other-native", new VariableReference("root", "owner")));
        Assert.Equal(Xml, Document(BpmnDocumentScopeProjection.ReadView(other).Owner).DocumentXml);
        AssertJson(root.Structure!.Payload, BpmnDocumentScopeProjection.ReadView(other).Owner.Structure!.Payload);
        Assert.Throws<NotSupportedException>(() => new ScopedVariableReferenceRemapper(_structures).Remap(root,
            new Dictionary<string, string> { ["a-native"] = "partial-copy" }));
    }

    [Fact]
    public void Selected_process_query_view_uses_the_actual_owner_identity_configuration_and_root_variables()
    {
        var owner = Owner(otherProcess: true) with { Inputs = [Literal("setting", "root-argument")], Outputs = [Reference("owner")] };
        var before = owner.Structure!.Payload.GetRawText();
        var query = BpmnDocumentScopeProjection.CreateProcessView(owner, "P1");
        Assert.Equal(BpmnDocumentScopeView.Kind, query.Structure!.Kind);
        Assert.Equal(owner.NodeId, query.NodeId);
        Assert.Equal(owner.ActivityVersionId, query.ActivityVersionId);
        Assert.Equal(owner.Inputs, query.Inputs);
        Assert.Equal(owner.Outputs, query.Outputs);
        Assert.Equal("root", Assert.Single(_structures.ProjectScopedVariables(query)).ReferenceKey);
        var visibility = new ScopedVariableResolver(_structures).Resolve([Variable("workflow-var")], query, 100);
        Assert.Equal(new[] { "a-local", "root", "workflow-var" }, visibility.GetVisibleVariables("a-native").Select(item => item.Variable.ReferenceKey));
        Assert.DoesNotContain("other-native", new ActivityTreeProjector(_structures).Project(query).Nodes.Select(node => node.NodeId));
        Assert.Equal(before, BpmnDocumentScopeProjection.ReadView(query).Owner.Structure!.Payload.GetRawText());
    }

    [Theory]
    [InlineData("P1", "root")]
    [InlineData("P2", "other-local")]
    [InlineData("A", "a-local")]
    public void Query_variables_and_children_are_derived_from_canonical_owner_instead_of_forged_projection_extras(string scope, string variable)
    {
        var owner = Owner(otherProcess: true);
        var query = scope == "A" ? View(Children(owner), scope) : BpmnDocumentScopeProjection.CreateProcessView(owner, scope);
        var payload = JsonSerializer.SerializeToNode(BpmnDocumentScopeProjection.ReadView(query), Json)!.AsObject();
        payload["variables"] = JsonSerializer.SerializeToNode(new[] { Variable("forged-variable") }, Json);
        payload["activities"] = JsonSerializer.SerializeToNode(new[] { new ActivityNode("forged-child", "forged-version", [], []) }, Json);
        var forged = query with { Structure = new(BpmnDocumentScopeView.Kind, BpmnDocumentScopeView.SchemaVersion, JsonSerializer.SerializeToElement(payload, Json)) };
        Assert.Equal(variable, Assert.Single(_structures.ProjectScopedVariables(forged)).ReferenceKey);
        Assert.DoesNotContain(new ActivityTreeProjector(_structures).Project(forged).Nodes, node => node.NodeId == "forged-child");
        AssertJson(owner.Structure!.Payload, BpmnDocumentScopeProjection.ReadView(forged).Owner.Structure!.Payload);
    }

    [Theory]
    [InlineData("node-id")]
    [InlineData("version")]
    [InlineData("inputs")]
    [InlineData("outputs")]
    [InlineData("placement")]
    [InlineData("view-schema")]
    [InlineData("owner-schema")]
    public void Selected_process_query_refuses_identity_configuration_or_schema_mismatches(string mutation)
    {
        var owner = Owner();
        var query = BpmnDocumentScopeProjection.CreateProcessView(owner, "P1");
        query = mutation switch
        {
            "node-id" => query with { NodeId = "invented-root" },
            "version" => query with { ActivityVersionId = "invented-version" },
            "inputs" => query with { Inputs = [Literal("setting", "unowned")] },
            "outputs" => query with { Outputs = [Reference("foreign")] },
            "placement" => query with { ExecutionPlacement = ActivityExecutionPlacement.Background },
            "view-schema" => query with { Structure = new(BpmnDocumentScopeView.Kind, "99", query.Structure!.Payload) },
            _ => query with { Structure = new(BpmnDocumentScopeView.Kind, BpmnDocumentScopeView.SchemaVersion,
                JsonSerializer.SerializeToElement(BpmnDocumentScopeProjection.ReadView(query) with { Owner = owner with
                { Structure = new(Process.StructureKind, "1.0.0", owner.Structure!.Payload) } }, Json)) }
        };
        // The module validates its exact view format. Generic projection deliberately keeps
        // unknown structure versions opaque rather than dispatching a different version's handler.
        Assert.Throws<InvalidOperationException>(() => BpmnDocumentScopeProjection.ReadView(query));
    }

    [Fact]
    public async Task Selected_process_query_cannot_fold_persist_or_compile_as_an_authored_root()
    {
        var owner = Owner();
        var query = BpmnDocumentScopeProjection.CreateProcessView(owner, "P1");
        Assert.Throws<InvalidOperationException>(() => _structures.ReplaceChildren(query, _structures.ProjectChildren(query)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new BpmnScopeViewSourceValidator(_provider.GetServices<IActivityStructureHandler>()).Validate(new Draft(query), default));
        var context = new ActivityStructureCompilationContext(owner, new Dictionary<string, ResolvedStructureInvocation>(),
            new Dictionary<string, IReadOnlyCollection<ActivityChildProjection>>());
        Assert.Throws<NotSupportedException>(() => new BpmnDocumentScopeViewHandler().CompileExecutableStructure(query, context));
        Assert.Equal(Xml, Document(owner).DocumentXml);
    }

    [Fact]
    public async Task Unknown_scope_view_version_remains_forbidden_physical_source_and_contextual_compilation()
    {
        var owner = Owner();
        var view = BpmnDocumentScopeProjection.CreateProcessView(owner, "P1");
        var unknown = view with { Structure = new(BpmnDocumentScopeView.Kind, "99", view.Structure!.Payload) };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new BpmnScopeViewSourceValidator(_provider.GetServices<IActivityStructureHandler>()).Validate(new Draft(unknown), default));
        var context = new ActivityStructureCompilationContext(unknown, new Dictionary<string, ResolvedStructureInvocation>(),
            new Dictionary<string, IReadOnlyCollection<ActivityChildProjection>>());
        Assert.Throws<NotSupportedException>(() => _structures.CompileExecutableStructure(unknown, context));
    }

    [Theory]
    [InlineData("foreign-native")]
    [InlineData("foreign-metadata")]
    [InlineData("XML")]
    [InlineData("wrong-scope")]
    public void Malicious_or_stale_scope_snapshots_refuse_foreign_mutations_atomically(string mutation)
    {
        var root = Owner();
        var children = Children(root);
        var a = View(children, "A");
        var view = BpmnDocumentScopeProjection.ReadView(a);
        var original = root.Structure!.Payload.GetRawText();
        var payload = JsonNode.Parse(original)!.AsObject();
        switch (mutation)
        {
            case "foreign-native": payload["activities"]![1]!["activityVersionId"] = "foreign-changed"; break;
            case "foreign-metadata": payload["futureMetadata"] = "foreign-changed"; break;
            case "XML": payload["documentXml"] = Xml.Replace("opaque", "changed", StringComparison.Ordinal); break;
            case "wrong-scope": break;
        }
        var contextOwner = root with { Structure = new(Process.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(payload, Json)) };
        var forged = a with { Structure = new(BpmnDocumentScopeView.Kind, BpmnDocumentScopeView.SchemaVersion,
            JsonSerializer.SerializeToElement(view with { Owner = contextOwner, ScopeElementId = mutation == "wrong-scope" ? "B" : "A" }, Json)) };
        Assert.Throws<InvalidOperationException>(() => _structures.ReplaceChildren(root,
            [new(Process.ActivitiesSlotName, children.Select(child => child.NodeId == a.NodeId ? forged : child).ToArray())]));
        Assert.Equal(original, root.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData("structure")]
    [InlineData("duplicate-node")]
    [InlineData("root-identity")]
    [InlineData("missing-ancestor")]
    [InlineData("documentXml")]
    [InlineData("processId")]
    [InlineData("activities")]
    [InlineData("bindings")]
    [InlineData("scopeHeaders")]
    public void Invalid_physical_scope_headers_refuse(string mutation)
    {
        var root = Owner();
        var payload = JsonNode.Parse(root.Structure!.Payload.GetRawText())!.AsObject();
        switch (mutation)
        {
            case "structure": payload["scopeHeaders"]![0]!["structure"] = JsonSerializer.SerializeToNode(root.Structure, Json); break;
            case "duplicate-node": payload["scopeHeaders"]![0]!["nodeId"] = "a-native"; break;
            case "root-identity": payload["scopeHeaders"]![0]!["nodeId"] = "owner"; break;
            case "missing-ancestor": ((JsonArray)payload["scopeHeaders"]!).RemoveAt(0); break;
            case "documentXml": case "processId": case "activities": case "bindings": case "scopeHeaders":
                payload["scopeHeaders"]![0]![mutation] = new JsonArray(); break;
        }
        var invalid = root with { Structure = new(Process.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(payload, Json)) };
        Assert.Throws<InvalidOperationException>(() => _structures.ProjectChildren(invalid));
    }

    [Theory]
    [InlineData("vendor")]
    [InlineData("extension")]
    [InlineData("standard")]
    public void Configured_scopes_require_a_continuous_semantic_scope_chain_but_unconfigured_opaque_XML_survives(string wrapper)
    {
        var root = Owner();
        var source = Document(root);
        var body = "<b:subProcess id=\"Hidden\"><b:task id=\"HiddenTask\"/></b:subProcess>";
        var fragment = wrapper switch
        {
            "vendor" => $"<b:extensionElements><v:opaque>{body}</v:opaque></b:extensionElements>",
            "extension" => $"<b:extensionElements>{body}</b:extensionElements>",
            _ => $"<b:dataObject id=\"OpaqueData\">{body}</b:dataObject>"
        };
        var xml = Xml.Replace("<b:subProcess id=\"A\">", fragment + "<b:subProcess id=\"A\">", StringComparison.Ordinal);
        var unconfigured = BpmnDocumentScopeProjection.Write(root, source with { DocumentXml = xml });
        Assert.Equal(xml, Document(unconfigured).DocumentXml);
        AssertJson(unconfigured.Structure!.Payload, _structures.ReplaceChildren(unconfigured, _structures.ProjectChildren(unconfigured)).Structure!.Payload);
        var hidden = new ActivityNode("hidden-native", "native-exact-version", [], []);
        var configured = source with
        {
            DocumentXml = xml, Activities = [.. source.Activities, hidden], Bindings = [.. source.Bindings, new("HiddenTask", hidden.NodeId)],
            ScopeHeaders = [.. source.ScopeHeaders, new("Hidden", "hidden-scope", "bpmn-exact-version", [], [], [])]
        };
        var invalid = root with { Structure = new(Process.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(configured, Json)) };
        Assert.Throws<InvalidOperationException>(() => _structures.ProjectChildren(invalid));
    }

    [Theory]
    [InlineData("A\u00b7B")]
    [InlineData("A\u203fB")]
    [InlineData("A\u2040B")]
    [InlineData("xmlns")]
    public void Standard_NCName_identities_are_retained_without_renaming(string id)
    {
        var original = Owner();
        var source = Document(original);
        var xml = source.DocumentXml.Replace("\"Message\"", $"\"{id}\"", StringComparison.Ordinal);
        var root = BpmnDocumentScopeProjection.Write(original, source with { DocumentXml = xml });
        Assert.Equal(xml, Document(root).DocumentXml);
        AssertJson(root.Structure!.Payload, _structures.ReplaceChildren(root, _structures.ProjectChildren(root)).Structure!.Payload);
    }

    [Fact]
    public async Task Physical_source_validation_refuses_even_a_genuine_derived_view_as_a_root_or_native_record()
    {
        var root = Owner();
        var view = View(Children(root), "A");
        using var scope = _provider.CreateScope();
        var validator = Assert.Single(scope.ServiceProvider.GetServices<IDraftValidator>().OfType<BpmnScopeViewSourceValidator>());
        Assert.Empty(await validator.Validate(new Draft(root), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.Validate(new Draft(view), CancellationToken.None));
        var source = Document(root);
        var forged = root with { Structure = new(Process.StructureKind, BpmnAuthoredDocument.SchemaVersion,
            JsonSerializer.SerializeToElement(source with { Activities = [view, .. source.Activities] }, Json)) };
        Assert.Throws<InvalidOperationException>(() => _structures.ProjectChildren(forged));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.Validate(new Draft(forged), CancellationToken.None));
        var sequence = source.Activities.Single(child => child.NodeId == "native-sequence");
        var nested = _structures.ReplaceChildren(sequence, [new(NativeSequence.ActivitiesSlotName, [view])]);
        var nestedSource = source with { Activities = source.Activities.Select(child => child.NodeId == sequence.NodeId ? nested : child).ToArray() };
        var nestedRoot = root with { Structure = new(Process.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(nestedSource, Json)) };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.Validate(new Draft(nestedRoot), CancellationToken.None));
        Assert.Throws<NotSupportedException>(() => _structures.CompileExecutableStructure(view));
        Assert.Throws<NotSupportedException>(() => _structures.CompileExecutableStructure(root));
    }

    [Fact]
    public async Task Ordinary_literal_and_opaque_metadata_kind_strings_are_not_activity_structures()
    {
        var literal = JsonSerializer.SerializeToElement(new { kind = BpmnDocumentScopeView.Kind, structure = new { kind = BpmnDocumentScopeView.Kind } }, Json);
        var ordinary = new ActivityNode("inline", "inline-version", [new("json", new(literal, "Literal"), null, null, null, null)], []);
        using var scope = _provider.CreateScope();
        var validator = Assert.Single(scope.ServiceProvider.GetServices<IDraftValidator>().OfType<BpmnScopeViewSourceValidator>());
        Assert.Empty(await validator.Validate(new Draft(ordinary), CancellationToken.None));
        var root = Owner();
        var document = Document(root);
        var configured = document.Activities.First() with { Inputs = ordinary.Inputs };
        var edited = BpmnDocumentScopeProjection.Write(root, document with
        {
            Activities = [configured, .. document.Activities.Skip(1)],
            AdditionalProperties = new() { ["opaque"] = literal }
        });
        Assert.Empty(await validator.Validate(new Draft(edited), CancellationToken.None));
        var replaced = _structures.ReplaceChildren(edited, _structures.ProjectChildren(edited));
        AssertJson(literal, replaced.Structure!.Payload.GetProperty("opaque"));
        AssertJson(literal, (JsonElement)Document(replaced).Activities.First().Inputs.Single().Value.Value!);
    }

    [Fact]
    public async Task Actual_inline_validation_gate_rejects_a_physical_view_and_returns_execution_diagnostics_for_retained_advanced_source()
    {
        using var scope = _provider.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>();
        var errors = await gate.DeriveValidationErrorsAsync(new Draft(Owner()), CancellationToken.None);
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Equal("Bpmn/ExecutionCapability", error.Type));
        var view = View(Children(Owner()), "A");
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.DeriveValidationErrorsAsync(new Draft(view), CancellationToken.None));
    }

    private ActivityNode EditChild(ActivityNode scope, string id, string value) => _structures.ReplaceChildren(scope,
        [new(Process.ActivitiesSlotName, Children(scope).Select(child => child.NodeId == id ? child with { Inputs = [Literal("setting", value)] } : child).ToArray())]);
    private ActivityNode[] Children(ActivityNode node) => _structures.ProjectChildren(node).Single().Activities.ToArray();
    private static ActivityNode View(IEnumerable<ActivityNode> nodes, string scopeId) => nodes.Single(node => BpmnDocumentScopeProjection.IsView(node) && BpmnDocumentScopeProjection.ReadView(node).ScopeElementId == scopeId);
    private static BpmnAuthoredDocument Document(ActivityNode root) => BpmnDocumentStructureHandler.Read(root);
    private static string? Value(ActivityNode node) => ((JsonElement)node.Inputs.Single().Value.Value!).GetString();
    private static VariableDefinition Variable(string id) => new(id, id, new TypeReference("String"), null, null);
    private static ArgumentState Literal(string key, string value) => new(key, new(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null);
    private static ArgumentState Reference(string scope) => new("target", new(new VariableReference("value", scope), "Variable"), null, null, null, null);
    private static void AssertJson(JsonElement left, JsonElement right) => Assert.True(JsonElement.DeepEquals(left, right));

    private static ActivityNode Owner(bool otherProcess = false)
    {
        ActivityNode Leaf(string id, string scope) => new(id, "native-exact-version", [Literal("setting", id)], [Reference(scope)],
            new("vendor.native", "7", JsonSerializer.SerializeToElement(new { value = "scope-a", nested = new { listener = true } }, Json)));
        var leaf = Leaf("native-leaf", "native-sequence");
        var native = new ActivityNode("native-sequence", "sequence-exact-version", [], [],
            new(NativeSequence.StructureKind, NativeSequence.StructureSchemaVersion, JsonSerializer.SerializeToElement(new SequenceAuthoredStructure([leaf], [Variable("native-local")]), Json)));
        var activities = new List<ActivityNode> { Leaf("a-native", "scope-a"), Leaf("b-native", "scope-b"), Leaf("event-native", "scope-event"), Leaf("event-listener", "owner"), native };
        var bindings = new List<BpmnDocumentBinding> { new("TaskA", "a-native"), new("TaskB", "b-native"), new("EventTask", "event-native"), new("Event", ListenerNodeId: "event-listener"), new("Native", "native-sequence") };
        BpmnDocumentScopeHeader Header(string xmlId, string nodeId, string variable) => new(xmlId, nodeId, "bpmn-exact-version", [], [], [Variable(variable)])
        { AdditionalProperties = new() { ["futureMetadata"] = JsonSerializer.SerializeToElement(new { text = "scope-a", flags = new[] { 1, 2 } }, Json) } };
        var headers = new List<BpmnDocumentScopeHeader> { Header("A", "scope-a", "a-local"), Header("B", "scope-b", "b-local"), Header("Event", "scope-event", "event-local") };
        if (otherProcess) { activities.Add(Leaf("other-native", "scope-other")); bindings.Add(new("OtherTask", "other-native")); headers.Add(Header("P2", "scope-other", "other-local")); }
        var document = new BpmnAuthoredDocument(Xml, "P1", activities, bindings, [Variable("root")])
        { ScopeHeaders = headers, AdditionalProperties = new() { ["futureMetadata"] = JsonSerializer.SerializeToElement(new { text = "owner", keep = true }, Json) } };
        return new("owner", "bpmn-exact-version", [], [], new(Process.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(document, Json)));
    }

    private static int CountProperties(JsonElement root, string name) => root.ValueKind switch
    {
        JsonValueKind.Object => root.EnumerateObject().Sum(property => (property.Name == name ? 1 : 0) + CountProperties(property.Value, name)),
        JsonValueKind.Array => root.EnumerateArray().Sum(child => CountProperties(child, name)), _ => 0
    };

    private sealed class Draft(ActivityNode root) : IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "definition";
        public WorkflowDefinitionState State => new([], root, [], [], null);
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
