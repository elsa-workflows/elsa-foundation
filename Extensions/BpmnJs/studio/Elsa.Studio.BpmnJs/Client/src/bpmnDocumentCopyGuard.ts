import type { BpmnDocumentAuthoring, BpmnXmlAddress } from "@elsa-workflows/studio-workflows";
import type { BpmnNativeProjection } from "./bpmnDocumentProjection";
import type { NativeNamespaceOrigin } from "./bpmnDocumentNamespaceContext";
import { nativeRefuse, nativeXml } from "./bpmnDocumentXml";

/** Until the owning copy bridge supplies a qualified configuration transaction, keep it atomic. */
export function guardBpmnNativeCopies(authoring: BpmnDocumentAuthoring, projection: BpmnNativeProjection, origins: readonly NativeNamespaceOrigin[] = []): void {
  if (!origins.length) return;
  const parsed = authoring.readBpmnDocument(projection.owner), ns = authoring.bpmnNamespaces;
  const at = (address: BpmnXmlAddress): Element | undefined => {
    let element = address.documentRoot ? parsed.document.documentElement : parsed.byId.get(address.elementId!);
    for (const step of address.path ?? []) element = element && [...element.children]
      .filter(child => child.namespaceURI === step.namespaceURI && child.localName === step.localName)[step.index ?? 0];
    return element;
  };
  for (const origin of origins) {
    if (!origin.snapshot || origin.snapshot.ownerNodeId !== projection.owner.nodeId) nativeRefuse("copied source namespace correspondence is unavailable");
    const sourceId = nativeXml(origin.snapshot.xml).documentElement.getAttribute("id");
    const root = sourceId ? parsed.byId.get(sourceId) : undefined;
    if (!root) nativeRefuse("copied canonical source correspondence is unavailable");
    const roots = [root];
    if (root.namespaceURI === ns.model && root.localName === "participant" && root.hasAttribute("processRef")) {
      const process = authoring.resolveBpmnDocumentReference(parsed, root, root.getAttribute("processRef"));
      if (!process || process.namespaceURI !== ns.model || process.localName !== "process") nativeRefuse("copied participant process ownership is unavailable");
      roots.push(process);
    }
    const inside = (element: Element | undefined) => !!element && roots.some(root => root === element || root.contains(element));
    const retainedMarkup = (element: Element): boolean => [...element.childNodes].some(child => child.nodeType === 7 || child.nodeType === 8
      || child.nodeType === 1 && retainedMarkup(child as Element));
    const hasOmissions = projection.omitted.some(address => inside(at(address)));
    const hasConfiguration = roots.some(root => root === parsed.process)
      || parsed.payload.bindings.some(binding => inside(parsed.byId.get(binding.elementId)))
      || (parsed.payload.scopeHeaders ?? []).some(header => inside(parsed.byId.get(header.scopeElementId)));
    if (hasOmissions || hasConfiguration || roots.some(retainedMarkup))
      nativeRefuse("copy requires canonical XML/configuration ownership before native admission");
  }
}
