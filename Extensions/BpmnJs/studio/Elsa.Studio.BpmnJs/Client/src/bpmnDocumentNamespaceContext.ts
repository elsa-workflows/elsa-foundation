import type { BpmnDocumentAuthoring } from "@elsa-workflows/studio-workflows";
import type { NativeCommandAction } from "./bpmnDocumentCommands";
import { nativeRefuse, nativeXml } from "./bpmnDocumentXml";
import { nativeIntentElement, nativeModeledProperty } from "./bpmnDocumentIntent";

const xmlns = "http://www.w3.org/2000/xmlns/", xsi = "http://www.w3.org/2001/XMLSchema-instance";
const xml = "http://www.w3.org/XML/1998/namespace";
type Bindings = ReadonlyMap<string, string>;
export interface NativeNamespaceContext {
  readonly bindings: Bindings;
  readonly own: Bindings;
  readonly attributes: ReadonlyMap<string, string>;
}
export interface NativeNamespaceSnapshot { readonly ownerNodeId: string; readonly xml: string }
export interface NativeNamespaceOrigin { readonly elementId: string; readonly snapshot?: NativeNamespaceSnapshot }
const kind = (element: Element) => `{${element.namespaceURI ?? ""}}${element.localName}`;
const field = (attribute: Attr) => `{${attribute.namespaceURI ?? ""}}${attribute.localName}`;
const own = (element: Element) => new Map([...element.attributes].filter(attribute => attribute.namespaceURI === xmlns)
  .map(attribute => [attribute.name === "xmlns" ? "" : attribute.localName, attribute.value]));
function key(element: Element): string {
  if (element.hasAttribute("id")) return `id:${element.getAttribute("id")}`;
  const parent = element.parentElement;
  return parent ? `${key(parent)}/${kind(element)}[${[...parent.children].filter(child => kind(child) === kind(element)).indexOf(element)}]` : "root";
}
function relative(root: Element, element: Element): { kind: string; index: number }[] {
  const path: { kind: string; index: number }[] = [];
  for (let current = element; current !== root; current = current.parentElement!) path.unshift({ kind: kind(current), index: [...current.parentElement!.children].filter(child => kind(child) === kind(current)).indexOf(current) });
  return path;
}
function at(root: Element, path: { kind: string; index: number }[]): Element | undefined {
  let current: Element | undefined = root;
  for (const step of path) current = current && [...current.children].filter(child => kind(child) === step.kind)[step.index];
  return current;
}
export function captureNativeNamespaceContexts(document: XMLDocument): ReadonlyMap<string, NativeNamespaceContext> {
  const result = new Map<string, NativeNamespaceContext>();
  const visit = (element: Element, parent: Bindings) => {
    const declarations = own(element), bindings = new Map([...parent, ...declarations]);
    result.set(key(element), { bindings, own: declarations, attributes: new Map([...element.attributes].filter(attribute => attribute.namespaceURI !== xmlns).map(attribute => [field(attribute), attribute.value])) });
    for (const child of [...element.children]) visit(child, bindings);
  };
  visit(document.documentElement, new Map([["", ""], ["xml", xml]]));
  return result;
}
export function snapshotNativeNamespaces(authoring: BpmnDocumentAuthoring, projection: { owner: { nodeId: string }; xml: string }, elementId: string, di: ReadonlyMap<string, { readonly id: string }> = new Map()): NativeNamespaceSnapshot {
  const sourceId = di.get(elementId)?.id ?? elementId;
  const document = nativeXml(projection.xml), elements = [...document.getElementsByTagName("*")].filter(element => element.getAttribute("id") === sourceId);
  if (elements.length !== 1) nativeRefuse("copied namespace source is missing or ambiguous");
  return Object.freeze({ ownerNodeId: projection.owner.nodeId, xml: authoring.serializeBpmnXml(elements[0]) });
}
const typed = (attribute: Attr) => attribute.namespaceURI === xsi && attribute.localName === "type"
  || !attribute.namespaceURI && ["calledElement", "structureRef", "implementationRef"].includes(attribute.localName);
const qName = (value: string) => /^(?:([\p{L}_][\p{L}\p{N}\p{M}_.-]*):)?([\p{L}_][\p{L}\p{N}\p{M}_.-]*)$/u.exec(value.trim());
const prefixes = (value: string) => [...value.matchAll(/(?<![\p{L}\p{N}\p{M}_.:-])([\p{L}_][\p{L}\p{N}\p{M}_.-]*):[\p{L}_][\p{L}\p{N}\p{M}_.-]*/gu)].map(match => match[1]);
const values = (element: Element): string => JSON.stringify([kind(element), [...element.attributes].filter(attribute => attribute.namespaceURI !== xmlns)
  .map(attribute => [field(attribute), attribute.value]).sort(), [...element.children].map(values), [...element.childNodes].filter(node => node.nodeType === 3 || node.nodeType === 4).map(node => node.textContent).join("")]);
const state = (context?: NativeNamespaceContext) => JSON.stringify([...(context?.bindings ?? [])].sort());


/** Reconstruct only private comparison/fragment views. Raw native XML and canonical XML stay distinct. */
export function contextualizeNativeNamespacePair(authoring: BpmnDocumentAuthoring, contexts: ReadonlyMap<string, NativeNamespaceContext>, beforeXml: string, nextXml: string,
  origins: readonly NativeNamespaceOrigin[] = [], ownerNodeId?: string, importing = false, actions: readonly NativeCommandAction[] = []): { before: string; next: string } {
  const before = nativeXml(beforeXml), next = nativeXml(nextXml);
  const old = new Map([...before.getElementsByTagName("*")].map(element => [key(element), element]));
  const matched = new Map<Element, Element>([[next.documentElement, before.documentElement]]);
  const correspond = (parent: Element) => {
    const previous = matched.get(parent) ?? (parent.hasAttribute("id") ? old.get(key(parent)) : undefined);
    if (previous) matched.set(parent, previous);
    for (const child of [...parent.children]) if (child.hasAttribute("id") && old.has(key(child))) matched.set(child, old.get(key(child))!);
    for (const name of new Set([...parent.children].filter(child => !child.hasAttribute("id")).map(kind))) {
      const nextValues = [...parent.children].filter(child => !child.hasAttribute("id") && kind(child) === name);
      const candidates = [...previous?.children ?? []].filter(child => !child.hasAttribute("id") && kind(child) === name), used = new Set<Element>();
      for (const value of nextValues) {
        let exact = candidates.filter(candidate => !used.has(candidate) && values(candidate) === values(value));
        if (exact.length > 1) {
          const declared = new Set(own(value).values());
          const scores = exact.map(candidate => [...(contexts.get(key(candidate))?.own.values() ?? [])].filter(namespace => declared.has(namespace)).length);
          const highest = Math.max(...scores);
          if (highest) exact = exact.filter((_, index) => scores[index] === highest);
        }
        if (exact.length === 1) { matched.set(value, exact[0]); used.add(exact[0]); }
      }
      const remaining = nextValues.filter(value => !matched.has(value)), unmatched = candidates.filter(candidate => !used.has(candidate));
      if (remaining.length === unmatched.length) {
        let owner: Element | null = previous ?? null; while (owner && !owner.hasAttribute("id")) owner = owner.parentElement;
        const memberEdit = actions.some(action => (action.moddleOwnerId ?? action.elementId) === owner?.getAttribute("id")
          && (action.propertyNames ?? Object.keys(action.properties ?? {})).some(field => field === nextValues[0].localName || field.replace(/s$/, "") === nextValues[0].localName || field === "eventDefinitions" && nextValues[0].localName.endsWith("EventDefinition")));
        const unchanged = !memberEdit && nextValues.length === candidates.length && nextValues.every((value, index) => values(value) === values(candidates[index]));
        if (remaining.length > 1 && !importing && !unchanged && new Set(unmatched.map(candidate => state(contexts.get(key(candidate))))).size > 1)
          nativeRefuse("ambiguous idless namespace correspondence");
        remaining.forEach((value, index) => matched.set(value, unmatched[index]));
      }
    }
    for (const child of [...parent.children]) correspond(child);
  };
  correspond(next.documentElement);
  const intended = new Map<Element, NativeNamespaceContext>();
  const bindIntent = (source: Element, target: Element, contexts: ReadonlyMap<string, NativeNamespaceContext>) => {
    if (kind(source) !== kind(target)) return;
    intended.set(target, contexts.get(key(source))!);
    for (let index = 0; index < source.children.length; index++) if (target.children[index]) bindIntent(source.children[index], target.children[index], contexts);
  };
  for (const action of actions) if (action.propertyIntents?.length) {
    const owner = action.moddleDocumentRoot ? next.documentElement : [...next.getElementsByTagName("*")].find(element => element.getAttribute("id") === (action.moddleOwnerId ?? action.elementId));
    if (!owner) nativeRefuse("captured property namespace owner is unavailable");
    const target = nativeModeledProperty(owner, action.moddlePath ?? []);
    for (const intent of action.propertyIntents) {
      const source = nativeIntentElement(intent), contexts = captureNativeNamespaceContexts(source.ownerDocument);
      const children = [...target.children].filter(child => intent.kinds.includes(`${child.namespaceURI}|${child.localName}`));
      [...source.children].forEach((child, index) => { if (children[index]) bindIntent(child, children[index], contexts); });
      for (const reference of intent.references) {
        const element = [...next.getElementsByTagName("*")].find(element => element.getAttribute("id") === reference.id);
        if (element && reference.payload && !old.has(`id:${reference.id}`)) { const source = nativeIntentElement(reference.payload); bindIntent(source, element, captureNativeNamespaceContexts(source.ownerDocument)); }
      }
    }
  }
  const copies = new Map<string, { root: Element; contexts: ReadonlyMap<string, NativeNamespaceContext> }>();
  for (const origin of origins) {
    if (old.has(`id:${origin.elementId}`) || copies.has(origin.elementId) || !origin.snapshot || origin.snapshot.ownerNodeId !== ownerNodeId) nativeRefuse("copied source namespace correspondence is unavailable");
    const document = nativeXml(origin.snapshot.xml);
    copies.set(origin.elementId, { root: document.documentElement, contexts: captureNativeNamespaceContexts(document) });
  }
  const reserved = new Set<string>(["xml", "xmlns"]), opaquePrefixes = new Set<string>();
  for (const document of [before, next]) for (const element of [...document.getElementsByTagName("*")]) {
    for (const attribute of [...element.attributes]) for (const prefix of prefixes(attribute.value)) {
      reserved.add(prefix); if (!typed(attribute) && attribute.namespaceURI !== xmlns) opaquePrefixes.add(prefix);
    }
    for (const prefix of prefixes(element.textContent ?? "")) { reserved.add(prefix); opaquePrefixes.add(prefix); }
  }
  for (const context of contexts.values()) for (const prefix of context.bindings.keys()) reserved.add(prefix);
  const fresh = (namespace: string, bindings: Map<string, string>) => {
    let hash = 0; for (const character of namespace) hash = (hash * 31 + character.charCodeAt(0)) >>> 0;
    const stem = `nativeNs${hash.toString(36)}`; let candidate = stem, suffix = 0;
    while (reserved.has(candidate) || bindings.has(candidate)) candidate = `${stem}_${++suffix}`;
    return candidate;
  };
  const rebuild = (document: XMLDocument, isNext: boolean): XMLDocument => {
    const result = document.implementation.createDocument(null, null), effective = new Map<Element, Bindings>();
    const visit = (element: Element, inherited: Bindings, outputParent?: Element): Element => {
      const previous = isNext ? matched.get(element) : old.get(key(element));
      let context = isNext && intended.get(element) || (previous ? contexts.get(key(previous)) : undefined);
      for (let ancestor: Element | null = element; ancestor; ancestor = ancestor.parentElement) {
        const copied = copies.get(ancestor.getAttribute("id") ?? "");
        if (!copied) continue;
        const source = at(copied.root, relative(ancestor, element));
        if (!source || kind(source) !== kind(element)) nativeRefuse("copied namespace property correspondence changed");
        context = copied.contexts.get(key(source)); break;
      }
      const bindings = new Map(context?.bindings ?? inherited);
      const declarations = own(element);
      const typedBinding = (prefix: string) => [element, ...element.getElementsByTagName("*")].some(member => [...member.attributes].some(attribute => typed(attribute)
        && (qName(attribute.value)?.[1] ?? "") === prefix && attribute.value !== (matched.get(member)?.getAttributeNS(attribute.namespaceURI, attribute.localName) ?? contexts.get(key(member))?.attributes.get(field(attribute)))));
      if (isNext) for (const [prefix, namespace] of declarations) {
        const original = importing || intended.has(element) ? context?.own.get(prefix) : previous && own(previous).get(prefix);
        if (original !== undefined && original !== namespace) nativeRefuse("unaccounted native namespace rebinding outside the declared normalization ledger");
        if (namespace !== bindings.get(prefix) && original === undefined && !typedBinding(prefix)) {
          const nameUse = [element, ...element.getElementsByTagName("*")].some(member => (member.prefix ?? "") === prefix && member.namespaceURI === namespace
            || [...member.attributes].some(attribute => attribute.prefix === prefix && attribute.namespaceURI === namespace));
          const priorBinding = previous?.lookupNamespaceURI(prefix || null);
          // Pinned moddle aliases a shadowed declaration q to q_N without rewriting opaque body text.
          const writerAlias = [...bindings].some(([source, value]) => source && value === namespace && prefix.startsWith(`${source}_`) && /^[1-9][0-9]*$/.test(prefix.slice(source.length + 1)));
          if (!nameUse && !writerAlias && priorBinding !== namespace) nativeRefuse(`unaccounted native namespace addition at ${key(element)} (${prefix}=${namespace})`);
        }
      }
      const opaque = [...element.attributes].filter(attribute => attribute.namespaceURI !== xmlns && !typed(attribute)).map(attribute => attribute.value)
        .concat([...element.childNodes].filter(node => node.nodeType === 3 || node.nodeType === 4).map(node => node.textContent ?? "").join(""));
      if (opaque.flatMap(prefixes).some(prefix => !bindings.has(prefix) && inherited.get(prefix))) nativeRefuse("unbound source QName cannot be represented in the target namespace scope");
      const prefixFor = (namespace: string, attribute = false): string => {
        const known = [...bindings].find(([prefix, value]) => value === namespace && (!attribute || prefix));
        if (known) return known[0];
        const prefix = fresh(namespace, bindings); bindings.set(prefix, namespace); return prefix;
      };
      const namespace = element.namespaceURI ?? "", prefix = namespace ? prefixFor(namespace) : "";
      const output = result.createElementNS(namespace || null, prefix ? `${prefix}:${element.localName}` : element.localName);
      for (const attribute of [...element.attributes]) {
        if (attribute.namespaceURI === xmlns) continue;
        let value = attribute.value;
        if (typed(attribute)) {
          const parts = qName(value);
          if (parts) {
            const originalValue = context?.attributes.get(field(attribute)), previousValue = previous?.getAttributeNS(attribute.namespaceURI, attribute.localName);
            const declared = declarations.get(parts[1] ?? "");
            const changed = importing ? value !== originalValue : value !== previousValue;
            const meaning = changed && declared !== undefined ? declared
              : bindings.has(parts[1] ?? "") ? bindings.get(parts[1] ?? "") : element.lookupNamespaceURI(parts[1] ?? null);
            if (parts[1] && !meaning) nativeRefuse("typed QName has no provable namespace context");
            if (meaning && parts[1] && !bindings.has(parts[1]) && !opaquePrefixes.has(parts[1])) bindings.set(parts[1], meaning);
            value = meaning ? `${prefixFor(meaning, true)}:${parts[2]}` : parts[2];
          }
        }
        const attributePrefix = attribute.namespaceURI ? prefixFor(attribute.namespaceURI, true) : "";
        output.setAttributeNS(attribute.namespaceURI, attributePrefix ? `${attributePrefix}:${attribute.localName}` : attribute.localName, value);
      }
      for (const [prefix, namespace] of bindings) if (prefix !== "xml") output.setAttributeNS(xmlns, prefix ? `xmlns:${prefix}` : "xmlns", namespace);
      effective.set(output, new Map([...inherited, ...bindings]));
      if (outputParent) outputParent.appendChild(output); else result.appendChild(output);
      for (const child of [...element.childNodes]) {
        if (child.nodeType === 1) visit(child as Element, effective.get(output)!, output);
        else output.appendChild(result.importNode(child, true));
      }
      return output;
    };
    visit(document.documentElement, new Map([["", ""], ["xml", xml]]));
    return result;
  };
  return { before: authoring.serializeBpmnXml(rebuild(before, false)), next: authoring.serializeBpmnXml(rebuild(next, true)) };
}
