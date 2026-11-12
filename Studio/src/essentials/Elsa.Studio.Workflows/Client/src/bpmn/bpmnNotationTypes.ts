import { bpmnElementTypes as legacyElementTypes, isTaskElementType } from "./bpmnTypes";

/** Complete-document notation facts; legacy source1 predicates keep their existing behavior. */
export const bpmnElementTypes = {
  ...legacyElementTypes,
  intermediateCatchEvent: "intermediateCatchEvent", intermediateThrowEvent: "intermediateThrowEvent", boundaryEvent: "boundaryEvent",
  adHocSubProcess: "adHocSubProcess", transaction: "transaction", callActivity: "callActivity",
  complexGateway: "complexGateway", eventBasedGateway: "eventBasedGateway",
  dataObject: "dataObject", dataObjectReference: "dataObjectReference", dataInput: "dataInput", dataOutput: "dataOutput",
  dataStoreReference: "dataStoreReference", textAnnotation: "textAnnotation", group: "group", participant: "participant", lane: "lane"
} as const;

/** Normalize modeler names while retaining foreign-namespace identity as unknown notation. */
export function bpmnNotationType(type: string) {
  if (type.startsWith("unknown:")) return type;
  if (type.includes(":") && !type.startsWith("bpmn:")) return `unknown:${type}`;
  const local = type.includes(":") ? type.slice(type.lastIndexOf(":") + 1) : type;
  return local.charAt(0).toLowerCase() + local.slice(1);
}

export function isSubProcessElementType(type: string) {
  return type === "subProcess" || type === "adHocSubProcess" || type === "transaction";
}

export function isActivityBearingElementType(type: string) {
  return isTaskElementType(type) || isSubProcessElementType(type) || type === "callActivity";
}

export function isEventElementType(type: string) {
  return ["startEvent", "endEvent", "intermediateCatchEvent", "intermediateThrowEvent", "boundaryEvent"].includes(type);
}

export function isGatewayElementType(type: string) {
  return ["exclusiveGateway", "parallelGateway", "inclusiveGateway", "complexGateway", "eventBasedGateway"].includes(type);
}

const modelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";

/** Resolve a standard model QName for standalone notation without allocating or normalizing IDs. */
export function resolveBpmnNotationReference(context: Element, reference: string | null): Element | undefined {
  if (!reference?.trim()) return undefined;
  const parts = reference.trim().split(":");
  if (parts.length > 2 || parts.some(part => !part)) return undefined;
  if (parts.length === 2) {
    const namespace = context.lookupNamespaceURI(parts[0]);
    if (!namespace || namespace !== context.ownerDocument.documentElement.getAttribute("targetNamespace")) return undefined;
  }
  const matches = [...context.ownerDocument.getElementsByTagNameNS(modelNamespace, "*")].filter(element => element.getAttribute("id") === parts.at(-1));
  return matches.length === 1 ? matches[0] : undefined;
}

/** Inline and referenced definitions share the same meaning for notation and connection policy. */
export function bpmnEventDefinitionTypes(element: Element): string[] {
  return [...element.children].flatMap(child => {
    if (child.namespaceURI !== modelNamespace) return [];
    if (child.localName.endsWith("EventDefinition")) return [child.localName];
    if (child.localName !== "eventDefinitionRef") return [];
    const definition = resolveBpmnNotationReference(child, child.textContent);
    return [definition?.localName.endsWith("EventDefinition") ? definition.localName : "unresolvedEventDefinition"];
  });
}
