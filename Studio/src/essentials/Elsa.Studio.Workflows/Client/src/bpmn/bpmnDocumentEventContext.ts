import { bpmnNamespaces, resolveBpmnDocumentReference, type ParsedBpmnDocument } from "./bpmnDocument";
import { assessBpmnBoundaryAttachment, bpmnBoolean, bpmnEventGatewayInputs, bpmnFlowScope } from "./bpmnDocumentPolicy";
import { bpmnEventDefinitionTypes, isEventElementType } from "./bpmnNotationTypes";
import { bpmnSourceAddress, isBpmnOwnedSyntax, refuseBpmnOperation } from "./bpmnDocumentOperationXml";
import { bpmnDocumentToolboxShapes } from "./bpmnDocumentToolbox";
import { readBpmnBooleanAttribute } from "./bpmnXmlBoolean";

const starts = bpmnDocumentToolboxShapes.filter(tool => tool.type === "startEvent");
const ordinaryTriggers = new Set(starts.filter(tool => !tool.eventSubprocessOnly).flatMap(tool => tool.eventDefinitions ?? []));
const eventBodyTriggers = new Set(starts.filter(tool => tool.eventSubprocessOnly).flatMap(tool => tool.eventDefinitions ?? []));

/** Typed transformations check changed contexts only; unrelated unfinished raw draft syntax stays retained. */
export function verifyBpmnEventContexts(before: ParsedBpmnDocument, after: ParsedBpmnDocument, rootIds: readonly string[]): void {
  const roots = rootIds.flatMap(id => after.byId.has(id) ? [after.byId.get(id)!] : []);
  const inside = (element: Element | undefined) => !!element && roots.some(root => root === element || root.contains(element));
  const host = (parsed: ParsedBpmnDocument, event: Element) => event.localName === "boundaryEvent"
    ? resolveBpmnDocumentReference(parsed, event, event.getAttribute("attachedToRef")) : undefined;
  const topology = (parsed: ParsedBpmnDocument, attached: Element | undefined) => bpmnEventGatewayInputs(parsed, attached)
    .map(flow => [flow.getAttribute("id"), resolveBpmnDocumentReference(parsed, flow, flow.getAttribute("sourceRef"))?.getAttribute("id")]);
  const signature = (parsed: ParsedBpmnDocument, event: Element) => {
    const scope = bpmnFlowScope(event), attached = host(parsed, event), hostScope = attached && bpmnFlowScope(attached);
    return JSON.stringify([scope?.getAttribute("id"), scope?.localName, scope && bpmnBoolean(scope, "triggeredByEvent"),
      attached?.getAttribute("id"), attached?.localName, hostScope?.getAttribute("id"),
      attached && bpmnBoolean(attached, "triggeredByEvent"), attached && bpmnBoolean(attached, "isForCompensation"),
      bpmnEventDefinitionTypes(event), readBpmnBooleanAttribute(event, "isInterrupting"), topology(parsed, attached)]);
  };
  const previous = (element: Element): Element | undefined => {
    const address = bpmnSourceAddress(element, after);
    let found = address.documentRoot ? before.document.documentElement : before.byId.get(address.elementId!);
    for (const step of address.path ?? []) {
      found = found && [...found.children].filter(child => (child.namespaceURI ?? "") === step.namespaceURI && child.localName === step.localName)[step.index ?? 0];
    }
    return found?.namespaceURI === element.namespaceURI && found.localName === element.localName ? found : undefined;
  };
  for (const event of after.document.getElementsByTagNameNS(bpmnNamespaces.model, "*")) {
    if (!isBpmnOwnedSyntax(event) || !isEventElementType(event.localName)) continue;
    const old = previous(event);
    const attached = host(after, event);
    if (!inside(event) && !inside(attached) && !(old && JSON.stringify(topology(before, host(before, old))) !== JSON.stringify(topology(after, attached)))) continue;
    if (old && signature(before, old) === signature(after, event)) continue;
    const scope = bpmnFlowScope(event), definitions = bpmnEventDefinitionTypes(event);
    if (event.localName === "boundaryEvent") {
      const policy = assessBpmnBoundaryAttachment(after, event, attached ?? "");
      if (!policy.allowed) refuseBpmnOperation(policy.reason);
    }
    if (definitions.includes("cancelEventDefinition") && (event.localName === "endEvent" ? scope?.localName !== "transaction"
      : event.localName !== "boundaryEvent" || attached?.localName !== "transaction"))
      refuseBpmnOperation("cancel events require the actual transaction context");
    if (event.localName !== "startEvent") continue;
    const eventBody = !!scope && bpmnBoolean(scope, "triggeredByEvent");
    if (!eventBody && scope?.localName !== "process" && definitions.length)
      refuseBpmnOperation("ordinary subprocess bodies require a none start event");
    if (eventBody && (!definitions.length || definitions.some(type => !eventBodyTriggers.has(type)))
      || !eventBody && (definitions.some(type => eventBodyTriggers.has(type) && !ordinaryTriggers.has(type))
        || readBpmnBooleanAttribute(event, "isInterrupting") === false))
      refuseBpmnOperation("the start trigger does not match its actual event subprocess context");
  }
}
