import { bpmnNamespaces as ns } from "./bpmnDocument";

/** Namespace resemblance inside an extension or text payload is not standard construct ownership. */
export function isBpmnOwnedSyntax(element: Element): boolean {
  for (let current: Element | null = element; current; current = current.parentElement) {
    if (!(Object.values(ns) as readonly string[]).includes(current.namespaceURI ?? "")
      || current.namespaceURI === ns.model && ["extensionElements", "documentation", "script"].includes(current.localName)) return false;
  }
  return true;
}
