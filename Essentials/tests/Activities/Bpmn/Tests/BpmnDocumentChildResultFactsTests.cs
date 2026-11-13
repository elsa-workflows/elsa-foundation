using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDocumentChildResultFactsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Admitted_ordinary_chain_proves_flow_order_once_per_invocation_in_its_actual_scope(bool nested)
    {
        var (structures, owner, scope) = Model(nested);
        var target = nested ? Assert.Single(structures.ProjectChildren(owner).Single().Activities) : owner;
        var facts = structures.AssessChildResult(target, "producer", "consumer");
        Assert.Equal(ActivityChildResultAvailability.Definite, facts.Availability);
        Assert.False(facts.EarlierProducerRunCanReachConsumer);
        Assert.False(facts.MayRunConcurrently);
        Assert.Equal(["producer"], structures.ChildrenCompletedBefore(target, "consumer"));
        Assert.Equal(ActivityChildResultAvailability.Unavailable, structures.AssessChildResult(target, "consumer", "producer").Availability);
        Assert.Equal(scope, nested ? BpmnDocumentScopeProjection.ReadView(target).ScopeElementId : BpmnDocumentStructureHandler.Read(owner).ProcessId);
        if (nested) Assert.Equal(ActivityChildResultAvailability.Unknown, structures.AssessChildResult(owner, target.NodeId, target.NodeId).Availability);
    }

    [Theory]
    [InlineData("loop")]
    [InlineData("event")]
    [InlineData("extension")]
    [InlineData("conditional")]
    [InlineData("unbound")]
    [InlineData("background")]
    [InlineData("native-body")]
    public void Raw_unknown_semantics_or_unqualified_bindings_never_gain_order_from_reduction(string mutation)
    {
        var (structures, owner, _) = Model(false, mutation);
        var before = owner.Structure!.Payload.GetRawText();
        var facts = structures.AssessChildResult(owner, "producer", "consumer");
        Assert.Equal(ActivityChildResultAvailability.Unknown, facts.Availability);
        Assert.Null(facts.EarlierProducerRunCanReachConsumer);
        Assert.Null(structures.ChildrenCompletedBefore(owner, "consumer"));
        Assert.Equal(before, owner.Structure.Payload.GetRawText());
    }

    private static (DefaultActivityStructureService Structures, ActivityNode Owner, string Scope) Model(bool nested, string mutation = "")
    {
        var services = new ServiceCollection(); new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var structures = new DefaultActivityStructureService(provider.GetServices<IActivityStructureHandler>());
        var modifier = mutation switch { "loop" => "<b:standardLoopCharacteristics/>", "event" => "<b:errorEventDefinition/>",
            "extension" => "<b:extensionElements><v:unknown/></b:extensionElements>", _ => "" };
        var body = $"<b:startEvent id='S'/><b:task id='First'>{modifier}</b:task><b:task id='Second'/><b:endEvent id='E'/>" +
            "<b:sequenceFlow id='F1' sourceRef='S' targetRef='First'/>" +
            $"<b:sequenceFlow id='F2' sourceRef='First' targetRef='Second' {(mutation == "conditional" ? "e:conditionOutcome='Next'" : "")}/>" +
            "<b:sequenceFlow id='F3' sourceRef='Second' targetRef='E'/>";
        var xml = $"<b:definitions xmlns:b='{BpmnDocumentStructureHandler.ModelNamespace}' xmlns:v='urn:vendor' xmlns:e='{BpmnDocumentAdmission.ElsaNamespace}'>" +
            "<b:process id='P' isExecutable='true'>" + (nested ? $"<b:startEvent id='OuterS'/><b:subProcess id='Scope'>{body}</b:subProcess><b:endEvent id='OuterE'/><b:sequenceFlow id='OF1' sourceRef='OuterS' targetRef='Scope'/><b:sequenceFlow id='OF2' sourceRef='Scope' targetRef='OuterE'/>" : body) + "</b:process></b:definitions>";
        var producer = new ActivityNode("producer", "native@1", [], []) { ExecutionPlacement = mutation == "background" ? ActivityExecutionPlacement.Background : ActivityExecutionPlacement.Inline };
        if (mutation == "native-body") producer = producer with { Structure = new("opaque.body", "1", JsonSerializer.SerializeToElement(new { })) };
        var document = new BpmnAuthoredDocument(xml, "P", mutation == "unbound" ? [new("consumer", "native@1", [], [])] : [new("consumer", "native@1", [], []), producer],
            mutation == "unbound" ? [new("Second", "consumer")] : [new("First", "producer"), new("Second", "consumer")], [])
        { ScopeHeaders = nested ? [new("Scope", "scope-node", "process@1", [], [], [])] : [] };
        return (structures, new("owner", "process@1", [], [], new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion,
            JsonSerializer.SerializeToElement(document, BpmnDocumentStructureHandler.SerializerOptions))), nested ? "Scope" : "P");
    }
}
