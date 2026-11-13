using System.Xml.Linq;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Proves order from admitted raw source only; a reduced graph never certifies unfamiliar source.</summary>
internal static class BpmnDocumentChildResultAnalysis
{
    internal static BpmnLinearChildResultAnalysis? TryCreate(ActivityNode owner, string scopeId)
    {
        if (BpmnDocumentAdmission.Assess(owner).Count != 0) return null;
        var source = BpmnDocumentStructureHandler.Read(owner);
        var xml = BpmnDocumentStructureHandler.ParseXml(source.DocumentXml, scopeId, owner.NodeId);
        XNamespace model = BpmnDocumentStructureHandler.ModelNamespace;
        var scope = xml.Descendants().Single(element => element.Name.Namespace == model && (string?)element.Attribute("id") == scopeId);
        var children = BpmnDocumentScopeProjection.Project(owner, scopeId).Single().Activities.ToArray();
        if (children.Any(child => child.Intrinsic is not null || child.Structure is not null || child.ExecutionPlacement != ActivityExecutionPlacement.Inline)) return null;
        var bindings = source.Bindings.ToDictionary(binding => binding.ElementId, StringComparer.Ordinal);
        var elements = new List<BpmnElement>();
        foreach (var element in scope.Elements().Where(element => element.Name.Namespace == model))
        {
            if (element.Name.LocalName is "sequenceFlow" or "documentation" or "laneSet" or "textAnnotation" or "association" or "group") continue;
            if (element.Name.LocalName is not ("startEvent" or "task" or "endEvent") || element.Elements().Any(child => child.Name.Namespace == model && child.Name.LocalName != "documentation") ||
                element.Attribute("default") is not null) return null;
            var id = (string)element.Attribute("id")!;
            bindings.TryGetValue(id, out var binding);
            elements.Add(new(id, element.Name.LocalName, childNodeId: binding?.ChildNodeId));
        }
        var flows = scope.Elements(model + "sequenceFlow").Select(flow => new BpmnSequenceFlow((string)flow.Attribute("id")!,
            BpmnDocumentReference.Resolve(flow, (string?)flow.Attribute("sourceRef"))!,
            BpmnDocumentReference.Resolve(flow, (string?)flow.Attribute("targetRef"))!,
            conditionOutcome: (string?)flow.Attribute(XName.Get("conditionOutcome", BpmnDocumentAdmission.ElsaNamespace)))).ToArray();
        return BpmnLinearChildResultAnalysis.TryCreate(new(activities: children, elements: elements, sequenceFlows: flows));
    }
}
