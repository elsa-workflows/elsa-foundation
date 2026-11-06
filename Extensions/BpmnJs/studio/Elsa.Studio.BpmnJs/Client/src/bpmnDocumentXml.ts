/** Private XML helpers for the optional native view; never serialize these trees into authored source. */
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
  if (raw === null || !raw.trim() || !Number.isFinite(Number(raw))) nativeRefuse(`'${element.localName}.${name}' must be finite`);
  return Number(raw);
}

export function nativeSignature(element: Element): string {
  // Namespace declarations affect opaque/external QName strings too. Unaccounted rebinding must refuse.
  const attributes = [...element.attributes]
    .map(attribute => [attribute.namespaceURI ?? "", attribute.localName, attribute.value]).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
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
  return JSON.stringify([element.namespaceURI, element.localName, attributes, children]);
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
  ["compensateEventDefinition", { waitForCompletion: true }], ["dataInput dataOutput dataObject itemDefinition", { isCollection: false }],
  ["dataStore", { isUnlimited: true }], ["participantMultiplicity", { minimum: 0, maximum: 1 }],
  ["choreographyActivity callChoreography subChoreography choreographyTask", { loopType: "None" }],
  ["subProcess transaction", { isForCompensation: false, startQuantity: 1, completionQuantity: 1, triggeredByEvent: false }],
  ["multiInstanceLoopCharacteristics", { isSequential: false, behavior: "All" }], ["standardLoopCharacteristics", { testBefore: false }],
  ["receiveTask", { isForCompensation: false, startQuantity: 1, completionQuantity: 1, instantiate: false }],
  ["adHocSubProcess", { isForCompensation: false, startQuantity: 1, completionQuantity: 1, triggeredByEvent: false, cancelRemainingInstances: true }],
  ["definitions", { expressionLanguage: "http://www.w3.org/1999/XPath", typeLanguage: "http://www.w3.org/2001/XMLSchema" }]
] as const) for (const name of names.split(" ")) semanticDefaults.set(name, defaults);
const booleanAttributes = new Set("isExecutable isClosed isForCompensation isCollection isImmediate isRequired isSequential isInterrupting cancelActivity parallelMultiple instantiate triggeredByEvent testBefore cancelRemainingInstances waitForCompletion isUnlimited mustUnderstand isReference isExpanded isHorizontal isMarkerVisible isBold isItalic isUnderline isStrikeThrough".split(" "));
const numberAttributes = new Set("x y width height size minimum maximum startQuantity completionQuantity".split(" "));

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
      if (booleanAttributes.has(attribute.localName) && ["true", "1", "false", "0"].includes(value)) value = value === "true" || value === "1";
      else if (numberAttributes.has(attribute.localName) && value.trim() && Number.isFinite(Number(value))) value = Number(value);
      if (element.namespaceURI === modelNamespace && semanticDefaults.get(element.localName)?.[attribute.localName] === value) return [];
    }
    const typedQName = attribute.namespaceURI === "http://www.w3.org/2001/XMLSchema-instance" && attribute.localName === "type";
    const externalQName = element.namespaceURI === modelNamespace && ["calledElement", "structureRef", "implementationRef"].includes(attribute.localName);
    if (typedQName || externalQName || element.namespaceURI === modelNamespace && attribute.localName.endsWith("Ref"))
      value = qName(String(value), typedQName || externalQName);
    if (typedQName && element.namespaceURI === "http://www.omg.org/spec/DD/20100524/DI" && element.localName === "waypoint"
      && value === "{http://www.omg.org/spec/DD/20100524/DC}Point") return [];
    return [[attribute.namespaceURI ?? "", attribute.localName, value] as const];
  }).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
  const children = [...element.childNodes].flatMap(node => {
    if (node.nodeType === 1) {
      const child = node as Element;
      if (child.namespaceURI === modelNamespace && child.localName === "extensionElements" && !child.children.length && !child.textContent?.trim()
        && [...child.attributes].every(attribute => attribute.namespaceURI === "http://www.w3.org/2000/xmlns/")) return [];
      if (child.namespaceURI === "http://www.omg.org/spec/BPMN/20100524/DI" && child.localName === "BPMNLabel" && !child.children.length && !child.textContent?.trim()
        && [...child.attributes].every(attribute => attribute.namespaceURI === "http://www.w3.org/2000/xmlns/")) return [];
      return [nativeImportSignature(child, modelNamespace)];
    }
    // Comments, PI and CDATA spelling are retained in R, but are not native modeled semantics.
    if (node.nodeType !== 3 && node.nodeType !== 4) return [];
    if (element.children.length && !node.textContent?.trim()) return [];
    const text = node.textContent ?? "";
    return [JSON.stringify(element.namespaceURI === modelNamespace && (element.localName.endsWith("Ref") || ["incoming", "outgoing"].includes(element.localName)) ? qName(text.trim()) : text)];
  });
  return JSON.stringify([element.namespaceURI, element.localName, attributes, children]);
}
