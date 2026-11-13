import type { BpmnDocumentAuthoring, BpmnXmlAddress } from "@elsa-workflows/studio-workflows";
import type { BpmnNativeProjection } from "./bpmnDocumentProjection";
import type { NativeNamespaceOrigin } from "./bpmnDocumentNamespaceContext";
import { nativeIndex, nativeRefuse, nativeXml } from "./bpmnDocumentXml";

/** Until the owning copy bridge supplies a qualified configuration transaction, keep it atomic. */
export function guardBpmnNativeCopies(authoring: BpmnDocumentAuthoring, projection: BpmnNativeProjection, origins: readonly NativeNamespaceOrigin[] = [], nextXml?: string): void {
  if (!origins.length) return;
  const parsed = authoring.readBpmnDocument(projection.owner), ns = authoring.bpmnNamespaces, standard = new Set<string>(Object.values(ns));
  const at = (address: BpmnXmlAddress): Element | undefined => {
    let element = address.documentRoot ? parsed.document.documentElement : parsed.byId.get(address.elementId!);
    for (const step of address.path ?? []) element = element && [...element.children]
      .filter(child => child.namespaceURI === step.namespaceURI && child.localName === step.localName)[step.index ?? 0];
    return element;
  };
  let nextIndex: Map<string, Element> | undefined;
  for (const origin of origins) {
    if (!origin.snapshot || origin.snapshot.ownerNodeId !== projection.owner.nodeId) nativeRefuse("copied source namespace correspondence is unavailable");
    const snapshot = nativeXml(origin.snapshot.xml).documentElement, sourceId = snapshot.getAttribute("id");
    const root = sourceId ? parsed.byId.get(sourceId) : undefined;
    if (!root) nativeRefuse("copied canonical source correspondence is unavailable");
    const roots = [root];
    if (root.namespaceURI === ns.model && root.localName === "participant" && root.hasAttribute("processRef")) {
      const process = authoring.resolveBpmnDocumentReference(parsed, root, root.getAttribute("processRef"));
      if (!process || process.namespaceURI !== ns.model || process.localName !== "process") nativeRefuse("copied participant process ownership is unavailable");
      roots.push(process);
    }
    const inside = (element: Element | undefined) => !!element && roots.some(root => root === element || root.contains(element));
    const xmlns = "http://www.w3.org/2000/xmlns/", xml = "http://www.w3.org/XML/1998/namespace", xsi = "http://www.w3.org/2001/XMLSchema-instance";
    const retainedAttributes = (element: Element) => [...element.attributes].some(attribute => attribute.namespaceURI && attribute.namespaceURI !== xmlns
      && !standard.has(attribute.namespaceURI)
      && !(attribute.namespaceURI === xsi && attribute.localName === "type"));
    const retainedMarkup = (element: Element): boolean => retainedAttributes(element) || [...element.childNodes].some(child => child.nodeType === 7 || child.nodeType === 8
      || child.nodeType === 1 && retainedMarkup(child as Element));
    const hasOmissions = projection.omitted.some(address => inside(at(address)));
    const hasConfiguration = roots.some(root => root === parsed.process)
      || parsed.payload.bindings.some(binding => inside(parsed.byId.get(binding.elementId)))
      || (parsed.payload.scopeHeaders ?? []).some(header => inside(parsed.byId.get(header.scopeElementId)));
    const inheritedXmlContext = roots.some(root => {
      for (let ancestor = root.parentElement; ancestor; ancestor = ancestor.parentElement)
        if ([...ancestor.attributes].some(attribute => attribute.namespaceURI === xml)) return true;
      return false;
    });
    if (hasOmissions || hasConfiguration || inheritedXmlContext || roots.some(retainedMarkup))
      nativeRefuse("copy requires canonical XML/configuration ownership before native admission");
    if (nextXml) {
      nextIndex ??= nativeIndex(nativeXml(nextXml));
      const lostContent = (source: Element, copied: Element | undefined): boolean => !copied
        || (!source.children.length && (source.textContent ?? "").trim() !== "" && source.textContent !== copied.textContent)
        || [...source.attributes].some(attribute => attribute.namespaceURI !== xmlns
        && !(attribute.localName === "id" && !attribute.namespaceURI) && !copied?.hasAttributeNS(attribute.namespaceURI, attribute.localName))
        || [...source.children].some(child => lostContent(child, copied && [...copied.children].filter(value => value.namespaceURI === child.namespaceURI
          && value.localName === child.localName)[[...source.children].filter(value => value.namespaceURI === child.namespaceURI && value.localName === child.localName).indexOf(child)]));
      if (lostContent(snapshot, nextIndex.get(origin.elementId))) nativeRefuse("copy requires canonical XML/configuration ownership for retained source content");
    }
  }
}
