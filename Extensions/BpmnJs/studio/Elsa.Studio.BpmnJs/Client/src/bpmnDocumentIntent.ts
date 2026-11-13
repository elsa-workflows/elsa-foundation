// @ts-expect-error pinned moddle-xml 12.3.2 has no published declarations.
import { Writer } from "moddle-xml";
import { nativeImportSignature, nativeRefuse, nativeXml } from "./bpmnDocumentXml";

interface Property { name: string; type: string; isAttr?: boolean; isBody?: boolean; isMany?: boolean; isReference?: boolean; isVirtual?: boolean; ns: { prefix: string; localName: string }; xml?: { serialize?: string } }
interface Descriptor { name: string; ns: { prefix: string; localName: string }; properties: Property[]; propertiesByName: Record<string, Property>; allTypesByName: Record<string, unknown> }
interface Package { prefix: string; uri: string; xml?: { tagAlias?: string; typePrefix?: string }; types: { name: string }[]; enumerations?: { name: string; literalValues: { name: string }[] }[] }
interface Model { create(type: string, properties?: Record<string, unknown>): Modeled; getType(type: string): { $descriptor: Descriptor }; getPackages(): Package[] }
interface Modeled extends Record<string, unknown> { $type: string; $descriptor: Descriptor; $model: Model; $attrs: Record<string, string>; $parent?: Modeled }
interface NativeModeledIntent {
  readonly xml: string;
  readonly namespaces: readonly { readonly path: readonly number[]; readonly bindings: readonly (readonly [string, string])[] }[];
}
export interface NativePropertyIntent extends NativeModeledIntent {
  readonly name: string;
  readonly attribute?: string;
  readonly body?: boolean;
  readonly kinds: readonly string[];
  readonly references: readonly { readonly id: string; readonly kind: string; readonly payload?: NativeModeledIntent }[];
}
class ReferenceAdmissionError extends Error {}
const referenceRefuse = (message: string): never => { throw new ReferenceAdmissionError(`Cannot reconcile native BPMN edit: ${message}.`); };
const standard = new Set(["bpmn", "bpmndi", "dc", "di"]);
const primitive = new Set(["String", "Boolean", "Integer", "Real"]);
const kind = (element: Element) => `${element.namespaceURI}|${element.localName}`;
const modeled = (value: unknown): Modeled => {
  const result = value as Modeled;
  if (!result || typeof result !== "object" || !result.$model || !result.$descriptor || !standard.has(result.$descriptor.ns.prefix))
    nativeRefuse("property intent has no qualified standard modeled contract");
  return result;
};
function namespaces(value: Modeled): Record<string, string> {
  const result: Record<string, string> = {};
  for (let current: Modeled | undefined = value; current; current = current.$parent)
    for (const [name, binding] of Object.entries(current.$attrs ?? {})) if ((name === "xmlns" || name.startsWith("xmlns:")) && !(name in result)) result[name] = binding;
  return result;
}
function write(value: Modeled, properties?: Record<string, unknown>): NativeModeledIntent {
  const shell = value.$model.create(value.$type, properties ?? Object.fromEntries(value.$descriptor.properties
    .filter(property => !property.isVirtual && Object.hasOwn(value, property.name)).map(property => [property.name, value[property.name]])));
  Object.assign(shell.$attrs, namespaces(value));
  const xml = new Writer({ preamble: false }).toXML(shell) as string, root = nativeXml(xml).documentElement;
  const contexts: NativeModeledIntent["namespaces"][number][] = [];
  const visit = (source: Modeled, element: Element, path: number[]) => {
    contexts.push(Object.freeze({ path: Object.freeze(path), bindings: Object.freeze(Object.entries(namespaces(source)).map(entry => Object.freeze(entry))) }));
    for (const field of source.$descriptor.properties) if (!field.isReference && !field.isVirtual && !field.isAttr && !field.isBody && !primitive.has(field.type) && source[field.name] != null) {
      const kinds = propertyKinds(source, field), children = [...element.children].filter(child => kinds.includes(kind(child)));
      const values = Array.isArray(source[field.name]) ? source[field.name] as Modeled[] : [modeled(source[field.name])];
      if (values.length !== children.length) nativeRefuse("writer property alias has no qualified intent correspondence");
      values.forEach((child, index) => visit(child, children[index], [...path, [...element.children].indexOf(children[index])]));
    }
  };
  visit(shell, root, []);
  return Object.freeze({ xml, namespaces: Object.freeze(contexts) });
}
function propertyKinds(owner: Modeled, property: Property): readonly string[] {
  const packages = owner.$model.getPackages(), pack = packages.find(value => value.prefix === property.ns.prefix);
  if (!pack || !standard.has(pack.prefix)) nativeRefuse("property intent uses an unqualified schema namespace");
  if (property.isReference || primitive.has(property.type) || property.xml?.serialize)
    return Object.freeze([`${pack.uri}|${property.ns.localName}`]);
  const kinds = packages.filter(value => standard.has(value.prefix)).flatMap(pack => pack.types.flatMap(type => {
    const descriptor = owner.$model.getType(type.name.includes(":") ? type.name : `${pack.prefix}:${type.name}`).$descriptor;
    return descriptor.allTypesByName[property.type] ? [`${pack.uri}|${pack.xml?.tagAlias === "lowerCase" ? descriptor.ns.localName[0].toLowerCase() + descriptor.ns.localName.slice(1) : descriptor.ns.localName}`] : [];
  }));
  if (!kinds.length) nativeRefuse("property intent has no qualified child membership contract");
  return Object.freeze(kinds);
}

/** Synchronous pinned Writer snapshots own the requested values; no live moddle object escapes. */
export function captureNativePropertyIntents(ownerValue: unknown, properties: Readonly<Record<string, unknown>>): readonly NativePropertyIntent[] {
  const owner = modeled(ownerValue), result: NativePropertyIntent[] = [];
  for (const [name, value] of Object.entries(properties)) {
    if (name === "di") continue; // The separate, bounded DI flag contract owns this plain map.
    const property = owner.$descriptor.propertiesByName[name];
    if (!property || property.isVirtual) nativeRefuse(`property '${name}' has no qualified modeled intent contract`);
    if (property.isAttr && !property.isReference && (value == null || typeof value !== "object")) continue;
    const references = new Map<string, { id: string; kind: string; payload?: NativeModeledIntent }>(), referenceObjects = new Map<string, Modeled>(), seen = new Set<Modeled>();
    const inspect = (elementValue: unknown) => {
      const element = modeled(elementValue);
      if (seen.has(element)) return;
      seen.add(element);
      const pack = element.$model.getPackages().find(pack => pack.prefix === element.$descriptor.ns.prefix)!;
      const type = element.$attrs?.["xsi:type"]?.split(":"), bindings = namespaces(element);
      const declaredType = type && bindings[type.length === 2 ? `xmlns:${type[0]}` : "xmlns"] === pack.uri && type.at(-1) === `${pack.xml?.typePrefix ?? ""}${element.$descriptor.ns.localName}`;
      const opaque = Object.keys(element.$attrs ?? {}).filter(name => name !== "xmlns" && !name.startsWith("xmlns:") && !(name === "xsi:type" && declaredType));
      if (opaque.length) nativeRefuse(`opaque property intent (${opaque.join(", ")}) needs an explicit owning contract`);
      if (Object.keys(element).some(name => !name.startsWith("$") && !element.$descriptor.propertiesByName[name])) nativeRefuse("unknown modeled property intent needs an explicit owning contract");
      for (const field of element.$descriptor.properties) {
        if (field.isVirtual) continue;
        const fieldValue = element[field.name];
        if (fieldValue != null && Object.hasOwn(element, field.name)) check(element, field, fieldValue);
      }
    };
    const check = (element: Modeled, field: Property, fieldValue: unknown) => {
      if (fieldValue == null) return;
      if (!!field.isMany !== Array.isArray(fieldValue)) nativeRefuse("modeled property intent has incompatible list cardinality");
      for (const member of Array.isArray(fieldValue) ? fieldValue : [fieldValue]) {
        const enumeration = element.$model.getPackages().flatMap(pack => (pack.enumerations ?? []).filter(type => `${pack.prefix}:${type.name}` === field.type))[0];
        if (enumeration) {
          if (!enumeration.literalValues.some(value => value.name === member)) nativeRefuse("modeled property intent has an incompatible enumeration value");
        } else if (primitive.has(field.type)) {
          if (typeof member !== (field.type === "String" ? "string" : field.type === "Boolean" ? "boolean" : "number")) nativeRefuse("modeled property intent has an incompatible scalar type");
        } else {
          let child: Modeled;
          try { child = modeled(member); }
          catch (error) { if (field.isReference) referenceRefuse("reference intent has no qualified modeled identity/type"); throw error; }
          if (!child.$descriptor.allTypesByName[field.type]) (field.isReference ? referenceRefuse : nativeRefuse)("modeled property intent has an incompatible child type");
          if (field.isReference) {
            if (typeof child.id !== "string" || !child.id) referenceRefuse("reference intent has no modeled identity");
            const id = child.id as string, existing = referenceObjects.get(id);
            if (existing && existing !== child) referenceRefuse("reference intent has conflicting modeled identities");
            if (existing) continue;
            referenceObjects.set(id, child);
            const admission = { id, kind: kind(nativeIntentElement(write(child, { id }))), payload: undefined as NativeModeledIntent | undefined };
            references.set(id, admission);
            // Selection owns identity/type. Only admission of a new ID later requires this payload.
            try { inspect(child); admission.payload = write(child); }
            catch (error) { if (error instanceof ReferenceAdmissionError) throw error; }
          } else inspect(child);
        }
      }
    };
    check(owner, property, value);
    result.push(Object.freeze({ name, ...write(owner, { [name]: value }), kinds: propertyKinds(owner, property),
      ...(property.isAttr ? { attribute: property.ns.localName } : {}), ...(property.isBody ? { body: true } : {}),
      references: Object.freeze([...references.values()].map(reference => Object.freeze(reference))) }));
  }
  return Object.freeze(result);
}

export function nativeIntentElement(intent: NativeModeledIntent): Element {
  const source = nativeXml(intent.xml), document = source.implementation.createDocument(null, null);
  const reserved = new Set([...intent.xml.matchAll(/([\p{L}_][\p{L}\p{N}\p{M}_.-]*):/gu)].map(match => match[1]));
  const contexts = new Map(intent.namespaces.map(context => [context.path.join("/"), context.bindings]));
  const visit = (element: Element, path: number[], inherited: Map<string, string>): Element => {
    const bindings = new Map(inherited);
    for (const [name, value] of contexts.get(path.join("/")) ?? []) bindings.set(name === "xmlns" ? "" : name.slice(6), value);
    const prefixFor = (uri: string, attribute = false): string => {
      const known = [...bindings].find(([prefix, value]) => value === uri && (!attribute || prefix));
      if (known) return known[0];
      let index = 0; while (reserved.has(`intentNs${index}`) || bindings.has(`intentNs${index}`)) index++;
      const prefix = `intentNs${index}`; bindings.set(prefix, uri); return prefix;
    };
    const name = (uri: string | null, local: string, attribute = false) => { const prefix = uri ? prefixFor(uri, attribute) : ""; return prefix ? `${prefix}:${local}` : local; };
    const result = document.createElementNS(element.namespaceURI, name(element.namespaceURI, element.localName));
    for (const attribute of [...element.attributes]) if (attribute.namespaceURI !== "http://www.w3.org/2000/xmlns/") {
      let value = attribute.value;
      if (attribute.namespaceURI === "http://www.w3.org/2001/XMLSchema-instance" && attribute.localName === "type") {
        const parts = value.split(":"), uri = element.lookupNamespaceURI(parts.length === 2 ? parts[0] : null);
        if (!uri) nativeRefuse("captured typed property has no qualified namespace");
        value = `${prefixFor(uri, true)}:${parts.at(-1)}`;
      }
      result.setAttributeNS(attribute.namespaceURI, name(attribute.namespaceURI, attribute.localName, true), value);
    }
    for (const [prefix, uri] of bindings) if (prefix !== "xml") result.setAttributeNS("http://www.w3.org/2000/xmlns/", prefix ? `xmlns:${prefix}` : "xmlns", uri);
    let index = 0;
    for (const child of [...element.childNodes]) result.appendChild(child.nodeType === 1 ? visit(child as Element, [...path, index++], bindings) : document.importNode(child, true));
    return result;
  };
  document.appendChild(visit(source.documentElement, [], new Map([["", ""], ["xml", "http://www.w3.org/XML/1998/namespace"]])));
  return document.documentElement;
}
export function nativeModeledProperty(owner: Element, path: readonly string[]): Element {
  for (const name of path) {
    const candidates = [...owner.children].filter(child => name === child.localName || name.replace(/s$/, "") === child.localName
      || name === "eventDefinitions" && child.localName.endsWith("EventDefinition") || name === "loopCharacteristics" && ["standardLoopCharacteristics", "multiInstanceLoopCharacteristics"].includes(child.localName)
      || ({ label: "BPMNLabel", labelStyle: "BPMNLabelStyle", font: "Font", bounds: "Bounds" } as Record<string, string>)[name] === child.localName);
    if (candidates.length !== 1) nativeRefuse("idless modeled property path is missing or ambiguous");
    owner = candidates[0];
  }
  return owner;
}

/** Compare only the declared property, using the writer's real aliases, order and defaults. */
export function assertNativePropertyIntents(intents: readonly NativePropertyIntent[], owner: Element, oldIndex: ReadonlyMap<string, Element>, newIndex: ReadonlyMap<string, Element>, modelNamespace: string): void {
  const signature = (element: Element) => nativeImportSignature(element, modelNamespace);
  for (const intent of intents) {
    const expected = nativeIntentElement(intent);
    const same = intent.attribute ? expected.getAttribute(intent.attribute) === owner.getAttribute(intent.attribute)
      : intent.body ? expected.textContent === owner.textContent
        : [...expected.children].map(signature).join() === [...owner.children].filter(child => intent.kinds.includes(kind(child))).map(signature).join();
    if (!same) nativeRefuse(`modeled property '${intent.name}' differs from completed command intent`);
    for (const reference of intent.references) {
      const actual = newIndex.get(reference.id), previous = oldIndex.get(reference.id);
      if (!actual || kind(actual) !== reference.kind || previous && kind(previous) !== reference.kind) nativeRefuse("referenced identity/type differs from completed command intent");
      if (!previous && (!reference.payload || signature(nativeIntentElement(reference.payload)) !== signature(actual))) nativeRefuse("new referenced global needs its qualified captured payload and exact command intent");
    }
  }
}
