using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class ActivityStructureCompilationContextTests
{
    private const string Xml = """
        <b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL" targetNamespace="urn:test"><b:process id="P" isExecutable="true"><b:startEvent id="S"/><b:endEvent id="E"/><b:sequenceFlow id="F" sourceRef="S" targetRef="E"/></b:process></b:definitions>
        """;

    [Fact]
    public void Exact_resolved_native_role_lowers_without_changing_the_physical_document()
    {
        var owner = Owner();
        var before = owner.Structure!.Payload.GetRawText();
        var compiled = new BpmnDocumentStructureHandler().CompileExecutableStructure(owner, Context(owner));
        Assert.Equal(BpmnActivityIdentities.StructureSchemaVersion, compiled.SchemaVersion);
        Assert.Equal(2, compiled.Payload.GetProperty("elements").GetArrayLength());
        Assert.False(compiled.Payload.TryGetProperty("documentXml", out _));
        Assert.Equal(before, owner.Structure.Payload.GetRawText());
        Assert.Equal(Xml, BpmnDocumentStructureHandler.Read(owner).DocumentXml);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("version")]
    [InlineData("reusable")]
    [InlineData("type")]
    [InlineData("consumer")]
    [InlineData("descriptor-schema")]
    [InlineData("target")]
    public void A_catalog_label_or_unqualified_runtime_contract_cannot_authorize_native_lowering(string mutation)
    {
        var owner = Owner();
        var fact = Fact(owner);
        fact = mutation switch
        {
            "version" => fact with { ActivityVersionId = "other-exact-version" },
            "reusable" => fact with { ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary },
            "type" => fact with { RuntimeActivityTypeKey = "Actual.GraphBoundary" },
            "consumer" => fact with { RuntimeConsumerKey = "activity-graph" },
            "descriptor-schema" => fact with { RuntimeDescriptorSchemaVersion = "99" },
            "target" => fact with { DeclaredStructure = new(BpmnActivityIdentities.StructureKind, "99") },
            _ => fact
        };
        var facts = mutation == "missing" ? new Dictionary<string, ResolvedStructureInvocation>()
            : new Dictionary<string, ResolvedStructureInvocation> { [owner.NodeId] = fact };
        var context = new ActivityStructureCompilationContext(owner, facts, EmptyProjection(owner));
        Assert.Throws<ArgumentException>(() => new BpmnDocumentStructureHandler().CompileExecutableStructure(owner, context));
        Assert.Equal(Xml, BpmnDocumentStructureHandler.Read(owner).DocumentXml);
    }

    [Fact]
    public void A_changed_source_cannot_be_lowered_against_a_different_physical_owner()
    {
        var physical = Owner();
        var changed = Owner(Xml.Replace("id=\"E\"", "id=\"End\"", StringComparison.Ordinal).Replace("targetRef=\"E\"", "targetRef=\"End\"", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new BpmnDocumentStructureHandler().CompileExecutableStructure(changed, Context(physical)));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("header")]
    [InlineData("native-descendant")]
    public void Background_intent_is_retained_and_refused_before_a_Graph_overlay_can_drop_it(string target)
    {
        var owner = Owner();
        var facts = new Dictionary<string, ResolvedStructureInvocation>();
        var projections = EmptyProjection(owner);
        if (target == "owner") owner = owner with { ExecutionPlacement = ActivityExecutionPlacement.Background };
        else if (target == "header")
        {
            owner = Owner(Xml.Replace("<b:endEvent id=\"E\"/>", "<b:subProcess id=\"E\"><b:startEvent id=\"Inner\"/></b:subProcess>", StringComparison.Ordinal));
            var source = BpmnDocumentStructureHandler.Read(owner);
            var header = new BpmnDocumentScopeHeader("E", "header", owner.ActivityVersionId, [], [], []) { ExecutionPlacement = ActivityExecutionPlacement.Background };
            owner = BpmnDocumentScopeProjection.Write(owner, source with { ScopeHeaders = [header] });
            facts.Add(header.NodeId, Fact(owner));
        }
        else
        {
            owner = Owner(Xml.Replace("<b:endEvent id=\"E\"/>", "<b:task id=\"E\"/>", StringComparison.Ordinal));
            var leaf = new ActivityNode("background-leaf", "leaf-version", [], []) { ExecutionPlacement = ActivityExecutionPlacement.Background };
            var body = new ActivityNode("native-body", "body-version", [], [], new("test.legacy", "1", JsonSerializer.SerializeToElement(new { })));
            var source = BpmnDocumentStructureHandler.Read(owner);
            owner = BpmnDocumentScopeProjection.Write(owner, source with { Activities = [body], Bindings = [new("E", body.NodeId)] });
            projections.Add(body.NodeId, [new("body", [leaf])]);
            projections.Add(leaf.NodeId, []);
        }
        facts.Add(owner.NodeId, Fact(owner));
        var before = owner.Structure!.Payload.GetRawText();
        var context = new ActivityStructureCompilationContext(owner, facts, projections);
        var error = Assert.Throws<NotSupportedException>(() => new BpmnDocumentStructureHandler().CompileExecutableStructure(owner, context));
        Assert.Contains("background execution placement", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, owner.Structure.Payload.GetRawText());
    }

    [Fact]
    public void Existing_custom_handler_uses_its_one_argument_contract_through_the_context_overload()
    {
        var handler = new LegacyHandler();
        IActivityStructureService service = new DefaultActivityStructureService([handler]);
        var node = new ActivityNode("custom", "custom-version", [], [], new(handler.Kind, handler.SchemaVersion, JsonSerializer.SerializeToElement(new { opaque = "retained" })));
        Assert.Equal(node.Structure, service.CompileExecutableStructure(node, Context(Owner())));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Context_dispatch_refuses_an_unhandled_version_of_a_known_kind_but_keeps_unrelated_opaque_contracts()
    {
        var service = new DefaultActivityStructureService([new LegacyHandler()]);
        var node = new ActivityNode("custom", "custom-version", [], [], new("test.legacy", "99", JsonSerializer.SerializeToElement(new { opaque = "retained" })));
        Assert.Throws<NotSupportedException>(() => service.CompileExecutableStructure(node, Context(Owner())));
        var unrelated = node with { Structure = new("vendor.unhandled", "99", node.Structure!.Payload) };
        Assert.Equal(unrelated.Structure, service.CompileExecutableStructure(unrelated, Context(Owner())));
    }

    [Fact]
    public void Context_captures_fact_and_projection_collections_before_the_caller_changes_them()
    {
        var owner = Owner();
        var fact = Fact(owner);
        var facts = new Dictionary<string, ResolvedStructureInvocation> { [owner.NodeId] = fact };
        var children = new[] { new ActivityNode("child", "child-version", [], []) };
        var slots = new[] { new ActivityChildProjection("body", children) };
        var projections = new Dictionary<string, IReadOnlyCollection<ActivityChildProjection>> { [owner.NodeId] = slots };
        var context = new ActivityStructureCompilationContext(owner, facts, projections);
        facts.Clear();
        children[0] = children[0] with { NodeId = "replacement" };
        slots[0] = new("other", []);
        projections.Clear();
        Assert.Equal(fact, context.Invocations[owner.NodeId]);
        var retained = Assert.Single(context.ChildProjections[owner.NodeId]);
        Assert.Equal("body", retained.Name);
        Assert.Equal("child", Assert.Single(retained.Activities).NodeId);
    }

    private static ActivityNode Owner(string xml = Xml) => new("source", "native-process-version", [], [],
        new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion,
            JsonSerializer.SerializeToElement(new BpmnAuthoredDocument(xml, "P", [], [], []), BpmnDocumentStructureHandler.SerializerOptions)));
    private static ResolvedStructureInvocation Fact(ActivityNode owner) => new(owner.ActivityVersionId,
        ActivityDefinitionVersionResolutionKind.AuthorableActivity, typeof(BpmnProcess).FullName!, WellKnownRuntimeActivityConsumers.ClrActivity,
        RuntimeActivityDescriptor.InitialSchemaVersion, new(BpmnActivityIdentities.StructureKind, BpmnActivityIdentities.StructureSchemaVersion));
    private static Dictionary<string, IReadOnlyCollection<ActivityChildProjection>> EmptyProjection(ActivityNode owner) => new() { [owner.NodeId] = [] };
    private static ActivityStructureCompilationContext Context(ActivityNode owner) => new(owner,
        new Dictionary<string, ResolvedStructureInvocation> { [owner.NodeId] = Fact(owner) }, EmptyProjection(owner));

    private sealed class LegacyHandler : IActivityStructureHandler
    {
        public string Kind => "test.legacy";
        public string SchemaVersion => "1";
        public int Calls { get; private set; }
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) { Calls++; return activity.Structure!; }
    }
}
