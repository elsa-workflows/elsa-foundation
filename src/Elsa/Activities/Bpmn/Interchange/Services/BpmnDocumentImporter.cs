using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// Maps BPMN 2.0 XML onto the native <c>elsa.bpmn.structure</c> authored payload. Supported elements
/// (Phase 1 core plus the Phase 2 event tier) import cleanly; expression flow conditions degrade to
/// unconditional flows (reported); unsupported flow nodes are dropped with an issue. An embedded
/// <c>subProcess</c> becomes a nested <c>BpmnProcess</c> activity node bound by the subprocess element,
/// mirroring the runtime module's composition model. Event-defined start events (spec 117) import as pure
/// elements carrying a populated <c>BpmnEventDefinition</c>; intermediate catch events (spec 116) import
/// with a populated definition plus a synthesized bound suspending child (a <c>Delay</c> for timer, an
/// <c>Event</c> for message/signal) in the <c>Bpmn.Activities</c> slot (spec 118). BPMNDI shapes/edges are
/// preserved on the authored <c>diagram</c> payload for lossless layout round-trips.
/// </summary>
public sealed class BpmnDocumentImporter : IBpmnDocumentImporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Placeholder activity version id for nested BPMN process nodes; API hosts override via options later.</summary>
    public const string DefaultBpmnProcessActivityVersionId = "Elsa.BpmnProcess";

    /// <summary>Placeholder activity version id for a timer catch event's synthesized <c>Delay</c> child (hosts resolve the real catalog row later).</summary>
    public const string DefaultDelayActivityVersionId = "Elsa.Delay";

    /// <summary>Placeholder activity version id for a message/signal catch event's synthesized <c>Event</c> child (matches <c>Event.ActivityType</c>).</summary>
    public const string DefaultEventActivityVersionId = "Elsa.Event";

    public BpmnImportAnalysis Analyze(string xml, BpmnImportOptions? options = null)
    {
        var context = new ImportContext();
        ImportCore(xml, options, context);
        return context.ToAnalysis();
    }

    public BpmnImportResult Import(string xml, BpmnImportOptions? options = null)
    {
        var context = new ImportContext();
        var node = ImportCore(xml, options, context);
        return new BpmnImportResult(node, context.ToAnalysis());
    }

    private static ActivityNode ImportCore(string xml, BpmnImportOptions? options, ImportContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new BpmnInterchangeException("The document is not well-formed XML.", exception);
        }

        var definitions = document.Root;
        if (definitions is null || definitions.Name != BpmnXmlNames.Model + "definitions")
            throw new BpmnInterchangeException("The document root is not a BPMN 2.0 <definitions> element.");

        var processes = definitions.Elements(BpmnXmlNames.Model + "process").ToArray();
        foreach (var candidate in processes)
            context.ProcessIds.Add(IdOf(candidate) ?? "(no id)");
        if (processes.Length == 0)
            throw new BpmnInterchangeException("The document contains no <process> element.");

        var process = options?.ProcessId is { } processId
            ? processes.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(IdOf(candidate), processId))
              ?? throw new BpmnInterchangeException($"The document contains no process with id '{processId}'.")
            : processes.FirstOrDefault(candidate => string.Equals((string?)candidate.Attribute("isExecutable"), "true", StringComparison.OrdinalIgnoreCase))
              ?? processes[0];

        var diagram = ReadDiagram(definitions);
        var messageSignalNames = ReadMessageSignalDeclarations(definitions);
        var escalationDeclarations = ReadEscalationDeclarations(definitions);
        var processIdValue = IdOf(process) ?? "process";
        var nodeId = $"{options?.NodeIdPrefix ?? "node"}-{processIdValue}";
        return BuildProcessNode(process, nodeId, diagram, messageSignalNames, escalationDeclarations, context);
    }

    private static ActivityNode BuildProcessNode(XElement container, string nodeId, JsonElement? diagram, IReadOnlyDictionary<string, string> messageSignalNames, IReadOnlyDictionary<string, EscalationDeclaration> escalationDeclarations, ImportContext context, bool isTransaction = false)
    {
        var elements = new List<BpmnElement>();
        var flows = new List<BpmnSequenceFlow>();
        var lanes = new List<BpmnLane>();
        var childActivities = new List<ActivityNode>();
        var pendingBoundaries = new List<XElement>();
        // spec 124: compensate throw/end and boundary→handler associations resolve in later passes so their
        // targets are known regardless of document order.
        var pendingCompensateThrows = new List<XElement>();
        var pendingCompensateEnds = new List<XElement>();
        var associations = new List<(string Source, string Target)>();

        // spec 123 D3: the container's declared container-scoped variables gate collection-mode loop imports —
        // an elsa:collection naming a variable declared here imports as a real collection loop; an undeclared or
        // reserved name degrades. Collected before the element loop so the loop-resolution sees them.
        var declaredVariables = ReadDeclaredVariables(container);
        var declaredVariableNames = declaredVariables.Select(variable => variable.Name).ToHashSet(StringComparer.Ordinal);

        // spec 128 D7: per-scope event-subprocess trackers — distinct escalation codes with at most one code-less
        // catch-all, and at most one error event subprocess; a collision drops the offending event subprocess.
        var eventSubprocessEscalationCodes = new HashSet<string>(StringComparer.Ordinal);
        var hasEventSubprocessEscalationCatchAll = false;
        var hasErrorEventSubprocess = false;

        foreach (var child in container.Elements().Where(child => child.Name.Namespace == BpmnXmlNames.Model))
        {
            var localName = child.Name.LocalName;
            var id = IdOf(child);
            context.CountElement(localName);

            switch (localName)
            {
                case "startEvent":
                {
                    if (id is null) break;
                    // spec 128 D7: an event-subprocess body start declares an escalation/error trigger + isInterrupting
                    // (default true). It imports with its definition so the event-subprocess body-shape holds; a
                    // ref-less escalation start is the code-less catch-all.
                    if (child.Element(BpmnXmlNames.Model + "escalationEventDefinition") is { } startEscalationDefinition)
                    {
                        var code = ResolveEscalationRefCode(startEscalationDefinition, escalationDeclarations);
                        var properties = code is null ? null : EscalationProperties(code, ResolveEscalationRefName(startEscalationDefinition, escalationDeclarations));
                        elements.Add(new BpmnElement(id, BpmnElementTypes.StartEvent, name: NameOf(child),
                            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, properties)],
                            cancelActivity: (bool?)child.Attribute("isInterrupting") ?? true));
                        break;
                    }
                    if (child.Element(BpmnXmlNames.Model + "errorEventDefinition") is not null)
                    {
                        elements.Add(new BpmnElement(id, BpmnElementTypes.StartEvent, name: NameOf(child),
                            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error)],
                            cancelActivity: (bool?)child.Attribute("isInterrupting") ?? true));
                        break;
                    }
                    var startDefinitions = child.Elements().Where(IsEventDefinition).ToArray();
                    var startDefinition = startDefinitions.Length == 0
                        ? null
                        : ResolveStartEventDefinition(id, startDefinitions, messageSignalNames, context);
                    elements.Add(new BpmnElement(
                        id,
                        BpmnElementTypes.StartEvent,
                        name: NameOf(child),
                        eventDefinitions: startDefinition is null ? null : [startDefinition]));
                    break;
                }
                case "intermediateCatchEvent":
                {
                    if (id is null) break;
                    var resolved = ResolveCatchEvent(id, child, messageSignalNames, context);
                    if (resolved is not { } catchImport)
                        break; // Dropped (finding added inside); its sequence flows cascade-drop as unresolved refs.
                    childActivities.Add(catchImport.Child);
                    elements.Add(new BpmnElement(
                        id,
                        BpmnElementTypes.IntermediateCatchEvent,
                        name: NameOf(child),
                        childNodeId: catchImport.Child.NodeId,
                        defaultFlowId: DefaultOf(child),
                        eventDefinitions: [catchImport.Definition]));
                    break;
                }
                case "endEvent":
                {
                    if (id is null) break;
                    // spec 124: a compensate end event resolves in a later pass (its activityRef targets a host
                    // whose compensation boundary is known only after the boundary pass).
                    if (child.Element(BpmnXmlNames.Model + "compensateEventDefinition") is not null)
                    {
                        pendingCompensateEnds.Add(child);
                        break;
                    }
                    // spec 127 D4: an escalation end event resolves its escalationRef to a code; a ref-less end
                    // degrades to a none end event with a finding (an end has no flows to cascade).
                    if (child.Element(BpmnXmlNames.Model + "escalationEventDefinition") is { } escalationEndDefinition)
                    {
                        elements.Add(ResolveEscalationEnd(id, child, escalationEndDefinition, escalationDeclarations, context));
                        break;
                    }
                    // spec 125 D4: a cancel end event is valid only inside a transaction; outside one it degrades to
                    // a none end event with a finding (the validator would reject a cancel end in a non-transaction).
                    if (child.Element(BpmnXmlNames.Model + "cancelEventDefinition") is not null)
                    {
                        if (isTransaction)
                        {
                            elements.Add(new BpmnElement(id, BpmnElementTypes.EndEvent, name: NameOf(child),
                                eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Cancel)]));
                            break;
                        }

                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"End event '{id}' declares a cancel event definition but is not inside a transaction; it imported as a none end event.", id));
                        elements.Add(new BpmnElement(id, BpmnElementTypes.EndEvent, name: NameOf(child)));
                        break;
                    }
                    var isTerminate = child.Elements(BpmnXmlNames.Model + "terminateEventDefinition").Any();
                    var otherDefinitions = child.Elements().Where(IsEventDefinition).Any(definition => definition.Name.LocalName != "terminateEventDefinition");
                    if (otherDefinitions)
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"End event '{id}' declares unsupported event definitions; imported as a {(isTerminate ? "terminate" : "none")} end event.", id));
                    elements.Add(new BpmnElement(
                        id,
                        BpmnElementTypes.EndEvent,
                        name: NameOf(child),
                        eventDefinitions: isTerminate ? [new BpmnEventDefinition(BpmnEventDefinitionTypes.Terminate)] : null));
                    break;
                }
                case "intermediateThrowEvent":
                {
                    if (id is null) break;
                    // spec 127 D4: an escalation throw resolves its escalationRef to a code; a ref-less throw is
                    // Dropped with a finding (its flows cascade-drop as unresolved references).
                    if (child.Element(BpmnXmlNames.Model + "escalationEventDefinition") is { } escalationThrowDefinition)
                    {
                        if (ResolveEscalationThrow(id, child, escalationThrowDefinition, escalationDeclarations, context) is { } escalationThrow)
                            elements.Add(escalationThrow);
                        break;
                    }
                    // spec 124: resolved in a later pass (compensate → keep with resolvable activityRef; anything
                    // else drops, its flows cascading as unresolved references).
                    pendingCompensateThrows.Add(child);
                    break;
                }
                case "association":
                {
                    var sourceRef = ((string?)child.Attribute("sourceRef"))?.Trim();
                    var targetRef = ((string?)child.Attribute("targetRef"))?.Trim();
                    if (!string.IsNullOrWhiteSpace(sourceRef) && !string.IsNullOrWhiteSpace(targetRef))
                        associations.Add((sourceRef, targetRef));
                    break;
                }
                case "subProcess":
                {
                    if (id is null) break;
                    var nestedNodeId = $"node-{id}";
                    var subProcessBodyNode = BuildProcessNode(child, nestedNodeId, diagram: null, messageSignalNames, escalationDeclarations, context);

                    // spec 128 D7: an event subprocess (triggeredByEvent="true") — validate the body shape and per-scope
                    // uniqueness BEFORE emitting the element, so the importer never emits a graph the validator rejects.
                    if ((bool?)child.Attribute("triggeredByEvent") == true)
                    {
                        if (!TryResolveEventSubprocess(id, subProcessBodyNode, eventSubprocessEscalationCodes, ref hasEventSubprocessEscalationCatchAll, ref hasErrorEventSubprocess, context))
                            break; // Dropped (finding added inside); its body node is not added, flows cascade-drop.
                        childActivities.Add(subProcessBodyNode);
                        elements.Add(new BpmnElement(id, BpmnElementTypes.SubProcess, name: NameOf(child), childNodeId: nestedNodeId, triggeredByEvent: true));
                        break;
                    }

                    childActivities.Add(subProcessBodyNode);
                    elements.Add(new BpmnElement(id, BpmnElementTypes.SubProcess, name: NameOf(child), childNodeId: nestedNodeId, defaultFlowId: DefaultOf(child),
                        loopCharacteristics: ResolveLoopCharacteristics(child, id, hostBindsChild: true, declaredVariableNames, context),
                        isForCompensation: IsForCompensationOf(child)));
                    break;
                }
                case "transaction":
                {
                    if (id is null) break;
                    // spec 125 D4: a <transaction> imports exactly like a <subProcess> (nested BpmnProcess node
                    // synthesis) plus IsTransaction on the element AND on the nested authored structure.
                    var nestedNodeId = $"node-{id}";
                    childActivities.Add(BuildProcessNode(child, nestedNodeId, diagram: null, messageSignalNames, escalationDeclarations, context, isTransaction: true));
                    elements.Add(new BpmnElement(id, BpmnElementTypes.SubProcess, name: NameOf(child), childNodeId: nestedNodeId, defaultFlowId: DefaultOf(child),
                        loopCharacteristics: ResolveLoopCharacteristics(child, id, hostBindsChild: true, declaredVariableNames, context),
                        isForCompensation: IsForCompensationOf(child), isTransaction: true));
                    break;
                }
                case "boundaryEvent":
                {
                    if (id is null) break;
                    // Resolved in a second pass (below) so attachment resolves regardless of document order.
                    pendingBoundaries.Add(child);
                    break;
                }
                case "sequenceFlow":
                {
                    if (id is null) break;
                    var sourceRef = (string?)child.Attribute("sourceRef");
                    var targetRef = (string?)child.Attribute("targetRef");
                    if (sourceRef is null || targetRef is null)
                    {
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Sequence flow '{id}' is missing sourceRef/targetRef and was dropped.", id));
                        break;
                    }

                    var conditionOutcome = (string?)child.Attribute(BpmnXmlNames.Elsa + "conditionOutcome");
                    var conditionExpression = child.Element(BpmnXmlNames.Model + "conditionExpression");
                    if (conditionOutcome is null && conditionExpression is not null)
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Sequence flow '{id}' carries an expression condition ('{conditionExpression.Value.Trim()}'); expression conditions are not executable in this slice, so the flow imported as unconditional.", id));

                    flows.Add(new BpmnSequenceFlow(id, sourceRef, targetRef, name: NameOf(child), conditionOutcome: conditionOutcome));
                    break;
                }
                case "laneSet":
                {
                    foreach (var lane in child.Elements(BpmnXmlNames.Model + "lane"))
                    {
                        var laneId = IdOf(lane);
                        if (laneId is null) continue;
                        lanes.Add(new BpmnLane(laneId, name: NameOf(lane)));
                        foreach (var flowNodeRef in lane.Elements(BpmnXmlNames.Model + "flowNodeRef"))
                            context.LaneByElementId[flowNodeRef.Value.Trim()] = laneId;
                    }
                    break;
                }
                default:
                {
                    if (BpmnXmlNames.TaskLocalNamesToElementTypes.TryGetValue(localName, out var taskType))
                    {
                        if (id is null) break;
                        // Tasks import unbound (no ChildNodeId until an Elsa activity is bound), so a
                        // multi-instance task is a childless host on import → degrade (validate-representable).
                        elements.Add(new BpmnElement(id, taskType, name: NameOf(child), defaultFlowId: DefaultOf(child),
                            loopCharacteristics: ResolveLoopCharacteristics(child, id, hostBindsChild: false, declaredVariableNames, context),
                            isForCompensation: IsForCompensationOf(child)));
                        if (taskType != BpmnElementTypes.Task)
                            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Info, $"{Capitalize(localName)} '{id}' imported unbound; bind an Elsa activity to execute it.", id));
                        break;
                    }

                    if (BpmnXmlNames.GatewayLocalNamesToElementTypes.TryGetValue(localName, out var gatewayType))
                    {
                        if (id is null) break;
                        elements.Add(new BpmnElement(id, gatewayType, name: NameOf(child), defaultFlowId: DefaultOf(child)));
                        break;
                    }

                    if (localName is "documentation" or "extensionElements" or "incoming" or "outgoing")
                        break;

                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"BPMN element <{localName}>{(id is null ? "" : $" '{id}'")} is not supported by this slice and was dropped.", id));
                    break;
                }
            }
        }

        // Second pass: boundary events resolve against the now-complete host set (spec 120 D6 / spec 124 D4).
        var elementsById = elements.ToDictionary(element => element.ElementId, StringComparer.Ordinal);
        var referencedHandlerIds = new HashSet<string>(StringComparer.Ordinal);
        // spec 124: an element that participates in any sequence flow can never be a compensation handler
        // (handlers are flow-less by rule); excluding flow participants here keeps the importer
        // validate-representable — the boundary drops and the element stays an ordinary flow element.
        var flowParticipantIds = flows
            .SelectMany(flow => new[] { flow.SourceRef, flow.TargetRef })
            .ToHashSet(StringComparer.Ordinal);
        // spec 125 D4: at most one cancel boundary per transaction host; a second one drops with a finding.
        var transactionHostsWithCancelBoundary = new HashSet<string>(StringComparer.Ordinal);
        // spec 127 D4: distinct escalation codes per host and at most one code-less catch-all; a collision drops with a finding.
        var escalationCodesByHost = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var escalationCatchAllHosts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var boundaryXml in pendingBoundaries)
        {
            var resolved = ResolveBoundaryEvent(boundaryXml, elementsById, messageSignalNames, escalationDeclarations, associations, flowParticipantIds, transactionHostsWithCancelBoundary, escalationCodesByHost, escalationCatchAllHosts, context);
            if (resolved is not { } boundaryImport)
                continue; // Dropped (finding added inside); its sequence flows cascade-drop as unresolved refs.
            if (boundaryImport.Child is not null)
                childActivities.Add(boundaryImport.Child);
            elements.Add(boundaryImport.Boundary);

            // spec 124: a compensation boundary marks its resolved handler as a compensation handler.
            if (boundaryImport.Boundary.CompensationHandlerElementId is { } handlerId)
            {
                referencedHandlerIds.Add(handlerId);
                MarkHandlerForCompensation(elements, handlerId);
            }
        }

        // Third pass: compensate throw/end events resolve against the full element set + the compensation hosts
        // (elements carrying a compensation boundary) now known (spec 124 D4).
        var compensationHostIds = elements
            .Where(element => element.CompensationHandlerElementId is not null && element.AttachedToRef is not null)
            .Select(element => element.AttachedToRef!)
            .ToHashSet(StringComparer.Ordinal);
        var importedElementIds = elements.Select(element => element.ElementId).ToHashSet(StringComparer.Ordinal);
        foreach (var throwXml in pendingCompensateThrows)
        {
            if (ResolveCompensateThrow(throwXml, importedElementIds, compensationHostIds, context) is { } throwElement)
                elements.Add(throwElement);
        }
        foreach (var endXml in pendingCompensateEnds)
            elements.Add(ResolveCompensateEnd(endXml, importedElementIds, compensationHostIds, context));

        // spec 124: an isForCompensation activity referenced by no compensation boundary cannot ride normal flow —
        // drop it (and its bound child) with a finding.
        foreach (var orphan in elements.Where(element => element.IsForCompensation && !referencedHandlerIds.Contains(element.ElementId)).ToArray())
        {
            elements.Remove(orphan);
            if (orphan.ChildNodeId is { } orphanChildId)
                childActivities.RemoveAll(child => StringComparer.Ordinal.Equals(child.NodeId, orphanChildId));
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Activity '{orphan.ElementId}' is marked isForCompensation but is referenced by no compensation boundary; it cannot ride normal flow and was dropped.", orphan.ElementId));
        }

        var elementsWithLanes = elements
            .Select(element => context.LaneByElementId.TryGetValue(element.ElementId, out var laneId)
                ? new BpmnElement(element.ElementId, element.ElementType, element.Name, element.ChildNodeId, laneId, element.DefaultFlowId, element.EventDefinitions, element.Properties, element.AttachedToRef, element.CancelActivity, element.LoopCharacteristics, element.IsForCompensation, element.CompensationHandlerElementId, element.IsTransaction, element.TriggeredByEvent)
                : element)
            .ToArray();

        var elementIds = elementsWithLanes.Select(element => element.ElementId).ToHashSet(StringComparer.Ordinal);
        var connectedFlows = flows.Where(flow =>
        {
            var connected = elementIds.Contains(flow.SourceRef) && elementIds.Contains(flow.TargetRef);
            if (!connected)
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Sequence flow '{flow.FlowId}' references a dropped element and was dropped with it.", flow.FlowId));
            return connected;
        }).ToArray();

        // spec 122: cyclic sequence flows are executable (loop-back edges become loop-iteration keys), so a
        // cycle no longer degrades the import; the structural rules still constrain where a loop-back may land.

        var structure = new BpmnAuthoredStructure(
            activities: childActivities,
            elements: elementsWithLanes,
            sequenceFlows: connectedFlows,
            lanes: lanes,
            variables: declaredVariables,
            diagram: diagram,
            isTransaction: isTransaction);

        return new ActivityNode(
            nodeId,
            DefaultBpmnProcessActivityVersionId,
            [],
            [],
            new ActivityNodeStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions)));
    }

    private static JsonElement? ReadDiagram(XElement definitions)
    {
        var plane = definitions
            .Elements(BpmnXmlNames.Di + "BPMNDiagram")
            .Elements(BpmnXmlNames.Di + "BPMNPlane")
            .FirstOrDefault();
        if (plane is null) return null;

        var shapes = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var shape in plane.Elements(BpmnXmlNames.Di + "BPMNShape"))
        {
            var elementId = (string?)shape.Attribute("bpmnElement");
            var bounds = shape.Element(BpmnXmlNames.Dc + "Bounds");
            if (elementId is null || bounds is null) continue;
            shapes[elementId] = new
            {
                x = (double?)bounds.Attribute("x") ?? 0,
                y = (double?)bounds.Attribute("y") ?? 0,
                width = (double?)bounds.Attribute("width") ?? 0,
                height = (double?)bounds.Attribute("height") ?? 0
            };
        }

        var edges = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var edge in plane.Elements(BpmnXmlNames.Di + "BPMNEdge"))
        {
            var elementId = (string?)edge.Attribute("bpmnElement");
            if (elementId is null) continue;
            edges[elementId] = new
            {
                waypoints = edge.Elements(BpmnXmlNames.Dd + "waypoint")
                    .Select(waypoint => new { x = (double?)waypoint.Attribute("x") ?? 0, y = (double?)waypoint.Attribute("y") ?? 0 })
                    .ToArray()
            };
        }

        return JsonSerializer.SerializeToElement(new { shapes, edges }, SerializerOptions);
    }

    /// <summary>
    /// Root-level <c>&lt;message&gt;</c>/<c>&lt;signal&gt;</c> declarations index (id → name); a
    /// message/signal event definition resolves its <c>messageRef</c>/<c>signalRef</c> through this to
    /// the event name that drives the stimulus (spec 118 D3).
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadMessageSignalDeclarations(XElement definitions)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var declaration in definitions.Elements().Where(element =>
                     element.Name.Namespace == BpmnXmlNames.Model &&
                     element.Name.LocalName is "message" or "signal"))
        {
            var declarationId = IdOf(declaration);
            var name = NameOf(declaration);
            if (declarationId is not null && name is not null)
                result[declarationId] = name;
        }

        return result;
    }

    /// <summary>
    /// Root-level <c>&lt;escalation id name escalationCode&gt;</c> declarations index (spec 127 D4): an escalation
    /// event definition's <c>escalationRef</c> resolves through this to the matching code (falling back to the
    /// declaration's <c>name</c>, else the ref id) and the display name. Mirrors
    /// <see cref="ReadMessageSignalDeclarations"/>.
    /// </summary>
    private static IReadOnlyDictionary<string, EscalationDeclaration> ReadEscalationDeclarations(XElement definitions)
    {
        var result = new Dictionary<string, EscalationDeclaration>(StringComparer.Ordinal);
        foreach (var declaration in definitions.Elements(BpmnXmlNames.Model + "escalation"))
        {
            if (IdOf(declaration) is not { } declarationId)
                continue;
            var code = ((string?)declaration.Attribute("escalationCode"))?.Trim();
            var name = NameOf(declaration)?.Trim();
            result[declarationId] = new EscalationDeclaration(
                string.IsNullOrWhiteSpace(code) ? null : code,
                string.IsNullOrWhiteSpace(name) ? null : name);
        }

        return result;
    }

    /// <summary>
    /// Resolves an <c>escalationEventDefinition</c>'s <c>escalationRef</c> to its matching code (spec 127 D4): the
    /// declaration's <c>escalationCode</c> → the declaration's <c>name</c> → the ref id itself. Returns <c>null</c>
    /// when the definition carries no <c>escalationRef</c> (a ref-less throw/end degrades; a ref-less boundary is
    /// the catch-all).
    /// </summary>
    private static string? ResolveEscalationRefCode(XElement definition, IReadOnlyDictionary<string, EscalationDeclaration> escalationDeclarations)
    {
        if (((string?)definition.Attribute("escalationRef"))?.Trim() is not { Length: > 0 } escalationRef)
            return null;

        if (escalationDeclarations.TryGetValue(escalationRef, out var declaration))
            return declaration.Code ?? declaration.Name ?? escalationRef;

        return escalationRef;
    }

    /// <summary>
    /// Resolves an escalation intermediate throw event (spec 127 D4): a resolvable <c>escalationRef</c> keeps the
    /// throw with its matched code (+ the declaration name); a ref-less throw is Dropped with a finding (its flows
    /// cascade-drop as unresolved references), so the importer never emits a graph the validator rejects.
    /// </summary>
    private static BpmnElement? ResolveEscalationThrow(string id, XElement element, XElement definition, IReadOnlyDictionary<string, EscalationDeclaration> escalationDeclarations, ImportContext context)
    {
        var code = ResolveEscalationRefCode(definition, escalationDeclarations);
        if (code is null)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Escalation throw event '{id}' declares no escalationRef; a throw must say what it escalates, so it was dropped.", id));
            return null;
        }

        return new BpmnElement(id, BpmnElementTypes.IntermediateThrowEvent, name: NameOf(element),
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, EscalationProperties(code, ResolveEscalationRefName(definition, escalationDeclarations)))]);
    }

    /// <summary>
    /// Resolves an escalation end event (spec 127 D4): a resolvable <c>escalationRef</c> keeps the escalation end
    /// with its matched code; a ref-less end degrades to a none end event with a finding (an end has no flows to
    /// cascade).
    /// </summary>
    private static BpmnElement ResolveEscalationEnd(string id, XElement element, XElement definition, IReadOnlyDictionary<string, EscalationDeclaration> escalationDeclarations, ImportContext context)
    {
        var code = ResolveEscalationRefCode(definition, escalationDeclarations);
        if (code is null)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Escalation end event '{id}' declares no escalationRef; a throw must say what it escalates, so it imported as a none end event.", id));
            return new BpmnElement(id, BpmnElementTypes.EndEvent, name: NameOf(element));
        }

        return new BpmnElement(id, BpmnElementTypes.EndEvent, name: NameOf(element),
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, EscalationProperties(code, ResolveEscalationRefName(definition, escalationDeclarations)))]);
    }

    /// <summary>
    /// Validates an event subprocess's imported body shape and per-scope uniqueness (spec 128 D7). The body must
    /// declare exactly one start event with exactly one supported trigger definition — escalation (with optional
    /// code; code-less = catch-all) or error (interrupting only) — with per scope distinct escalation codes, at most
    /// one code-less catch-all, and at most one error event subprocess. Any violation Drops the event subprocess with
    /// a specific finding so the importer never emits a graph the validator rejects; a valid trigger updates the
    /// scope trackers and returns <c>true</c>.
    /// </summary>
    private static bool TryResolveEventSubprocess(
        string id,
        ActivityNode bodyNode,
        HashSet<string> escalationCodes,
        ref bool hasEscalationCatchAll,
        ref bool hasError,
        ImportContext context)
    {
        var bodyStructure = bodyNode.Structure?.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions);
        var starts = (bodyStructure?.Elements ?? [])
            .Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.StartEvent))
            .ToArray();
        if (starts.Length != 1)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Event subprocess '{id}' body must declare exactly one start event; it declares {starts.Length}. It was dropped.", id));
            return false;
        }

        var start = starts[0];
        if (start.EventDefinitions.Count != 1)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Event subprocess '{id}' body start event must declare exactly one supported trigger definition (escalation or error); it declares {start.EventDefinitions.Count}. It was dropped.", id));
            return false;
        }

        var definition = start.EventDefinitions.Single();
        var interrupting = start.CancelActivity;
        if (StringComparer.Ordinal.Equals(definition.Type, BpmnEventDefinitionTypes.Escalation))
        {
            var code = definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Code, out var codeValue) && !string.IsNullOrWhiteSpace(codeValue) ? codeValue.Trim() : null;
            if (code is null)
            {
                if (hasEscalationCatchAll)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Event subprocess '{id}' is a second code-less catch-all escalation event subprocess in its scope, which may carry at most one; it was dropped.", id));
                    return false;
                }
                hasEscalationCatchAll = true;
            }
            else if (!escalationCodes.Add(code))
            {
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Event subprocess '{id}' declares escalation code '{code}', which another event subprocess in its scope already claims; it was dropped.", id));
                return false;
            }

            return true;
        }

        if (StringComparer.Ordinal.Equals(definition.Type, BpmnEventDefinitionTypes.Error))
        {
            if (!interrupting)
            {
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Event subprocess '{id}' is a non-interrupting error event subprocess, which is not meaningful; it was dropped.", id));
                return false;
            }
            if (hasError)
            {
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Event subprocess '{id}' is a second error event subprocess in its scope, which may carry at most one; it was dropped.", id));
                return false;
            }
            hasError = true;
            return true;
        }

        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Event subprocess '{id}' body start event declares an unsupported trigger definition '{definition.Type}'; only escalation and error triggers are supported (tier 1). It was dropped.", id));
        return false;
    }

    /// <summary>The display name an escalation ref resolves to (the declaration's <c>name</c>), or <c>null</c>.</summary>
    private static string? ResolveEscalationRefName(XElement definition, IReadOnlyDictionary<string, EscalationDeclaration> escalationDeclarations) =>
        ((string?)definition.Attribute("escalationRef"))?.Trim() is { Length: > 0 } escalationRef
        && escalationDeclarations.TryGetValue(escalationRef, out var declaration)
            ? declaration.Name
            : null;

    /// <summary>Builds the escalation event-definition properties (spec 127): the required <c>code</c> plus the optional display <c>name</c>.</summary>
    private static IReadOnlyDictionary<string, string> EscalationProperties(string code, string? name)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal) { [BpmnEventDefinitionProperties.Code] = code };
        if (!string.IsNullOrWhiteSpace(name))
            properties[BpmnEventDefinitionProperties.Name] = name.Trim();
        return properties;
    }

    /// <summary>A root escalation declaration (spec 127 D4): the optional explicit code and display name.</summary>
    private readonly record struct EscalationDeclaration(string? Code, string? Name);

    /// <summary>
    /// Resolves the single event definition of an event-defined start element (spec 117/118 D2/D3) into a
    /// populated <see cref="BpmnEventDefinition"/>, or returns <c>null</c> and reports a <c>Degraded</c>
    /// finding (importing a plain none start) when the definition set is unusable.
    /// </summary>
    private static BpmnEventDefinition? ResolveStartEventDefinition(string id, IReadOnlyList<XElement> definitions, IReadOnlyDictionary<string, string> messageSignalNames, ImportContext context)
    {
        if (definitions.Count != 1)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Start event '{id}' declares {definitions.Count} event definitions; only a single timer/message/signal definition is supported, so it imported as a none start event.", id));
            return null;
        }

        var definition = definitions[0];
        switch (definition.Name.LocalName)
        {
            case "messageEventDefinition":
            case "signalEventDefinition":
            {
                var type = definition.Name.LocalName == "messageEventDefinition" ? BpmnEventDefinitionTypes.Message : BpmnEventDefinitionTypes.Signal;
                var name = ResolveMessageSignalName(definition, type, messageSignalNames);
                if (name is null)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Start event '{id}' declares a {type} event definition with no resolvable name (missing or unresolvable {type}Ref); it imported as a none start event.", id));
                    return null;
                }

                return new BpmnEventDefinition(type, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name });
            }
            case "timerEventDefinition":
            {
                var properties = ResolveStartTimerProperties(definition);
                if (properties is null)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Start event '{id}' declares a timer event definition that is not a recurring schedule; only a <timeCycle> interval/cron start is supported, so it imported as a none start event.", id));
                    return null;
                }

                return new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer, properties);
            }
            default:
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Start event '{id}' declares an unsupported '{definition.Name.LocalName}'; only timer/message/signal start events are supported, so it imported as a none start event.", id));
                return null;
        }
    }

    /// <summary>
    /// Resolves an intermediate catch event (spec 116/118 D1) into a populated definition plus a
    /// synthesized bound suspending child, or returns <c>null</c> and reports a <c>Dropped</c> finding when
    /// the catch cannot form a valid graph (its sequence flows then cascade-drop as unresolved references).
    /// </summary>
    private static (BpmnEventDefinition Definition, ActivityNode Child)? ResolveCatchEvent(string id, XElement element, IReadOnlyDictionary<string, string> messageSignalNames, ImportContext context)
    {
        var definitions = element.Elements().Where(IsEventDefinition).ToArray();
        if (definitions.Length != 1)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Intermediate catch event '{id}' declares {definitions.Length} event definitions; exactly one timer/message/signal definition is required, so it was dropped.", id));
            return null;
        }

        var definition = definitions[0];
        var childNodeId = $"node-{id}";
        switch (definition.Name.LocalName)
        {
            case "messageEventDefinition":
            case "signalEventDefinition":
            {
                var type = definition.Name.LocalName == "messageEventDefinition" ? BpmnEventDefinitionTypes.Message : BpmnEventDefinitionTypes.Signal;
                var name = ResolveMessageSignalName(definition, type, messageSignalNames);
                if (name is null)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Intermediate catch event '{id}' declares a {type} event definition with no resolvable name (missing or unresolvable {type}Ref), so it was dropped.", id));
                    return null;
                }

                var eventChild = BuildEventCatchChild(childNodeId, name);
                return (new BpmnEventDefinition(type, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name }), eventChild);
            }
            case "timerEventDefinition":
            {
                var duration = ResolveCatchTimerDuration(definition);
                if (duration is null)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Intermediate catch event '{id}' declares a timer event definition without a <timeDuration>; only a one-shot duration catch timer is supported, so it was dropped.", id));
                    return null;
                }

                var delayChild = BuildDelayCatchChild(childNodeId, duration);
                return (new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Interval] = duration }), delayChild);
            }
            default:
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Intermediate catch event '{id}' declares an unsupported '{definition.Name.LocalName}'; only timer/message/signal catch events are supported, so it was dropped.", id));
                return null;
        }
    }

    /// <summary>
    /// Resolves a boundary event (spec 120 D6) into its element plus, for a catch boundary, a synthesized bound
    /// listener child, or returns <c>null</c> and reports a <c>Dropped</c> finding when it cannot form a
    /// validator-representable boundary (unresolvable/childless host, unsupported definition, non-interrupting
    /// error boundary). Its sequence flows then cascade-drop as unresolved references.
    /// </summary>
    private static (BpmnElement Boundary, ActivityNode? Child)? ResolveBoundaryEvent(
        XElement element,
        IReadOnlyDictionary<string, BpmnElement> elementsById,
        IReadOnlyDictionary<string, string> messageSignalNames,
        IReadOnlyDictionary<string, EscalationDeclaration> escalationDeclarations,
        IReadOnlyList<(string Source, string Target)> associations,
        IReadOnlySet<string> flowParticipantIds,
        HashSet<string> transactionHostsWithCancelBoundary,
        Dictionary<string, HashSet<string>> escalationCodesByHost,
        HashSet<string> escalationCatchAllHosts,
        ImportContext context)
    {
        var id = IdOf(element)!;
        var attachedToRef = ((string?)element.Attribute("attachedToRef"))?.Trim();
        if (string.IsNullOrWhiteSpace(attachedToRef))
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' declares no attachedToRef host and was dropped.", id));
            return null;
        }

        if (!elementsById.TryGetValue(attachedToRef, out var host))
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' is attached to '{attachedToRef}', which is not an imported element, and was dropped.", id));
            return null;
        }

        var hostIsTaskFamily = BpmnXmlNames.TaskLocalNamesToElementTypes.Values.Contains(host.ElementType, StringComparer.Ordinal);
        if (!hostIsTaskFamily && !StringComparer.Ordinal.Equals(host.ElementType, BpmnElementTypes.SubProcess))
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' is attached to '{attachedToRef}' ({host.ElementType}), which is not a task-family or subprocess host, and was dropped.", id));
            return null;
        }

        if (host.ChildNodeId is null)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' is attached to host '{attachedToRef}', which has no bound child activity to host a boundary, and was dropped.", id));
            return null;
        }

        var definitions = element.Elements().Where(IsEventDefinition).ToArray();
        if (definitions.Length != 1)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' declares {definitions.Length} event definitions; exactly one timer/message/signal/error definition is required, so it was dropped.", id));
            return null;
        }

        var cancelActivity = (bool?)element.Attribute("cancelActivity") ?? true;
        var definition = definitions[0];
        var childNodeId = $"node-{id}";
        switch (definition.Name.LocalName)
        {
            case "compensateEventDefinition":
            {
                // spec 124 D4: a compensation boundary resolves its handler via a boundary↔activity association
                // (either direction). No resolvable association, or a handler that is not an importable
                // task-family/subprocess binding a child, drops the boundary (validate-representable). cancelActivity
                // is imported as authored but ignored, so it is not read here.
                var handlerId = ResolveCompensationHandler(id, associations, elementsById, flowParticipantIds);
                if (handlerId is null)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Compensation boundary event '{id}' has no association to an importable, flow-less compensation handler activity and was dropped.", id));
                    return null;
                }

                var compensationElement = new BpmnElement(id, BpmnElementTypes.BoundaryEvent, name: NameOf(element),
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation)],
                    attachedToRef: attachedToRef, cancelActivity: cancelActivity,
                    compensationHandlerElementId: handlerId);
                return (compensationElement, null);
            }
            case "cancelEventDefinition":
            {
                // spec 125 D4: a cancel boundary attaches only to a transaction host, at most one per host.
                // cancelActivity is imported as authored but ignored (the host is finished when it fires).
                if (!host.IsTransaction)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Cancel boundary event '{id}' is attached to '{attachedToRef}', which is not a transaction; it was dropped.", id));
                    return null;
                }

                if (!transactionHostsWithCancelBoundary.Add(attachedToRef))
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Cancel boundary event '{id}' is a second cancel boundary on transaction '{attachedToRef}', which may carry at most one; it was dropped.", id));
                    return null;
                }

                var cancelElement = new BpmnElement(id, BpmnElementTypes.BoundaryEvent, name: NameOf(element),
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Cancel)],
                    attachedToRef: attachedToRef, cancelActivity: cancelActivity);
                return (cancelElement, null);
            }
            case "escalationEventDefinition":
            {
                // spec 127 D4: an escalation boundary attaches only to a subprocess host (a task host is dead by
                // construction — its bound child is a leaf that can never escalate). A ref-less boundary is the
                // code-less catch-all; a code collision or a second catch-all on one host drops with a finding.
                if (!StringComparer.Ordinal.Equals(host.ElementType, BpmnElementTypes.SubProcess))
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Escalation boundary event '{id}' is attached to '{attachedToRef}' ({host.ElementType}), which is not a subprocess host; it was dropped.", id));
                    return null;
                }

                var code = ResolveEscalationRefCode(definition, escalationDeclarations);
                if (code is null)
                {
                    if (!escalationCatchAllHosts.Add(attachedToRef))
                    {
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Escalation boundary event '{id}' is a second code-less catch-all on host '{attachedToRef}', which may carry at most one; it was dropped.", id));
                        return null;
                    }
                }
                else
                {
                    var codes = escalationCodesByHost.TryGetValue(attachedToRef, out var existing) ? existing : escalationCodesByHost[attachedToRef] = new HashSet<string>(StringComparer.Ordinal);
                    if (!codes.Add(code))
                    {
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Escalation boundary event '{id}' declares code '{code}', which another escalation boundary on host '{attachedToRef}' already claims; it was dropped.", id));
                        return null;
                    }
                }

                var escalationProperties = code is null ? null : new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = code };
                var escalationElement = new BpmnElement(id, BpmnElementTypes.BoundaryEvent, name: NameOf(element),
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, escalationProperties)],
                    attachedToRef: attachedToRef, cancelActivity: cancelActivity);
                return (escalationElement, null);
            }
            case "errorEventDefinition":
            {
                if (!cancelActivity)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' is a non-interrupting error boundary, which is not meaningful; it was dropped.", id));
                    return null;
                }

                var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                if (((string?)definition.Attribute("errorRef"))?.Trim() is { Length: > 0 } errorRef)
                    properties["bpmn.errorRef"] = errorRef; // recorded for future error-code matching; not read this slice.
                var errorElement = new BpmnElement(id, BpmnElementTypes.BoundaryEvent, name: NameOf(element),
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error)],
                    properties: properties.Count == 0 ? null : properties,
                    attachedToRef: attachedToRef, cancelActivity: true);
                return (errorElement, null);
            }
            case "messageEventDefinition":
            case "signalEventDefinition":
            {
                var type = definition.Name.LocalName == "messageEventDefinition" ? BpmnEventDefinitionTypes.Message : BpmnEventDefinitionTypes.Signal;
                var name = ResolveMessageSignalName(definition, type, messageSignalNames);
                if (name is null)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' declares a {type} event definition with no resolvable name (missing or unresolvable {type}Ref), so it was dropped.", id));
                    return null;
                }

                var eventChild = BuildEventCatchChild(childNodeId, name);
                var messageElement = new BpmnElement(id, BpmnElementTypes.BoundaryEvent, name: NameOf(element), childNodeId: childNodeId,
                    eventDefinitions: [new BpmnEventDefinition(type, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name })],
                    attachedToRef: attachedToRef, cancelActivity: cancelActivity);
                return (messageElement, eventChild);
            }
            case "timerEventDefinition":
            {
                var duration = ResolveCatchTimerDuration(definition);
                if (duration is null)
                {
                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' declares a timer event definition without a <timeDuration>; only a one-shot duration boundary timer is supported, so it was dropped.", id));
                    return null;
                }

                var delayChild = BuildDelayCatchChild(childNodeId, duration);
                var timerElement = new BpmnElement(id, BpmnElementTypes.BoundaryEvent, name: NameOf(element), childNodeId: childNodeId,
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Interval] = duration })],
                    attachedToRef: attachedToRef, cancelActivity: cancelActivity);
                return (timerElement, delayChild);
            }
            default:
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Boundary event '{id}' declares an unsupported '{definition.Name.LocalName}'; only timer/message/signal/error/escalation/compensation/cancel boundary events are supported, so it was dropped.", id));
                return null;
        }
    }

    /// <summary>
    /// Resolves a compensation boundary's handler element id via a boundary↔activity association (spec 124 D4):
    /// an association with one endpoint at the boundary and the other at an importable task-family/subprocess that
    /// binds a child (a childless task cannot host a handler) and participates in no sequence flow (a handler is
    /// flow-less by rule — a flow participant stays an ordinary flow element so the importer never emits a graph
    /// the validator rejects). Either association direction is accepted; <c>null</c> when no such
    /// association/handler exists.
    /// </summary>
    private static string? ResolveCompensationHandler(
        string boundaryId,
        IReadOnlyList<(string Source, string Target)> associations,
        IReadOnlyDictionary<string, BpmnElement> elementsById,
        IReadOnlySet<string> flowParticipantIds)
    {
        foreach (var (source, target) in associations)
        {
            var other = StringComparer.Ordinal.Equals(source, boundaryId) ? target
                : StringComparer.Ordinal.Equals(target, boundaryId) ? source
                : null;
            if (other is null || !elementsById.TryGetValue(other, out var candidate) || flowParticipantIds.Contains(other))
                continue;
            var isTaskFamily = BpmnXmlNames.TaskLocalNamesToElementTypes.Values.Contains(candidate.ElementType, StringComparer.Ordinal);
            var isSubProcess = StringComparer.Ordinal.Equals(candidate.ElementType, BpmnElementTypes.SubProcess);
            if ((isTaskFamily || isSubProcess) && candidate.ChildNodeId is not null)
                return other;
        }

        return null;
    }

    /// <summary>Marks a resolved compensation handler element as <c>IsForCompensation</c> in place (spec 124 D4); idempotent.</summary>
    private static void MarkHandlerForCompensation(List<BpmnElement> elements, string handlerId)
    {
        var index = elements.FindIndex(element => StringComparer.Ordinal.Equals(element.ElementId, handlerId));
        if (index < 0 || elements[index].IsForCompensation)
            return;
        var handler = elements[index];
        elements[index] = new BpmnElement(handler.ElementId, handler.ElementType, handler.Name, handler.ChildNodeId, handler.LaneId,
            handler.DefaultFlowId, handler.EventDefinitions, handler.Properties, handler.AttachedToRef, handler.CancelActivity,
            handler.LoopCharacteristics, isForCompensation: true, compensationHandlerElementId: handler.CompensationHandlerElementId,
            isTransaction: handler.IsTransaction, triggeredByEvent: handler.TriggeredByEvent);
    }

    /// <summary>
    /// Resolves a compensate intermediate throw event (spec 124 D4). A ref-less throw compensates everything; an
    /// <c>activityRef</c> keeps the throw only when it names an existing element with an attached compensation
    /// boundary — otherwise the throw imports WITHOUT the compensate definition and is Dropped with a finding (its
    /// flows cascade-drop as unresolved references), so the importer never emits a graph the validator rejects.
    /// </summary>
    private static BpmnElement? ResolveCompensateThrow(
        XElement element,
        IReadOnlySet<string> importedElementIds,
        IReadOnlySet<string> compensationHostIds,
        ImportContext context)
    {
        var id = IdOf(element)!;
        var definitions = element.Elements().Where(IsEventDefinition).ToArray();
        if (definitions.Length != 1 || definitions[0].Name.LocalName != "compensateEventDefinition")
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Intermediate throw event '{id}' does not declare exactly one compensate event definition; only compensate throw events are supported, so it was dropped.", id));
            return null;
        }

        var activityRef = ((string?)definitions[0].Attribute("activityRef"))?.Trim();
        if (activityRef is { Length: > 0 } && !(importedElementIds.Contains(activityRef) && compensationHostIds.Contains(activityRef)))
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Compensate throw event '{id}' targets activityRef '{activityRef}', which is not an element with an attached compensation boundary; it was dropped.", id));
            return null;
        }

        return new BpmnElement(id, BpmnElementTypes.IntermediateThrowEvent, name: NameOf(element),
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation, CompensationProperties(activityRef))]);
    }

    /// <summary>
    /// Resolves a compensate end event (spec 124 D4). A ref-less end compensates everything; an unresolvable
    /// <c>activityRef</c> degrades the end event to a plain none end event (which never rejects the graph) with a
    /// finding rather than dropping it (an end event has no flows to cascade).
    /// </summary>
    private static BpmnElement ResolveCompensateEnd(
        XElement element,
        IReadOnlySet<string> importedElementIds,
        IReadOnlySet<string> compensationHostIds,
        ImportContext context)
    {
        var id = IdOf(element)!;
        var activityRef = ((string?)element.Element(BpmnXmlNames.Model + "compensateEventDefinition")?.Attribute("activityRef"))?.Trim();
        if (activityRef is { Length: > 0 } && !(importedElementIds.Contains(activityRef) && compensationHostIds.Contains(activityRef)))
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Compensate end event '{id}' targets activityRef '{activityRef}', which is not an element with an attached compensation boundary; it imported as a none end event.", id));
            return new BpmnElement(id, BpmnElementTypes.EndEvent, name: NameOf(element));
        }

        return new BpmnElement(id, BpmnElementTypes.EndEvent, name: NameOf(element),
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation, CompensationProperties(activityRef))]);
    }

    private static IReadOnlyDictionary<string, string>? CompensationProperties(string? activityRef) =>
        string.IsNullOrWhiteSpace(activityRef)
            ? null
            : new Dictionary<string, string> { [BpmnEventDefinitionProperties.ActivityRef] = activityRef.Trim() };

    private static bool IsForCompensationOf(XElement element) =>
        (bool?)element.Attribute("isForCompensation") ?? false;

    /// <summary>
    /// Resolves a task/subprocess element's <c>&lt;multiInstanceLoopCharacteristics&gt;</c> (spec 121 D4):
    /// <c>isSequential</c> + an integer literal <c>&lt;loopCardinality&gt;</c> → a cardinality
    /// <see cref="BpmnLoopCharacteristics"/>. Collection mode (via the elsa-namespaced
    /// <c>elsa:collection</c>/<c>elsa:itemVariable</c> attributes), a non-integer/missing cardinality, a
    /// standard-loop/completion/data-input form, or a host that binds no child on import all
    /// <b>Degrade</b> (the element imports WITHOUT loop characteristics) with a finding, so the importer stays
    /// validate-representable (it never emits a loop the graph validator would reject).
    /// </summary>
    /// <summary>
    /// Reads a process/subprocess container's declared container-scoped variables (spec 123 D3) from its
    /// elsa-namespaced <c>&lt;extensionElements&gt;&lt;elsa:variable name="…"/&gt;</c> declarations — the
    /// vendor-extension representation for BPMN's out-of-band variable model, mirroring the exporter. Only the
    /// name is load-bearing for the collection-loop guard and round-trip; the type is a neutral default.
    /// </summary>
    private static IReadOnlyCollection<VariableDefinition> ReadDeclaredVariables(XElement container)
    {
        var extensions = container.Element(BpmnXmlNames.Model + "extensionElements");
        if (extensions is null)
            return [];

        var variables = new List<VariableDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in extensions.Elements(BpmnXmlNames.Elsa + "variable"))
        {
            if (((string?)declaration.Attribute("name"))?.Trim() is not { Length: > 0 } name || !seen.Add(name))
                continue;
            variables.Add(new VariableDefinition(name, name, new TypeReference("Object"), StorageDriverType: null, Default: null));
        }

        return variables;
    }

    private static BpmnLoopCharacteristics? ResolveLoopCharacteristics(XElement element, string id, bool hostBindsChild, IReadOnlySet<string> declaredVariableNames, ImportContext context)
    {
        if (element.Element(BpmnXmlNames.Model + "multiInstanceLoopCharacteristics") is not { } loop)
        {
            // Standard (while/until) loops are a stated cut; report them so the drop is visible.
            if (element.Element(BpmnXmlNames.Model + "standardLoopCharacteristics") is not null)
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Element '{id}' declares standardLoopCharacteristics, which is not supported by this slice; it imported without loop characteristics.", id));
            return null;
        }

        if (!hostBindsChild)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Element '{id}' declares multi-instance loop characteristics but binds no child on import (only a bound subprocess can host a loop); it imported without loop characteristics.", id));
            return null;
        }

        var isSequential = (bool?)loop.Attribute("isSequential") ?? false;

        // spec 123 D3: a collection multi-instance imports as a real collection-mode loop when the named variable
        // is a declared container-scoped variable of this process and the item variable is not the reserved
        // loopIndex key; an undeclared/empty name or a reserved item variable degrades (never emitting a loop the
        // graph validator would reject).
        if (((string?)loop.Attribute(BpmnXmlNames.Elsa + "collection"))?.Trim() is { Length: > 0 } collection)
        {
            if (!declaredVariableNames.Contains(collection))
            {
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Element '{id}' declares a collection multi-instance over '{collection}', which is not a declared container-scoped variable of the process; it imported without loop characteristics.", id));
                return null;
            }

            var itemVariable = ((string?)loop.Attribute(BpmnXmlNames.Elsa + "itemVariable"))?.Trim() is { Length: > 0 } authoredItem
                ? authoredItem
                : BpmnLoopCharacteristics.DefaultItemVariable;
            if (StringComparer.Ordinal.Equals(itemVariable, BpmnLoopCharacteristics.LoopIndexVariable))
            {
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Element '{id}' declares a collection multi-instance whose item variable is the reserved '{BpmnLoopCharacteristics.LoopIndexVariable}' key; it imported without loop characteristics.", id));
                return null;
            }

            return new BpmnLoopCharacteristics(isSequential: isSequential, collectionVariable: collection, itemVariable: itemVariable);
        }

        var cardinalityText = loop.Element(BpmnXmlNames.Model + "loopCardinality")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(cardinalityText) ||
            !int.TryParse(cardinalityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cardinality) ||
            cardinality < 1)
        {
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Element '{id}' declares multi-instance loop characteristics without a positive integer <loopCardinality>; only a literal cardinality is executable in this slice, so it imported without loop characteristics.", id));
            return null;
        }

        return new BpmnLoopCharacteristics(isSequential: isSequential, cardinality: cardinality);
    }

    /// <summary>Resolves a message/signal event name through the root-declaration index; <c>null</c> when the ref is missing, unresolvable, or names a blank declaration.</summary>
    private static string? ResolveMessageSignalName(XElement definition, string type, IReadOnlyDictionary<string, string> messageSignalNames)
    {
        var refAttribute = type == BpmnEventDefinitionTypes.Message ? "messageRef" : "signalRef";
        if ((string?)definition.Attribute(refAttribute) is not { } reference)
            return null;
        if (!messageSignalNames.TryGetValue(reference.Trim(), out var name) || string.IsNullOrWhiteSpace(name))
            return null;
        return name.Trim();
    }

    /// <summary>
    /// Maps a start timer's <c>&lt;timeCycle&gt;</c> to the recurring interval-xor-cron properties (spec 118 D3):
    /// a <c>'P'</c>/<c>'R'</c>-prefixed text is an ISO-8601 duration interval (any leading <c>R…/</c> repetition
    /// prefix stripped), otherwise a cron expression. A non-recurring <c>&lt;timeDuration&gt;</c>/<c>&lt;timeDate&gt;</c>
    /// start, or an empty cycle, returns <c>null</c> (degrade to none).
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ResolveStartTimerProperties(XElement definition)
    {
        if (definition.Element(BpmnXmlNames.Model + "timeCycle") is not { } timeCycle)
            return null;

        var text = timeCycle.Value.Trim();
        if (text.Length == 0)
            return null;

        if (text[0] is 'P' or 'R')
        {
            var interval = StripRepetitionPrefix(text);
            return interval.Length == 0
                ? null
                : new Dictionary<string, string> { [BpmnEventDefinitionProperties.Interval] = interval };
        }

        return new Dictionary<string, string> { [BpmnEventDefinitionProperties.Cron] = text };
    }

    /// <summary>Maps a catch timer's <c>&lt;timeDuration&gt;</c> (ISO-8601 duration) to the interval/delay text; <c>null</c> for a <c>&lt;timeCycle&gt;</c>/<c>&lt;timeDate&gt;</c> catch (drop).</summary>
    private static string? ResolveCatchTimerDuration(XElement definition)
    {
        if (definition.Element(BpmnXmlNames.Model + "timeDuration") is not { } timeDuration)
            return null;

        var text = timeDuration.Value.Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Strips an ISO-8601 repetition prefix (<c>R…/</c>) from a recurring cycle, leaving the bare duration.</summary>
    private static string StripRepetitionPrefix(string text)
    {
        if (text[0] != 'R')
            return text;
        var slash = text.IndexOf('/');
        return slash >= 0 ? text[(slash + 1)..].Trim() : text;
    }

    /// <summary>A timer catch event's synthesized child: the durable <see cref="Elsa.Activities.Scheduling.Activities.Delay"/> (its <c>Duration</c> literal is the ISO-8601 duration).</summary>
    private static ActivityNode BuildDelayCatchChild(string nodeId, string isoDuration) =>
        new(nodeId, DefaultDelayActivityVersionId, [LiteralArgument("Duration", isoDuration)], []);

    /// <summary>A message/signal catch event's synthesized child: a mid-flow <c>Event</c> wait (<c>CanStartWorkflow = false</c>).</summary>
    private static ActivityNode BuildEventCatchChild(string nodeId, string eventName) =>
        new(nodeId, DefaultEventActivityVersionId, [LiteralArgument("EventName", eventName), LiteralArgument("CanStartWorkflow", false)], []);

    /// <summary>Authors a single literal input binding for a synthesized child node.</summary>
    private static ArgumentState LiteralArgument(string key, object? value) =>
        new(key, new ArgumentValue(value, "Literal"), null, null, null, null);

    private static bool IsEventDefinition(XElement element) =>
        element.Name.Namespace == BpmnXmlNames.Model && element.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal);

    private static string? IdOf(XElement element) => (string?)element.Attribute("id");
    private static string? NameOf(XElement element) => (string?)element.Attribute("name");
    private static string? DefaultOf(XElement element) => (string?)element.Attribute("default");

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private sealed class ImportContext
    {
        public List<string> ProcessIds { get; } = [];
        public List<BpmnImportIssue> Issues { get; } = [];
        public Dictionary<string, string> LaneByElementId { get; } = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _elementCounts = new(StringComparer.Ordinal);

        public void CountElement(string localName)
        {
            _elementCounts[localName] = (_elementCounts.TryGetValue(localName, out var count) ? count : 0) + 1;
        }

        public BpmnImportAnalysis ToAnalysis() => new(ProcessIds, _elementCounts, Issues);
    }
}
