const xmlns = "http://www.w3.org/2000/xmlns/";
const xml = "http://www.w3.org/XML/1998/namespace";
type Bindings = Map<string, string>;
const fail = (message: string): never => { throw new Error(`Cannot serialize BPMN XML: ${message}.`); };

function characters(value: string) {
  for (const character of value) {
    const point = character.codePointAt(0)!;
    if (!(point === 9 || point === 10 || point === 13 || point >= 32 && point <= 0xd7ff
      || point >= 0xe000 && point <= 0xfffd || point >= 0x10000 && point <= 0x10ffff)) fail("content contains an invalid XML 1.0 character");
  }
}
function escape(value: string, attribute = false) {
  characters(value);
  return [...value].map(character => character === "&" ? "&amp;" : character === "<" ? "&lt;" : character === ">" ? "&gt;"
    : attribute && character === '"' ? "&quot;" : character === "\r" ? "&#13;" : attribute && character === "\n" ? "&#10;"
      : attribute && character === "\t" ? "&#9;" : character).join("");
}
function declaration(prefix: string, namespace: string) {
  characters(namespace);
  if (prefix === "xmlns" || namespace === xmlns || prefix === "xml" && namespace !== xml
    || namespace === xml && prefix !== "xml" || prefix && !namespace) fail("namespace declaration uses a reserved or empty binding");
}

/** Preserve explicit namespace bindings, including unused bindings that own opaque QName values.
 * Native XMLSerializer can drop a root binding restored below a shadowing declaration.
 * Prefix fixups affect generated names only; authored values and declarations are never rewritten.
 */
export function serializeBpmnXml(node: Document | Element): string {
  const values: string[] = [], reserved = new Set(["xml", "xmlns"]);
  const collect = (current: Node) => {
    if (current.nodeType === Node.PROCESSING_INSTRUCTION_NODE || current.nodeType === Node.COMMENT_NODE) values.push(current.nodeValue ?? "");
    if (current.nodeType === Node.ELEMENT_NODE) {
      const element = current as Element;
      if (element.prefix) reserved.add(element.prefix);
      for (const attribute of [...element.attributes]) {
        if (attribute.prefix) reserved.add(attribute.prefix);
        if (attribute.namespaceURI === xmlns) reserved.add(attribute.name === "xmlns" ? "" : attribute.localName);
        else values.push(attribute.value);
      }
      values.push([...element.childNodes].filter(child => child.nodeType === Node.TEXT_NODE || child.nodeType === Node.CDATA_SECTION_NODE)
        .map(child => child.textContent ?? "").join(""));
    }
    for (const child of [...current.childNodes]) collect(child);
  };
  collect(node);
  values.push((node.nodeType === Node.DOCUMENT_NODE ? (node as Document).documentElement : node).textContent ?? "");
  let nextPrefix = 1;
  const fresh = () => {
    let prefix: string;
    do { prefix = `ns${nextPrefix++}`; } while (reserved.has(prefix) || values.some(value => value.includes(`${prefix}:`)));
    reserved.add(prefix); return prefix;
  };
  const writeElement = (element: Element, inherited: Bindings, captured: Bindings = new Map()): string => {
    const scope = new Map(inherited), own = new Map<string, string>(), added = new Map(captured);
    for (const attribute of [...element.attributes]) if (attribute.namespaceURI === xmlns) {
      const prefix = attribute.name === "xmlns" ? "" : attribute.localName;
      declaration(prefix, attribute.value); own.set(prefix, attribute.value); added.delete(prefix); scope.set(prefix, attribute.value);
    }
    for (const [prefix, namespace] of added) { declaration(prefix, namespace); scope.set(prefix, namespace); }
    const bind = (prefix: string, namespace: string) => {
      declaration(prefix, namespace);
      if (own.has(prefix) && own.get(prefix) !== namespace) fail("an explicit declaration conflicts with its element name");
      added.set(prefix, namespace); scope.set(prefix, namespace);
    };
    if (element.namespaceURI === xmlns || element.prefix === "xmlns" || element.localName.includes(":")) fail("element name is not namespace well-formed");
    const namespace = element.namespaceURI ?? "", prefix = element.prefix ?? "";
    if (prefix && !namespace || prefix === "xml" && namespace !== xml) fail("element prefix has no matching namespace");
    if (!namespace && scope.get("")) fail("an unqualified generated element needs an explicit default namespace undeclaration");
    if ((scope.get(prefix) ?? "") !== namespace) bind(prefix, namespace);
    const name = prefix ? `${prefix}:${element.localName}` : element.localName;
    const attributes: string[] = [];
    for (const attribute of [...element.attributes]) {
      let attributeName = attribute.name;
      if (attribute.namespaceURI !== xmlns && attribute.name === "xmlns") fail("namespace declarations need the XMLNS namespace");
      if (attribute.namespaceURI && attribute.namespaceURI !== xmlns) {
        let chosen = attribute.namespaceURI === xml ? "xml" : attribute.prefix;
        if (!chosen || scope.get(chosen) !== attribute.namespaceURI) {
          chosen = [...scope].find(([candidate, value]) => candidate && candidate !== "xmlns" && value === attribute.namespaceURI)?.[0] ?? null;
          if (!chosen) {
            chosen = fresh();
            bind(chosen, attribute.namespaceURI);
          }
        }
        attributeName = `${chosen}:${attribute.localName}`;
      } else if (attribute.namespaceURI !== xmlns && (attribute.prefix || attribute.name.includes(":"))) fail("attribute prefix has no namespace");
      attributes.push(`${attributeName}="${escape(attribute.value, true)}"`);
    }
    for (const [declared, value] of added) attributes.push(`${declared ? `xmlns:${declared}` : "xmlns"}="${escape(value, true)}"`);
    const opening = `<${name}${attributes.length ? ` ${attributes.join(" ")}` : ""}`;
    return element.childNodes.length ? `${opening}>${[...element.childNodes].map(child => write(child, scope)).join("")}</${name}>` : `${opening}/>`;
  };
  const write = (current: Node, bindings: Bindings): string => {
    switch (current.nodeType) {
      case Node.ELEMENT_NODE: return writeElement(current as Element, bindings);
      case Node.TEXT_NODE: return escape(current.nodeValue ?? "");
      case Node.CDATA_SECTION_NODE: {
        const value = current.nodeValue ?? ""; characters(value);
        if (value.includes("]]>") || value.includes("\r")) fail("CDATA cannot round-trip its closing delimiter or carriage return");
        return `<![CDATA[${value}]]>`;
      }
      case Node.COMMENT_NODE: {
        const value = current.nodeValue ?? ""; characters(value);
        if (value.includes("--") || value.endsWith("-") || value.includes("\r")) fail("comment cannot round-trip its delimiter or carriage return");
        return `<!--${value}-->`;
      }
      case Node.PROCESSING_INSTRUCTION_NODE: {
        const instruction = current as ProcessingInstruction; characters(instruction.data);
        if (instruction.target.toLowerCase() === "xml" || instruction.data.includes("?>") || instruction.data.includes("\r")
          || [" ", "\n", "\t"].includes(instruction.data[0])) fail("processing instruction cannot round-trip its content");
        return `<?${instruction.target}${instruction.data ? ` ${instruction.data}` : ""}?>`;
      }
      default: return fail(`unsupported XML node type ${current.nodeType}`);
    }
  };
  const initial: Bindings = new Map([["xml", xml], ["", ""]]);
  if (node.nodeType === Node.DOCUMENT_NODE) {
    if ([...node.childNodes].filter(child => child.nodeType === Node.ELEMENT_NODE).length !== 1) fail("a document needs one root element");
    return [...node.childNodes].map(child => {
      if (!new Set<number>([Node.ELEMENT_NODE, Node.TEXT_NODE, Node.COMMENT_NODE, Node.PROCESSING_INSTRUCTION_NODE]).has(child.nodeType)) return fail(`unsupported XML node type ${child.nodeType}`);
      if (child.nodeType === Node.TEXT_NODE && [...(child.nodeValue ?? "")].some(character => ![" ", "\r", "\n", "\t"].includes(character))) fail("document text must be whitespace");
      return write(child, initial);
    }).join("");
  }
  if (node.nodeType !== Node.ELEMENT_NODE) return fail("a fragment needs one actual root element");
  const captured: Bindings = new Map();
  for (let parent = node.parentElement; parent; parent = parent.parentElement) {
    for (const attribute of [...parent.attributes]) if (attribute.namespaceURI === xmlns) {
      const prefix = attribute.name === "xmlns" ? "" : attribute.localName;
      if (!captured.has(prefix)) captured.set(prefix, attribute.value);
    }
    if (parent.namespaceURI && !captured.has(parent.prefix ?? "")) captured.set(parent.prefix ?? "", parent.namespaceURI);
  }
  return writeElement(node as Element, initial, captured);
}
