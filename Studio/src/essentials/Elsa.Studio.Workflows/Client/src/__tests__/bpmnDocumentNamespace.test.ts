import { describe, expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentCommands, bpmnNamespaces, readBpmnDocument } from "../bpmn/bpmnDocument";

function owner(content = ""): ActivityNode {
  return { nodeId: "owner", activityVersionId: "bpmn", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    documentXml: `<b:definitions xmlns:b="${bpmnNamespaces.model}" xmlns:v="urn:vendor" targetNamespace="urn:namespace"><b:process id="P"><b:callActivity id="T">${content}</b:callActivity></b:process></b:definitions>`,
    processId: "P", activities: [], bindings: [], variables: []
  } } };
}

describe("explicit namespace introduction", () => {
  it("introduces a new QName prefix at its standard owner before writing the field", () => {
    const before = owner();
    const next = applyBpmnDocumentCommands(before, [{ kind: "namespace", target: { elementId: "T" }, prefix: "external", namespaceURI: "urn:external" },
      { kind: "attribute", target: { elementId: "T" }, name: "calledElement", value: "external:Process" }]);
    const target = readBpmnDocument(next).byId.get("T")!;
    expect(target.getAttribute("calledElement")).toBe("external:Process");
    expect(target.lookupNamespaceURI("external")).toBe("urn:external");
    expect(readBpmnDocument(next).process.lookupNamespaceURI("external")).toBeNull();
    expect(readBpmnDocument(before).byId.get("T")!.hasAttribute("calledElement")).toBe(false);
  });

  it("keeps exact bytes for an already correct inherited declaration", () => {
    const before = owner();
    expect(applyBpmnDocumentCommands(before, [{ kind: "namespace", target: { elementId: "T" }, prefix: "v", namespaceURI: "urn:vendor" }])).toBe(before);
  });

  it("refuses rebinding a prefix that owns retained names or opaque QName values", () => {
    const before = owner('<b:extensionElements><v:keep ref="v:Thing"/></b:extensionElements>');
    expect(() => applyBpmnDocumentCommands(before, [{ kind: "namespace", target: { elementId: "T" }, prefix: "v", namespaceURI: "urn:other" }])).toThrow("rebind");
  });

  it("cannot bypass the opaque QName guard through implicit attribute namespace serialization", () => {
    const before = owner('<b:extensionElements><v:keep ref="ns1:Thing"/></b:extensionElements>');
    expect(() => applyBpmnDocumentCommands(before, [{ kind: "attribute", target: { elementId: "T" }, name: "fresh:flag", namespaceURI: "urn:fresh", value: "true" }])).toThrow("explicitly declared");
    expect(() => applyBpmnDocumentCommands(before, [{ kind: "attribute", target: { elementId: "T" }, name: "flag", namespaceURI: "urn:fresh", value: "true" }])).toThrow("explicitly declared");
    expect(readBpmnDocument(before).byId.get("T")!.lookupNamespaceURI("fresh")).toBeNull();
  });

  it("permits an explicit safe declaration and namespaced write without changing descendant context", () => {
    const next = applyBpmnDocumentCommands(owner('<b:extensionElements><v:keep ref="ns1:Thing"/></b:extensionElements>'), [{ kind: "namespace", target: { elementId: "T" }, prefix: "fresh", namespaceURI: "urn:fresh" },
      { kind: "attribute", target: { elementId: "T" }, name: "fresh:flag", namespaceURI: "urn:fresh", value: "true" }]);
    expect(readBpmnDocument(next).byId.get("T")!.getAttributeNS("urn:fresh", "flag")).toBe("true");
    expect(readBpmnDocument(next).document.getElementsByTagNameNS("urn:vendor", "keep")[0].lookupNamespaceURI("ns1")).toBeNull();
  });

  it.each(['<b:extensionElements><v:keep ref="fresh:Thing"/></b:extensionElements>',
    '<b:extensionElements><v:keep>fresh:Thing</v:keep></b:extensionElements>',
    '<b:extensionElements><v:keep><![CDATA[fresh:Thing]]></v:keep></b:extensionElements>',
    '<b:extensionElements><v:keep>fresh<![CDATA[:Thing]]></v:keep></b:extensionElements>',
    '<b:extensionElements><v:keep>fre<!-- retained -->sh:Thing</v:keep></b:extensionElements>',
    '<b:extensionElements><v:keep>fresh:<?vendor keep?>Thing</v:keep></b:extensionElements>'])("does not resolve previously opaque unbound content %s", content => {
    expect(() => applyBpmnDocumentCommands(owner(content), [{ kind: "namespace", target: { elementId: "T" }, prefix: "fresh", namespaceURI: "urn:fresh" }])).toThrow("opaque QName");
  });

  it("preserves a descendant's independent binding of the newly introduced prefix", () => {
    const next = applyBpmnDocumentCommands(owner('<b:extensionElements><v:keep xmlns:fresh="urn:local" ref="fresh:Thing"/></b:extensionElements>'),
      [{ kind: "namespace", target: { elementId: "T" }, prefix: "fresh", namespaceURI: "urn:fresh" }]);
    const parsed = readBpmnDocument(next);
    expect(parsed.byId.get("T")!.lookupNamespaceURI("fresh")).toBe("urn:fresh");
    expect(parsed.document.getElementsByTagNameNS("urn:vendor", "keep")[0].lookupNamespaceURI("fresh")).toBe("urn:local");
  });

  it.each(['<b:extensionElements><v:keep ref="fresh:Thing"/></b:extensionElements>',
    '<b:extensionElements><v:keep>fresh:Thing</v:keep></b:extensionElements>',
    '<b:extensionElements><v:keep><![CDATA[fresh:Thing]]></v:keep></b:extensionElements>',
    '<b:extensionElements><v:keep>fresh<![CDATA[:Thing]]></v:keep></b:extensionElements>',
    '<b:extensionElements><v:keep>fre<!-- retained -->sh:Thing</v:keep></b:extensionElements>',
    '<b:extensionElements><v:keep>fresh:<?vendor keep?>Thing</v:keep></b:extensionElements>'])("refuses newly resolved opaque QNames on reparenting %s", content => {
    const before = owner(content);
    before.structure!.payload.documentXml = String(before.structure!.payload.documentXml)
      .replace('</b:process>', '<b:subProcess id="S" xmlns:fresh="urn:new"/></b:process>');
    const snapshot = structuredClone(before);
    expect(() => applyBpmnDocumentCommands(before, [{ kind: "reparent", target: { elementId: "T" }, parent: { elementId: "S" } }])).toThrow("opaque QName");
    expect(before).toEqual(snapshot);
  });

  it.each(['<b:extensionElements><v:keep xmlns:fresh="urn:local" ref="fresh:Thing"/></b:extensionElements>',
    '<b:extensionElements><v:keep ref="unrelated:Thing"/></b:extensionElements>'])("allows reparenting when destination declarations do not resolve opaque content %s", content => {
    const before = owner(content);
    before.structure!.payload.documentXml = String(before.structure!.payload.documentXml)
      .replace('</b:process>', '<b:subProcess id="S" xmlns:fresh="urn:new"/></b:process>');
    const next = applyBpmnDocumentCommands(before, [{ kind: "reparent", target: { elementId: "T" }, parent: { elementId: "S" } }]);
    const parsed = readBpmnDocument(next);
    expect(parsed.byId.get("T")!.parentElement!.getAttribute("id")).toBe("S");
    const keep = parsed.document.getElementsByTagNameNS("urn:vendor", "keep")[0];
    expect(keep.getAttribute("ref")).toBe(content.includes("unrelated:") ? "unrelated:Thing" : "fresh:Thing");
    expect(keep.lookupNamespaceURI(content.includes("unrelated:") ? "unrelated" : "fresh")).toBe(content.includes("unrelated:") ? null : "urn:local");
  });

  it.each([["", "urn:new"], ["bad:prefix", "urn:new"], ["xmlns", "urn:new"], ["xml", "urn:wrong"],
    ["other", "http://www.w3.org/XML/1998/namespace"], ["other", "http://www.w3.org/2000/xmlns/"], ["other", ""]])("refuses reserved/invalid prefix or namespace %s %s", (prefix, namespaceURI) => {
    expect(() => applyBpmnDocumentCommands(owner(), [{ kind: "namespace", target: { documentRoot: true }, prefix, namespaceURI }])).toThrow();
  });
});
