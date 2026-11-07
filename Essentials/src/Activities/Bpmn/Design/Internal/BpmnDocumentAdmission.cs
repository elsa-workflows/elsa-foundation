using System.Text.Json;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed record BpmnDocumentDiagnostic(string Code, string ElementId, string SourcePath, string Message);

/// <summary>
/// Content admission before complete XML becomes the existing runtime graph. Unknown content remains
/// authored; no importer, warning flag or lossy typed model decides whether execution is admitted.
/// </summary>
internal static class BpmnDocumentAdmission
{
    internal const string ElsaNamespace = "https://elsa-workflows.io/schemas/bpmn";
    private static readonly XNamespace Model = BpmnDocumentStructureHandler.ModelNamespace;
    private static readonly XNamespace Diagram = "http://www.omg.org/spec/BPMN/20100524/DI";
    private static readonly XNamespace Coordinates = "http://www.omg.org/spec/DD/20100524/DI";
    private static readonly XNamespace Bounds = "http://www.omg.org/spec/DD/20100524/DC";
    private static readonly HashSet<string> Tasks = new(StringComparer.Ordinal)
    { "task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask" };
    private static readonly HashSet<string> Gateways = new(StringComparer.Ordinal)
    { "exclusiveGateway", "inclusiveGateway", "parallelGateway" };
    private static readonly HashSet<string> Presentation = new(StringComparer.Ordinal)
    { "laneSet", "childLaneSet", "lane", "flowNodeRef", "textAnnotation", "text", "association", "group", "category", "categoryValue" };

    internal static IReadOnlyList<BpmnDocumentDiagnostic> Assess(ActivityNode owner)
    {
        var source = BpmnDocumentStructureHandler.Read(owner);
        var xml = BpmnDocumentStructureHandler.ParseXml(source.DocumentXml);
        var definitions = xml.Root!;
        var diagnostics = new List<BpmnDocumentDiagnostic>();
        var scopes = definitions.Descendants().Where(BpmnDocumentScopeProjection.IsScope).ToArray();
        var headers = source.ScopeHeaders.ToDictionary(header => header.ScopeElementId, StringComparer.Ordinal);
        var bindings = source.Bindings.ToDictionary(binding => binding.ElementId, StringComparer.Ordinal);
        var selected = definitions.Elements(Model + "process").Single(element => Id(element) == source.ProcessId);
        if (owner.ExecutionPlacement != ActivityExecutionPlacement.Inline)
            Refuse(selected, "BackgroundPlacement", "This complete-source execution profile has unqualified background execution placement; the exact configuration remains authored.");
        foreach (var header in source.ScopeHeaders.Where(header => header.ExecutionPlacement != ActivityExecutionPlacement.Inline))
            Refuse(scopes.Single(scope => Id(scope) == header.ScopeElementId), "BackgroundPlacement",
                "This complete-source execution profile has unqualified background execution placement; the exact scope configuration remains authored.");

        foreach (var binding in source.Bindings)
        {
            var element = definitions.Descendants().Single(element => element.Name.Namespace == Model && (string?)element.Attribute("id") == binding.ElementId);
            if (binding.ListenerNodeId is not null)
                Refuse(element, "UnsupportedListener", "Listener execution requires qualified event-subprocess lowering; the exact listener remains authored.");
            if (binding.ChildNodeId is not null && !Tasks.Contains(element.Name.LocalName) &&
                element.Name.LocalName is not ("subProcess" or "exclusiveGateway" or "inclusiveGateway"))
                Refuse(element, "UnsupportedBinding", "This element's native work role has no qualified execution mapping.");
        }

        foreach (var process in definitions.Elements(Model + "process").Where(element => element != selected))
            Refuse(process, "IndependentProcess", "Independent-process execution is not qualified by this source adapter; all processes remain in the draft.");
        if (selected.Attribute("isExecutable")?.Value is not ("true" or "1"))
            Refuse(selected, "NotExecutable", "The selected process must explicitly declare isExecutable=true.");

        Visit(definitions);
        foreach (var scope in scopes.Where(element => BpmnDocumentScopeProjection.HasStandardScopeChain(element, definitions)))
        {
            if (scope.Name.LocalName != "process" && !headers.ContainsKey(Id(scope)) && !bindings.ContainsKey(Id(scope)))
                Refuse(scope, "MissingScopeConfiguration", "A standard body requires a stable native scope header or an explicitly bound native body.");
            if (headers.ContainsKey(Id(scope)) && bindings.TryGetValue(Id(scope), out var bodyBinding) && bodyBinding.ChildNodeId is not null)
                Refuse(scope, "ConflictingBodies", "Standard scope configuration and an independent native body both carry intent; neither can be selected implicitly.");
            if (bindings.TryGetValue(Id(scope), out var nativeBody) && nativeBody.ChildNodeId is not null && scope.Elements().Any(IsFlowContent))
                Refuse(scope, "ConflictingBodies", "A native body is bound while standard body execution content is also present.");
            var elements = scope.Elements().Where(element => IsFlowNode(element) && element.Attribute("id") is not null).ToDictionary(Id, StringComparer.Ordinal);
            var flows = scope.Elements(Model + "sequenceFlow").ToArray();
            if ((scope == selected || headers.ContainsKey(Id(scope))) && !elements.Values.Any(element => element.Name == Model + "startEvent"))
                Refuse(scope, "MissingStart", "This supported execution profile requires a start event in every invoked standard scope.");
            foreach (var flow in flows)
            {
                var start = Reference(flow, "sourceRef");
                var end = Reference(flow, "targetRef");
                if (start is null || end is null || !elements.TryGetValue(start, out var from) || !elements.TryGetValue(end, out var to))
                { Refuse(flow, "InvalidFlow", "Sequence flow endpoints must resolve to flow nodes in this exact scope."); continue; }
                if (from.Name == Model + "endEvent" || to.Name == Model + "startEvent")
                    Refuse(flow, "InvalidFlow", "An end event cannot have outgoing flow and a start event cannot have incoming flow.");
                if ((string?)from.Attribute("default") == Id(flow) && flow.Attribute(XName.Get("conditionOutcome", ElsaNamespace)) is not null)
                    Refuse(flow, "InvalidDefaultCondition", "A default flow cannot also declare an outcome condition.");
            }
            foreach (var element in elements.Values)
            {
                if (element.Elements(Model + "terminateEventDefinition").Skip(1).Any())
                    Refuse(element, "UnsupportedEventCombination", "Multiple event definitions cannot be collapsed into one runtime event.");
                var defaultId = (string?)element.Attribute("default");
                if (defaultId is not null && !flows.Any(flow => Id(flow) == defaultId && Reference(flow, "sourceRef") == Id(element)))
                    Refuse(element, "InvalidDefault", "The default must identify this element's outgoing sequence flow.");
                if (Tasks.Contains(element.Name.LocalName) && element.Name.LocalName != "task" &&
                    (!bindings.TryGetValue(Id(element), out var work) || work.ChildNodeId is null))
                    Refuse(element, "UnboundWork", "This work-bearing task requires an explicit native activity binding; it cannot execute as a no-op.");
                foreach (var reference in element.Elements().Where(child => child.Name == Model + "incoming" || child.Name == Model + "outgoing"))
                {
                    var flowId = Resolve(reference, reference.Value);
                    if (!flows.Any(flow => Id(flow) == flowId && Reference(flow, reference.Name.LocalName == "incoming" ? "targetRef" : "sourceRef") == Id(element)))
                        Refuse(reference, "InvalidFlowReference", "The flow-node reference must resolve to its actual incoming or outgoing flow.");
                }
                if (element.Elements().Where(child => child.Name == Model + "incoming" || child.Name == Model + "outgoing")
                    .GroupBy(child => (child.Name, Flow: Resolve(child, child.Value))).Any(group => group.Count() > 1))
                    Refuse(element, "InvalidFlowReference", "A flow-node reference is duplicated.");
            }
        }
        return diagnostics;

        void Visit(XElement element)
        {
            if (IsPresentationNamespace(element)) { VisitDiagram(element); return; }
            if (element.Name == Model + "documentation") return;
            if (element.Name.Namespace != Model)
            { Refuse(element, "UnknownExtension", "This extension has no qualified execution mapping."); return; }
            var name = element.Name.LocalName;
            var allowed = name switch
            {
                "definitions" => new[] { "id", "name", "targetNamespace", "exporter", "exporterVersion", "expressionLanguage", "typeLanguage" },
                "process" => new[] { "id", "name", "isExecutable" },
                "subProcess" => new[] { "id", "name" },
                "sequenceFlow" => new[] { "id", "name", "sourceRef", "targetRef" },
                "exclusiveGateway" or "inclusiveGateway" => new[] { "id", "name", "default" },
                "parallelGateway" => new[] { "id", "name" },
                "startEvent" or "endEvent" or "terminateEventDefinition" or "incoming" or "outgoing" => new[] { "id", "name" },
                _ when Tasks.Contains(name) => new[] { "id", "name", "default" },
                _ when Presentation.Contains(name) => new[] { "id", "name", "sourceRef", "targetRef", "associationDirection", "categoryValueRef", "value", "textFormat" },
                "extensionElements" => new[] { "id" },
                _ => null
            };
            if (allowed is null)
            { Refuse(element, "UnsupportedSyntax", $"'{name}' has no qualified lowering to the current runtime structure."); return; }
            if (!ValidParent(element)) Refuse(element, "UnsupportedContainment", "This element is outside its represented standard ownership path.");
            if ((IsFlowContent(element) || BpmnDocumentScopeProjection.IsScope(element)) && element.Attribute("id") is null)
                Refuse(element, "MissingIdentity", "Executable source elements require authored identities; lowering cannot invent them.");
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace == XNamespace.Xml && (attribute.Name.LocalName is "base" or "lang" or "space")) continue;
                if (name == "definitions" && attribute.Name == XName.Get("schemaLocation", "http://www.w3.org/2001/XMLSchema-instance")) continue;
                if (attribute.Name.NamespaceName.Length == 0 && allowed.Contains(attribute.Name.LocalName, StringComparer.Ordinal)) continue;
                if (name == "sequenceFlow" && attribute.Name == XName.Get("conditionOutcome", ElsaNamespace))
                {
                    if (string.IsNullOrWhiteSpace(attribute.Value) || attribute.Value != attribute.Value.Trim())
                        Refuse(element, "InvalidOutcome", "An outcome name must be nonblank and cannot require trimming during lowering.");
                    continue;
                }
                Refuse(element, "UnsupportedAttribute", $"Attribute '{attribute.Name}' has no qualified execution mapping.");
            }
            if (element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)) &&
                name is not ("incoming" or "outgoing" or "flowNodeRef" or "text"))
                Refuse(element, "UnsupportedText", "Nonblank executable or extension text cannot be discarded during lowering.");
            if (element.Nodes().OfType<XProcessingInstruction>().Any())
                Refuse(element, "UnsupportedInstruction", "A processing instruction in executable source has no qualified mapping.");
            foreach (var child in element.Elements()) Visit(child);
        }
        void VisitDiagram(XElement element)
        {
            var parent = element.Parent;
            var valid = element.Name.Namespace == Diagram ? element.Name.LocalName switch
            {
                "BPMNDiagram" => parent == definitions,
                "BPMNPlane" or "BPMNLabelStyle" => parent?.Name == Diagram + "BPMNDiagram",
                "BPMNShape" or "BPMNEdge" => parent?.Name == Diagram + "BPMNPlane",
                "BPMNLabel" => parent?.Name == Diagram + "BPMNShape" || parent?.Name == Diagram + "BPMNEdge",
                _ => false
            } : element.Name.Namespace == Bounds ? element.Name.LocalName switch
            {
                "Bounds" => parent?.Name == Diagram + "BPMNShape" || parent?.Name == Diagram + "BPMNLabel",
                "Font" => parent?.Name == Diagram + "BPMNLabelStyle",
                _ => false
            } : element.Name.Namespace == Coordinates && (element.Name.LocalName switch
            {
                "waypoint" => parent?.Name == Diagram + "BPMNEdge",
                "extension" => parent?.Name == Diagram + "BPMNPlane" || parent?.Name == Diagram + "BPMNShape" ||
                    parent?.Name == Diagram + "BPMNEdge" || parent?.Name == Diagram + "BPMNLabel",
                _ => false
            });
            if (!valid) Refuse(element, "UnsupportedPresentation", "Presentation syntax is misplaced or has no qualified non-executable ownership path.");
            foreach (var child in element.Elements())
            {
                if (child.Name.Namespace == Model)
                    Refuse(child, "SemanticContentInPresentation", "Standard model content inside presentation cannot be silently excluded from execution admission.");
                else if (IsPresentationNamespace(child)) VisitDiagram(child);
                else if (element.Name == Coordinates + "extension")
                {
                    // Retain vendor DI metadata, but never let an opaque wrapper hide model syntax.
                    foreach (var nested in child.DescendantsAndSelf().Where(node => node.Name.Namespace == Model || IsPresentationNamespace(node)))
                        Refuse(nested, "SemanticContentInPresentation", "A presentation extension contains model or unqualified presentation syntax.");
                }
                else Refuse(child, "UnsupportedPresentation", "Unknown presentation children require a qualified DI extension owner.");
            }
        }
        void Refuse(XElement element, string code, string message) => diagnostics.Add(new(code, Id(element), Path(element), message));
        string? Resolve(XElement context, string value)
        {
            var parts = value.Trim().Split(':');
            return parts.Length == 1 ? parts[0] : parts.Length == 2 && context.GetNamespaceOfPrefix(parts[0])?.NamespaceName == (string?)definitions.Attribute("targetNamespace") ? parts[1] : null;
        }
        string? Reference(XElement element, string name) => element.Attribute(name) is { } attribute ? Resolve(element, attribute.Value) : null;
    }

    private static bool ValidParent(XElement element)
    {
        var parent = element.Parent;
        var name = element.Name.LocalName;
        if (name == "definitions") return parent is null;
        if (parent?.Name.Namespace != Model) return false;
        if (name == "process" || name == "category") return parent.Name.LocalName == "definitions";
        if (IsFlowContent(element) || name is "laneSet" or "textAnnotation" or "association" or "group") return BpmnDocumentScopeProjection.IsScope(parent);
        return name switch
        {
            "terminateEventDefinition" => parent.Name.LocalName == "endEvent",
            "incoming" or "outgoing" => IsFlowNode(parent),
            "lane" => parent.Name.LocalName is "laneSet" or "childLaneSet",
            "childLaneSet" or "flowNodeRef" => parent.Name.LocalName == "lane",
            "text" => parent.Name.LocalName == "textAnnotation",
            "categoryValue" => parent.Name.LocalName == "category",
            "extensionElements" => true,
            _ => false
        };
    }

    internal static void Require(ActivityNode owner)
    {
        var diagnostics = Assess(owner);
        if (diagnostics.Count != 0)
            throw new NotSupportedException($"BPMN source '{owner.NodeId}' cannot execute: " + string.Join("; ", diagnostics.Select(item => $"{item.Code} at {item.SourcePath}: {item.Message}")));
    }

    internal static ActivityNodeStructure Lower(ActivityNode owner, string scopeId)
    {
        Require(owner); // Always assess the complete source, including every other scope, before reduction.
        var source = BpmnDocumentStructureHandler.Read(owner);
        var xml = BpmnDocumentStructureHandler.ParseXml(source.DocumentXml);
        var scope = xml.Descendants().Single(element => (string?)element.Attribute("id") == scopeId && BpmnDocumentScopeProjection.IsScope(element));
        var bindings = source.Bindings.ToDictionary(binding => binding.ElementId, StringComparer.Ordinal);
        var headers = source.ScopeHeaders.ToDictionary(header => header.ScopeElementId, StringComparer.Ordinal);
        var elements = scope.Elements().Where(IsFlowNode).Select(element =>
        {
            bindings.TryGetValue(Id(element), out var binding);
            headers.TryGetValue(Id(element), out var header);
            return new BpmnElement(Id(element), element.Name.LocalName, (string?)element.Attribute("name"), binding?.ChildNodeId ?? header?.NodeId,
                defaultFlowId: (string?)element.Attribute("default"),
                eventDefinitions: element.Element(Model + "terminateEventDefinition") is null ? [] : [new(BpmnEventDefinitionTypes.Terminate)]);
        }).ToArray();
        var flows = scope.Elements(Model + "sequenceFlow").Select(flow => new BpmnSequenceFlow(Id(flow), Resolve(flow, "sourceRef"), Resolve(flow, "targetRef"),
            (string?)flow.Attribute("name"), (string?)flow.Attribute(XName.Get("conditionOutcome", ElsaNamespace)),
            elements.Any(element => element.DefaultFlowId == Id(flow)))).ToArray();
        var variables = scopeId == source.ProcessId ? source.Variables : headers[scopeId].Variables;
        return new(BpmnActivityIdentities.StructureKind, BpmnActivityIdentities.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows, variables), BpmnDocumentStructureHandler.SerializerOptions));

        string Resolve(XElement element, string name) => element.Attribute(name)!.Value.Trim().Split(':').Last();
    }

    internal static IReadOnlyCollection<ActivityChildOutcomeProjection> Outcomes(ActivityNode owner, string scopeId)
    {
        var source = BpmnDocumentStructureHandler.Read(owner);
        var xml = BpmnDocumentStructureHandler.ParseXml(source.DocumentXml);
        var scope = xml.Descendants().Single(element => (string?)element.Attribute("id") == scopeId && BpmnDocumentScopeProjection.IsScope(element));
        return source.Bindings.Where(binding => binding.ChildNodeId is not null && scope.Elements().Any(element => element.Name.Namespace == Model && (string?)element.Attribute("id") == binding.ElementId))
            .Select(binding => new ActivityChildOutcomeProjection(binding.ChildNodeId!, BpmnActivityIdentities.DecisionType,
                scope.Elements(Model + "sequenceFlow").Where(flow => Source(flow) == binding.ElementId &&
                    scope.Elements().Single(element => element.Name.Namespace == Model && (string?)element.Attribute("id") == binding.ElementId).Attribute("default")?.Value != Id(flow))
                    .Select(flow => (string?)flow.Attribute(XName.Get("conditionOutcome", ElsaNamespace))).OfType<string>().Distinct(StringComparer.Ordinal).ToArray(),
                BpmnActivityIdentities.DecisionOutcomeInput)).ToArray();

        string? Source(XElement flow)
        {
            var parts = flow.Attribute("sourceRef")?.Value.Trim().Split(':');
            return parts?.Length == 1 ? parts[0] : parts?.Length == 2 && flow.GetNamespaceOfPrefix(parts[0])?.NamespaceName == (string?)xml.Root!.Attribute("targetNamespace") ? parts[1] : null;
        }
    }

    private static bool IsFlowNode(XElement element) => element.Name.Namespace == Model &&
        (Tasks.Contains(element.Name.LocalName) || Gateways.Contains(element.Name.LocalName) || element.Name.LocalName is "startEvent" or "endEvent" or "subProcess");
    private static bool IsFlowContent(XElement element) => IsFlowNode(element) || element.Name == Model + "sequenceFlow";
    private static bool IsPresentationNamespace(XElement element) => element.Name.Namespace == Diagram || element.Name.Namespace == Coordinates || element.Name.Namespace == Bounds;
    private static string Id(XElement element) => (string?)element.Attribute("id") ?? element.Name.LocalName;
    private static string Path(XElement element) => string.Join("/", element.AncestorsAndSelf().Reverse().Select(item => $"{item.Name}[{Id(item)}]"));
}
