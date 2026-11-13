import { bpmnNamespaces, resolveBpmnDocumentReference, type ParsedBpmnDocument } from "./bpmnDocument";
import { bpmnBoolean, bpmnFlowScope } from "./bpmnDocumentPolicy";
import { bpmnEventDefinitionTypes, isActivityBearingElementType, isEventElementType } from "./bpmnNotationTypes";
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
  const signature = (parsed: ParsedBpmnDocument, event: Element) => {
    const scope = bpmnFlowScope(event), attached = host(parsed, event), hostScope = attached && bpmnFlowScope(attached);
    return JSON.stringify([scope?.getAttribute("id"), scope?.localName, scope && bpmnBoolean(scope, "triggeredByEvent"),
      attached?.getAttribute("id"), attached?.localName, hostScope?.getAttribute("id"),
      attached && bpmnBoolean(attached, "triggeredByEvent"), attached && bpmnBoolean(attached, "isForCompensation")]);
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
    if (!isBpmnOwnedSyntax(event) || !isEventElementType(event.localName) || !inside(event) && !inside(host(after, event))) continue;
    const old = previous(event);
    if (old && signature(before, old) === signature(after, event)) continue;
    const scope = bpmnFlowScope(event), attached = host(after, event), definitions = bpmnEventDefinitionTypes(event);
    if (event.localName === "boundaryEvent" && (!attached || !isActivityBearingElementType(attached.localName)
      || bpmnFlowScope(attached) !== scope || bpmnBoolean(attached, "triggeredByEvent") || bpmnBoolean(attached, "isForCompensation")))
      refuseBpmnOperation("the changed boundary event needs an eligible host in the same standard flow scope");
    if (definitions.includes("cancelEventDefinition") && (event.localName === "endEvent" ? scope?.localName !== "transaction"
      : event.localName !== "boundaryEvent" || attached?.localName !== "transaction"))
      refuseBpmnOperation("cancel events require the actual transaction context");
    if (event.localName !== "startEvent") continue;
    const eventBody = !!scope && bpmnBoolean(scope, "triggeredByEvent");
    if (eventBody && (!definitions.length || definitions.some(type => !eventBodyTriggers.has(type)))
      || !eventBody && (definitions.some(type => eventBodyTriggers.has(type) && !ordinaryTriggers.has(type))
        || readBpmnBooleanAttribute(event, "isInterrupting") === false))
      refuseBpmnOperation("the start trigger does not match its actual event subprocess context");
  }
}
