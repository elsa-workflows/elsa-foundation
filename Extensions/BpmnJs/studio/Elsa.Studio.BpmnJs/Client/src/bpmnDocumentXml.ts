/** Private XML helpers; whole native serialization never replaces authored source. */
export function nativeRefuse(message: string): never { throw new Error(`Cannot reconcile native BPMN edit: ${message}.`); }

export function nativeXml(xml: string): XMLDocument {
  const markup = xml.replace(/<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<\?[\s\S]*?\?>/g, "");
  if (/<!\s*(?:DOCTYPE|ENTITY)\b/i.test(markup)) nativeRefuse("DTD/entity declarations are unavailable");
  const document = new DOMParser().parseFromString(xml, "application/xml");
  if (document.documentElement.localName === "parsererror" || document.getElementsByTagNameNS("http://www.mozilla.org/newlayout/xml/parsererror.xml", "parsererror").length)
    nativeRefuse("modeled XML is malformed");
  return document;
}

export function nativeChildren(element: Element, namespace: string, name: string): Element[] {
  return [...element.children].filter(child => child.namespaceURI === namespace && child.localName === name);
}

export function nativeIndex(document: XMLDocument): Map<string, Element> {
  const result = new Map<string, Element>();
  for (const element of [...document.getElementsByTagName("*")]) {
    const id = element.getAttribute("id");
    if (!id) continue;
    if (result.has(id)) nativeRefuse(`modeled identity '${id}' is ambiguous`);
    result.set(id, element);
  }
  return result;
}

export function nativeNumber(element: Element, name: string): number {
  const raw = element.getAttribute(name);
  if (raw === null && nativeAttributeDefault(element, name) === 0) return 0;
  if (raw === null || !raw.trim() || !Number.isFinite(Number(raw))) nativeRefuse(`'${element.localName}.${name}' must be finite`);
  return Number(raw);
}

/** Opaque strings are never rewritten; compare the bindings of their QName-looking tokens too. */
function opaqueQNameContext(element: Element, value: string, defaultQName = false) {
  const prefixes = new Set([...value.matchAll(/(?<![\p{L}\p{N}\p{M}_.:-])([\p{L}_][\p{L}\p{N}\p{M}_.-]*):[\p{L}_][\p{L}\p{N}\p{M}_.-]*/gu)].map(match => match[1]));
  if (defaultQName && /^[\p{L}_][\p{L}\p{N}\p{M}_.-]*$/u.test(value.trim())) prefixes.add("");
  return [...prefixes].sort().map(prefix => [prefix, element.lookupNamespaceURI(prefix || null)]);
}
function opaqueTextContext(element: Element) {
  // Joining text/CDATA across comments detects split tokens without inferring references from them.
  return opaqueQNameContext(element, [...element.childNodes].filter(node => node.nodeType === 3 || node.nodeType === 4).map(node => node.textContent ?? "").join(""), true);
}

export function nativeSignature(element: Element): string {
  // Namespace declarations affect opaque/external QName strings too. Unaccounted rebinding must refuse.
  const attributes = [...element.attributes]
    .map(attribute => [attribute.namespaceURI ?? "", attribute.localName, attribute.value, opaqueQNameContext(element, attribute.value)]).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
  const children = [...element.childNodes].flatMap(node => {
    if (node.nodeType === 1) {
      const child = node as Element;
      // An empty label carries no modeled notation. Canonical label metadata remains in raw source.
      if (child.namespaceURI === "http://www.omg.org/spec/BPMN/20100524/DI" && child.localName === "BPMNLabel"
        && !child.attributes.length && !child.childNodes.length) return [];
      return [nativeSignature(child)];
    }
    if (node.nodeType === 7 || node.nodeType === 8) return [JSON.stringify([node.nodeType, node.nodeName, node.textContent])];
    if (node.nodeType !== 3 && node.nodeType !== 4) return [];
    if (element.children.length && !node.textContent?.trim()) return [];
    return [JSON.stringify(node.textContent)];
  });
  return JSON.stringify([element.namespaceURI, element.localName, attributes, children, opaqueTextContext(element)]);
}

/** Only declared zero-coordinate omissions vary in geometry commands; other native content stays strict. */
export function nativeGeometrySignature(element: Element): string {
  const view = element.cloneNode(true) as Element;
  for (const member of [view, ...view.getElementsByTagName("*")]) for (const name of ["x", "y"]) {
    const value = member.getAttribute(name);
    if (nativeAttributeDefault(member, name) === 0 && (value === null || nativeNumericValue(member, name, value) === 0)) member.setAttribute(name, "0");
  }
  return nativeSignature(view);
}

// Attribute defaults (including inheritance) from pinned bpmn-moddle10.3.1 bpmn.json.
// This is an explicit import-normalization policy, not permission to omit other semantic fields.
const semanticDefaults = new Map<string, Readonly<Record<string, string | number | boolean>>>();
for (const [names, defaults] of [
  ["manualTask userTask activity serviceTask callActivity task sendTask scriptTask businessRuleTask", { isForCompensation: false, startQuantity: 1, completionQuantity: 1 }],
  ["gateway complexGateway exclusiveGateway inclusiveGateway parallelGateway", { gatewayDirection: "Unspecified" }],
  ["eventBasedGateway", { gatewayDirection: "Unspecified", instantiate: false, eventGatewayType: "Exclusive" }],
  ["extension", { mustUnderstand: false }], ["extensionAttributeDefinition", { isReference: false }],
  ["documentation textAnnotation", { textFormat: "text/plain" }], ["intermediateCatchEvent catchEvent", { parallelMultiple: false }],
  ["startEvent", { parallelMultiple: false, isInterrupting: true }], ["boundaryEvent", { parallelMultiple: false, cancelActivity: true }],
  ["compensateEventDefinition", { waitForCompletion: true }], ["dataInput inputDataItem dataOutput outputDataItem dataObject itemDefinition", { isCollection: false }],
  ["dataStore", { isUnlimited: true }], ["participantMultiplicity", { minimum: 0, maximum: 1 }],
  ["choreographyActivity callChoreography subChoreography choreographyTask", { loopType: "None" }],
  ["subProcess transaction", { isForCompensation: false, startQuantity: 1, completionQuantity: 1, triggeredByEvent: false }],
  ["multiInstanceLoopCharacteristics", { isSequential: false, behavior: "All" }], ["standardLoopCharacteristics", { testBefore: false }],
  ["receiveTask", { isForCompensation: false, startQuantity: 1, completionQuantity: 1, instantiate: false }],
  ["adHocSubProcess", { isForCompensation: false, startQuantity: 1, completionQuantity: 1, triggeredByEvent: false, cancelRemainingInstances: true }],
  ["definitions", { expressionLanguage: "http://www.w3.org/1999/XPath", typeLanguage: "http://www.w3.org/2001/XMLSchema" }]
] as const) for (const name of names.split(" ")) semanticDefaults.set(name, defaults);
const modelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
const diagramNamespace = "http://www.omg.org/spec/BPMN/20100524/DI";
const boundsNamespace = "http://www.omg.org/spec/DD/20100524/DC";
function nativeModelKind(element: Element): string {
  const type = element.getAttributeNS("http://www.w3.org/2001/XMLSchema-instance", "type")?.trim();
  const match = /^(?:([^:]+):)?t([A-Z][A-Za-z0-9]*)$/.exec(type ?? "");
  return match && element.lookupNamespaceURI(match[1] ?? null) === modelNamespace
    ? match[2][0].toLowerCase() + match[2].slice(1) : element.localName;
}
/** Only declared Boolean fields are converted; identically named opaque fields stay strings. */
export function nativeBooleanAttribute(element: Element, name: string): boolean {
  const kind = nativeModelKind(element);
  if (element.namespaceURI === modelNamespace) return typeof semanticDefaults.get(kind)?.[name] === "boolean"
    || ["process", "collaboration", "choreography", "globalConversation", "globalChoreographyTask"].includes(kind) && name === "isClosed"
    || kind === "process" && name === "isExecutable"
    || kind === "sequenceFlow" && name === "isImmediate"
    || kind === "resourceParameter" && name === "isRequired";
  return element.namespaceURI === diagramNamespace && element.localName === "BPMNShape" && ["isExpanded", "isHorizontal", "isMarkerVisible", "isMessageVisible"].includes(name)
    || element.namespaceURI === boundsNamespace && element.localName === "Font" && ["isBold", "isItalic", "isUnderline", "isStrikeThrough"].includes(name);
}
export function nativeBoolean(value: string): boolean | undefined {
  const collapsed = value.replace(/^[ \t\r\n]+|[ \t\r\n]+$/g, "");
  return collapsed === "true" || collapsed === "1" ? true : collapsed === "false" || collapsed === "0" ? false : undefined;
}
/** Match captured primitive intent against the pinned writer's declared default omission only. */
export function nativePrimitiveMatches(element: Element, name: string, value: unknown): boolean {
  const actual = element.getAttribute(name);
  const fallback = nativeAttributeDefault(element, name);
  if (actual === null) return value == null || fallback !== undefined && fallback === value;
  if (typeof value === "boolean" && nativeBooleanAttribute(element, name)) return nativeBoolean(actual) === value;
  if (typeof value === "number" && typeof fallback === "number") return actual.trim() !== "" && Number(actual) === value;
  return actual === (value == null ? null : String(value));
}
function nativeAttributeDefault(element: Element, name: string): string | number | boolean | undefined {
  if (element.namespaceURI === diagramNamespace && element.localName === "BPMNEdge" && name === "messageVisibleKind") return "initiating";
  if (element.namespaceURI === modelNamespace) return semanticDefaults.get(nativeModelKind(element))?.[name];
  return (element.namespaceURI === boundsNamespace && ["Point", "Bounds"].includes(element.localName)
    || element.namespaceURI === "http://www.omg.org/spec/DD/20100524/DI" && element.localName === "waypoint") && ["x", "y"].includes(name) ? 0 : undefined;
}
function nativeNumericAttribute(element: Element, name: string): "integer" | "real" | undefined {
  if (element.namespaceURI === modelNamespace) return typeof semanticDefaults.get(nativeModelKind(element))?.[name] === "number"
    || nativeModelKind(element) === "dataStore" && name === "capacity"
    || nativeModelKind(element) === "standardLoopCharacteristics" && name === "loopMaximum" ? "integer" : undefined;
  return element.namespaceURI === boundsNamespace && (element.localName === "Font" && name === "size"
    || ["Point", "Bounds"].includes(element.localName) && ["x", "y"].includes(name)
    || element.localName === "Bounds" && ["width", "height"].includes(name))
    || element.namespaceURI === "http://www.omg.org/spec/DD/20100524/DI" && element.localName === "waypoint" && ["x", "y"].includes(name)
    || element.namespaceURI === diagramNamespace && element.localName === "BPMNDiagram" && name === "resolution" ? "real" : undefined;
}
function nativeNumericValue(element: Element, name: string, value: string): number | undefined {
  const type = nativeNumericAttribute(element, name), lexical = value.replace(/^[ \t\r\n]+|[ \t\r\n]+$/g, "");
  const valid = type === "integer" ? /^[+-]?[0-9]+$/.test(lexical) : type === "real" && /^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$/.test(lexical);
  return valid && Number.isFinite(Number(lexical)) ? Number(lexical) : undefined;
}

/** Complete projected content comparison with only declared serializer spelling/default normalizations. */
export function nativeImportSignature(element: Element, modelNamespace: string): string {
  const qName = (value: string, useDefaultNamespace = false) => {
    value = value.trim();
    const match = /^([\p{L}_][\p{L}\p{N}\p{M}_.-]*):([\p{L}_][\p{L}\p{N}\p{M}_.-]*)$/u.exec(value);
    if (!match) return useDefaultNamespace && /^[\p{L}_][\p{L}\p{N}\p{M}_.-]*$/u.test(value)
      ? `{${element.lookupNamespaceURI(null) ?? ""}}${value}` : value;
    const namespace = element.lookupNamespaceURI(match[1]);
    if (!namespace) nativeRefuse(`import QName '${value}' has no namespace context`);
    return `{${namespace}}${match[2]}`;
  };
  const attributes = [...element.attributes].filter(attribute => attribute.namespaceURI !== "http://www.w3.org/2000/xmlns/").flatMap(attribute => {
    let value: string | boolean | number = attribute.value;
    if (!attribute.namespaceURI) {
      if (nativeBooleanAttribute(element, attribute.localName) && nativeBoolean(value) !== undefined) value = nativeBoolean(value)!;
      else if (nativeNumericValue(element, attribute.localName, value) !== undefined) value = nativeNumericValue(element, attribute.localName, value)!;
      if (nativeAttributeDefault(element, attribute.localName) === value) return [];
    }
    const typedQName = attribute.namespaceURI === "http://www.w3.org/2001/XMLSchema-instance" && attribute.localName === "type";
    const externalQName = element.namespaceURI === modelNamespace && ["calledElement", "structureRef", "implementationRef"].includes(attribute.localName);
    if (typedQName || externalQName || element.namespaceURI === modelNamespace && attribute.localName.endsWith("Ref"))
      value = qName(String(value), typedQName || externalQName);
    if (typedQName && element.namespaceURI === "http://www.omg.org/spec/DD/20100524/DI" && element.localName === "waypoint"
      && value === "{http://www.omg.org/spec/DD/20100524/DC}Point") return [];
    const opaqueContext = typedQName || externalQName || element.namespaceURI === modelNamespace && attribute.localName.endsWith("Ref")
      ? [] : opaqueQNameContext(element, attribute.value);
    return [[attribute.namespaceURI ?? "", attribute.localName, value, opaqueContext] as const];
  }).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
  const typedText = element.namespaceURI === modelNamespace && (element.localName.endsWith("Ref") || ["incoming", "outgoing"].includes(element.localName));
  const children: string[] = [];
  let text = "";
  const flush = () => {
    if (text && !(element.children.length && !text.trim())) children.push(JSON.stringify(typedText ? qName(text.trim()) : text));
    text = "";
  };
  for (const node of [...element.childNodes]) {
    if (node.nodeType === 3 || node.nodeType === 4) { text += node.textContent ?? ""; continue; }
    if (node.nodeType !== 1) continue; // Comments/PI split no modeled body; raw source retains them.
    flush();
    const child = node as Element;
    if (child.namespaceURI === modelNamespace && child.localName === "extensionElements" && !child.children.length && !child.textContent?.trim()
      && [...child.attributes].every(attribute => attribute.namespaceURI === "http://www.w3.org/2000/xmlns/")) continue;
    if (child.namespaceURI === diagramNamespace && child.localName === "BPMNLabel" && !child.children.length && !child.textContent?.trim()
      && [...child.attributes].every(attribute => attribute.namespaceURI === "http://www.w3.org/2000/xmlns/")) continue;
    children.push(nativeImportSignature(child, modelNamespace));
  }
  flush();
  return JSON.stringify([element.namespaceURI, element.localName, attributes, children, typedText ? [] : opaqueTextContext(element)]);
}
