import type { BpmnDocumentAuthoring } from "@elsa-workflows/studio-workflows";
import type { BpmnNativeBaseline } from "./bpmnDocumentProjection";
import type { NativeCommandGroup } from "./bpmnDocumentCommands";
import { nativeIndex, nativeRefuse, nativeXml } from "./bpmnDocumentXml";

/** Private native DI identities may change on replacement; canonical DI identities and payload do not. */
export function nativeDiAliases(authoring: BpmnDocumentAuthoring, baseline: BpmnNativeBaseline, group: NativeCommandGroup, nextXml: string): ReadonlyMap<string, string> {
  const aliases = new Map<string, string>(), next = nativeXml(nextXml), nextIndex = nativeIndex(next), ns = authoring.bpmnNamespaces;
  for (const [nativeId, correspondence] of baseline.di) if (nextIndex.has(nativeId)) aliases.set(nativeId, correspondence.id);
  const replacements = new Set(group.actions.filter(action => action.command === "shape.replace").flatMap(action => [action.elementId, action.oldElementId, action.newElementId]).filter(Boolean));
  for (const [nativeId, correspondence] of baseline.di) {
    if (nextIndex.has(nativeId) || !replacements.has(correspondence.elementId)) continue;
    const candidates = [...nextIndex.values()].filter(element => element.namespaceURI === ns.diagram && element.localName === "BPMNShape"
      && element.getAttribute("bpmnElement") === correspondence.elementId
      && element.parentElement?.getAttribute("bpmnElement") === correspondence.nativePlaneScopeId && !baseline.di.has(element.getAttribute("id")!));
    if (candidates.length !== 1) nativeRefuse("replacement has ambiguous native/source DI identity correspondence");
    aliases.set(candidates[0].getAttribute("id")!, correspondence.id);
  }
  if (new Set(aliases.values()).size !== aliases.size) nativeRefuse("native DI aliases cannot share a canonical identity");
  return aliases;
}

/** Transient comparison tree only; callers retain/save the raw authored source independently. */
export function normalizeNativeDiIds(authoring: BpmnDocumentAuthoring, xml: string, aliases: ReadonlyMap<string, string>): string {
  const document = nativeXml(xml);
  for (const element of [...document.getElementsByTagName("*")]) {
    if (element.namespaceURI !== authoring.bpmnNamespaces.diagram) continue;
    const id = element.getAttribute("id"); if (id && aliases.has(id)) element.setAttribute("id", aliases.get(id)!);
    for (const name of ["sourceElement", "targetElement", "labelStyle"] as const) {
      const value = element.getAttribute(name); if (value && aliases.has(value)) element.setAttribute(name, aliases.get(value)!);
    }
  }
  nativeIndex(document); // Reject an alias colliding with another actual modeled ID.
  return authoring.serializeBpmnXml(document);
}
