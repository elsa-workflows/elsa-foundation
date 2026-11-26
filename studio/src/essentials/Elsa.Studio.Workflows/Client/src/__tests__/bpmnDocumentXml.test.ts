import { describe, expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentCommands, bpmnNamespaces, readBpmnDocument } from "../bpmn/bpmnDocument";
import { serializeBpmnXml } from "../bpmn/bpmnDocumentXml";

const parse = (source: string) => new DOMParser().parseFromString(source, "application/xml");
const namespace = "http://www.w3.org/2000/xmlns/";
function owner(): ActivityNode {
  return { nodeId: "owner", activityVersionId: "bpmn@exact", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    processId: "P", activities: [], bindings: [], variables: [],
    documentXml: `<?xml version="1.0"?>\n<b:definitions xmlns:b="${bpmnNamespaces.model}" xmlns:q="urn:test" xmlns:v="urn:vendor" targetNamespace="urn:test">
 <b:process id="P" xml:base="https://source.example/" xml:lang="nl" xml:space="preserve">
  <b:exclusiveGateway id="G"><b:extensionElements><v:exact qname="q:G">q<!-- keep -->:<![CDATA[Thing]]><?vendor q:Instruction?></v:exact></b:extensionElements></b:exclusiveGateway>
  <b:transaction xmlns:q="urn:other" xml:base="https://target.example/" xml:lang="en" xml:space="default" id="TX"/>
 </b:process></b:definitions>`
  } } };
}

describe("namespace-preserving canonical XML serialization", () => {
  it("retains root declarations restored below shadowing together with opaque mixed content and XML context", () => {
    const original = owner(), snapshot = structuredClone(original);
    const next = applyBpmnDocumentCommands(original, [{ kind: "reparent", target: { elementId: "G" }, parent: { elementId: "TX" } }]);
    const parsed = readBpmnDocument(next), moved = parsed.byId.get("G")!, opaque = moved.getElementsByTagNameNS("urn:vendor", "exact")[0];
    expect(moved.parentElement).toBe(parsed.byId.get("TX"));
    expect(moved.lookupNamespaceURI("q")).toBe("urn:test");
    expect(opaque.lookupNamespaceURI("q")).toBe("urn:test");
    expect(opaque.getAttribute("qname")).toBe("q:G");
    expect(opaque.textContent).toBe("q:Thing");
    expect([...opaque.childNodes].map(node => node.nodeType)).toEqual([3, 8, 3, 4, 7]);
    for (const [name, value] of [["base", "https://source.example/"], ["lang", "nl"], ["space", "preserve"]])
      expect(moved.getAttributeNS("http://www.w3.org/XML/1998/namespace", name)).toBe(value);
    expect(parsed.byId.get("TX")!.lookupNamespaceURI("q")).toBe("urn:other");
    expect(original).toEqual(snapshot);
  });

  it("never collapses an explicit redundant declaration that becomes meaningful below another binding", () => {
    const document = parse('<root xmlns:q="urn:old"><target xmlns:q="urn:new"><restored xmlns:q="urn:old" literal="q:Value"/></target></root>');
    const output = parse(serializeBpmnXml(document));
    expect(output.getElementsByTagName("restored")[0].lookupNamespaceURI("q")).toBe("urn:old");
    expect(output.getElementsByTagName("restored")[0].getAttributeNS(namespace, "q")).toBe("urn:old");
  });

  it("captures inherited namespace declarations for standalone fragments without changing their DOM", () => {
    const document = parse('<root xmlns:q="urn:old" xmlns:v="urn:vendor"><child xmlns:q="urn:local"><v:keep literal="q:Value"/></child></root>');
    const element = document.getElementsByTagName("keep")[0] ?? document.getElementsByTagNameNS("urn:vendor", "keep")[0];
    const before = [...element.attributes].map(attribute => [attribute.name, attribute.value]);
    const output = parse(serializeBpmnXml(element));
    expect(output.documentElement.lookupNamespaceURI("q")).toBe("urn:local");
    expect(output.documentElement.namespaceURI).toBe("urn:vendor");
    expect([...element.attributes].map(attribute => [attribute.name, attribute.value])).toEqual(before);
  });

  it("fixes generated names without resolving opaque unbound QName values, including split text", () => {
    const document = parse('<root><keep literal="ns1:Unbound">ns<!-- keep -->2:<![CDATA[Unbound]]></keep><mixed>ns<piece>3:</piece>Unbound</mixed><?vendor ns4:Unbound?></root>');
    const value = 'tab\tline\nreturn\rquote"&<>😀';
    document.documentElement.setAttributeNS("urn:generated", "flag", value);
    const output = parse(serializeBpmnXml(document)), root = output.documentElement;
    expect(root.getAttributeNS("urn:generated", "flag")).toBe(value);
    expect(root.lookupNamespaceURI("ns1")).toBeNull();
    expect(root.lookupNamespaceURI("ns2")).toBeNull();
    expect(root.lookupNamespaceURI("ns3")).toBeNull();
    expect(root.lookupNamespaceURI("ns4")).toBeNull();
    expect(root.getElementsByTagName("keep")[0].textContent).toBe("ns2:Unbound");
  });

  it("keeps element and attribute expanded names when their generated prefixes differ", () => {
    const document = parse('<root/>'), generated = document.createElementNS("urn:element", "p:node");
    generated.setAttributeNS("urn:attribute", "p:flag", "p:Value"); document.documentElement.append(generated);
    const output = parse(serializeBpmnXml(document)), node = output.getElementsByTagNameNS("urn:element", "node")[0];
    expect(node.getAttributeNS("urn:attribute", "flag")).toBe("p:Value");
    expect(node.lookupNamespaceURI("p")).toBe("urn:element");
  });

  it("retains character references in edited text and attributes without newline normalization loss", () => {
    const original = owner(), value = 'tab\tline\nreturn\rquote"&<>😀';
    const next = applyBpmnDocumentCommands(original, [{ kind: "attribute", target: { elementId: "G" }, name: "name", value },
      { kind: "append", parent: { elementId: "G" }, xml: `<b:documentation xmlns:b="${bpmnNamespaces.model}"/>` }]);
    const edited = applyBpmnDocumentCommands(next, [{ kind: "text", target: { elementId: "G", path: [{ namespaceURI: bpmnNamespaces.model, localName: "documentation" }] }, value }]);
    const node = readBpmnDocument(edited).byId.get("G")!;
    expect(node.getAttribute("name")).toBe(value);
    expect(node.getElementsByTagNameNS(bpmnNamespaces.model, "documentation")[0].textContent).toBe(value);
  });

  it("retains an explicit absent default namespace when inserting a fragment", () => {
    const original = owner();
    original.structure!.payload.documentXml = String(original.structure!.payload.documentXml).replaceAll("b:", "").replace("xmlns:b=", "xmlns=");
    const next = applyBpmnDocumentCommands(original, [{ kind: "append", parent: { elementId: "G" }, xml: '<unqualified literal="Type"/>' }]);
    const element = readBpmnDocument(next).byId.get("G")!.getElementsByTagName("unqualified")[0];
    expect(element.namespaceURI).toBeNull();
    expect(element.lookupNamespaceURI(null)).toBeNull();
  });

  it("refuses inserted content that would acquire a previously unbound opaque prefix", () => {
    const original = owner(), snapshot = structuredClone(original);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "append", parent: { elementId: "G" }, xml: '<unqualified literal="q:Unbound"/>' }])).toThrow("opaque QName");
    expect(original).toEqual(snapshot);
  });

  it("keeps original no-op bytes including prolog, whitespace and declaration spelling", () => {
    const original = owner();
    expect(applyBpmnDocumentCommands(original, [{ kind: "attribute", target: { elementId: "G" }, name: "idempotent", value: null }])).toBe(original);
  });

  it.each(["\u0000", "\ud800", "\uffff"])("refuses an invalid XML character %j atomically", value => {
    const original = owner(), snapshot = structuredClone(original);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "attribute", target: { elementId: "G" }, name: "name", value }])).toThrow("XML 1.0 character");
    expect(original).toEqual(snapshot);
  });

  it.each([["comment", "bad--comment"], ["comment", "trailing-"], ["comment", "line\rreturn"], ["cdata", "]]>"], ["cdata", "line\rreturn"], ["pi", "bad?>data"], ["pi", "line\rreturn"], ["pi", "  q:Value"]])("refuses unrepresentable %s content %j", (kind, value) => {
    const document = parse('<root/>');
    const node = kind === "comment" ? document.createComment("safe") : kind === "cdata" ? document.createCDATASection("safe") : document.createProcessingInstruction("vendor", "safe");
    node.data = value; document.documentElement.append(node);
    expect(() => serializeBpmnXml(document)).toThrow();
    expect(node.data).toBe(value);
  });

  it.each(["", "q:Value", "q:Value\nnext"])("retains processing instruction data %j", value => {
    const document = parse('<root/>'); document.documentElement.append(document.createProcessingInstruction("vendor", value));
    const output = parse(serializeBpmnXml(document));
    expect((output.documentElement.firstChild as ProcessingInstruction).data).toBe(value);
  });

  it("refuses unsupported document types rather than dropping their content", () => {
    expect(() => serializeBpmnXml(parse('<!DOCTYPE root><root/>'))).toThrow("unsupported XML node");
  });

  it("refuses namespace-unaware declarations and conflicting generated element bindings", () => {
    const document = parse('<root/>');
    document.documentElement.setAttribute("xmlns", "urn:misleading");
    expect(() => serializeBpmnXml(document)).toThrow("XMLNS");
    const element = document.createElementNS("urn:name", "p:generated");
    element.setAttributeNS(namespace, "xmlns:p", "urn:other");
    expect(() => serializeBpmnXml(element)).toThrow("conflicts");
  });
});
