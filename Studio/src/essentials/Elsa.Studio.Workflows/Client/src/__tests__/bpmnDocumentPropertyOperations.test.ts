import { describe, expect, it } from "vitest";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { bpmnAppearanceNamespaces as colors, readBpmnDocumentAppearance } from "../bpmn/bpmnDocumentAppearance";
import { planBpmnAppearance, planBpmnCondition, planBpmnDefaultFlow, planBpmnLabel, planBpmnLoop, planBpmnStandardProperty, readBpmnStandardProperties } from "../bpmn/bpmnDocumentPropertyOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

describe("source-owned standard properties", () => {
  it("edits an idless exact property and retains mixed vendor content and exact native references", () => {
    const owner = operationOwner(), original = readBpmnDocument(owner), next = applyBpmnDocumentTransaction(owner, planBpmnLabel(owner, operationScope, { elementId: "Note", diId: "NoteDI" }, "Edited note", { x: 25.125, y: 550.5, width: 120.25, height: 30 }).transaction);
    expect(readBpmnDocument(next).byId.get("Note")!.textContent).toBe("Edited note");
    expect(readBpmnDocument(next).payload.activities).toEqual(original.payload.activities);
    const properties = readBpmnStandardProperties(next, operationScope, { elementId: "A" });
    expect(properties.attributes.find(attribute => attribute.name === "id")?.readOnly).toBe(true);
    expect(properties.children[0].children[0].attributes[0]).toMatchObject({ name: "qname", value: "q:A" });
    expect(() => planBpmnStandardProperty(owner, operationScope, { kind: "text", target: { elementId: "P", path: [{ namespaceURI: ns.model, localName: "extensionElements" }] }, value: "discard" })).toThrow("mixed-content");
  });

  it("keeps raw expression text and configures default/condition without replacing existing opaque attributes", () => {
    const owner = operationOwner();
    const defaulted = applyBpmnDocumentTransaction(owner, planBpmnDefaultFlow(owner, operationScope, "A", "F").transaction);
    const next = applyBpmnDocumentTransaction(defaulted, planBpmnCondition(defaulted, operationScope, "F", "a < 4 && q.F === 'literal F'", "urn:language").transaction), parsed = readBpmnDocument(next);
    const condition = parsed.byId.get("F")!.getElementsByTagNameNS(ns.model, "conditionExpression")[0];
    expect(condition.textContent).toBe("a < 4 && q.F === 'literal F'");
    expect(condition.getAttribute("language")).toBe("urn:language");
    expect(condition.getAttributeNS("http://www.w3.org/2001/XMLSchema-instance", "type")).toMatch(/tFormalExpression$/);
    expect(parsed.byId.get("A")!.getAttribute("default")).toBe("F");
    expect(() => planBpmnDefaultFlow(owner, operationScope, "B", "F")).toThrow("outgoing");
    expect(() => planBpmnStandardProperty(owner, operationScope, { kind: "attribute", target: { elementId: "A" }, name: "default", value: "F" })).toThrow("dedicated");
  });

  it("changes MI ordering in place but requires an explicit choice before discarding loop configuration", () => {
    const owner = operationOwner(), parallel = applyBpmnDocumentTransaction(owner, planBpmnLoop(owner, operationScope, "B", "parallel").transaction);
    const configured = applyBpmnDocumentTransaction(parallel, planBpmnStandardProperty(parallel, operationScope, { kind: "appendProperty", parent: { elementId: "B", path: [{ namespaceURI: ns.model, localName: "multiInstanceLoopCharacteristics" }] }, xml: `<b:completionCondition xmlns:b="${ns.model}">done == 2</b:completionCondition>` }).transaction);
    const sequential = applyBpmnDocumentTransaction(configured, planBpmnLoop(configured, operationScope, "B", "sequential").transaction);
    expect(readBpmnDocument(sequential).byId.get("B")!.getElementsByTagNameNS(ns.model, "completionCondition")[0].textContent).toBe("done == 2");
    expect(() => planBpmnLoop(sequential, operationScope, "B", "none")).toThrow("explicit");
    const removed = applyBpmnDocumentTransaction(sequential, planBpmnLoop(sequential, operationScope, "B", "none", true).transaction);
    expect(readBpmnDocument(removed).byId.get("B")!.getElementsByTagNameNS(ns.model, "multiInstanceLoopCharacteristics")).toHaveLength(0);
  });

  it("scopes authored appearance to the actual DI and makes a private copy of a shared label style", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string)
      .replace('<bd:BPMNShape id="ADI"', `<bd:BPMNShape xmlns:legacy="${colors.legacyColor}" legacy:fill="red" id="ADI"`)
      .replace('<dc:Bounds x="100.25" y="100.5" width="100" height="80"/>', '<dc:Bounds x="100.25" y="100.5" width="100" height="80"/><bd:BPMNLabel labelStyle="Shared"/>')
      .replace('</bd:BPMNPlane></bd:BPMNDiagram>', '</bd:BPMNPlane><bd:BPMNLabelStyle id="Shared" xmlns:v="urn:style" v:qname="v:keep"><dc:Font name="Original font" size="12" isBold="true" v:keep="exact"/></bd:BPMNLabelStyle></bd:BPMNDiagram>');
    const plan = planBpmnAppearance(owner, operationScope, "ADI", { fill: "#abcdef", stroke: "rgb(20 30 40)", labelColor: "navy", font: { size: 15.5 } });
    const next = readBpmnDocument(applyBpmnDocumentTransaction(owner, plan.transaction)), di = next.byId.get("ADI")!, appearance = readBpmnDocumentAppearance(di);
    expect(appearance).toMatchObject({ fill: "#abcdef", labelColor: "navy", labelFont: { name: "Original font", size: 15.5, bold: true } });
    expect(next.byId.get("Shared")!.firstElementChild!.getAttribute("size")).toBe("12");
    const styleId = di.getElementsByTagNameNS(ns.diagram, "BPMNLabel")[0].getAttribute("labelStyle")!;
    expect(styleId).not.toBe("Shared");
    expect(next.byId.get(styleId)!.getAttributeNS("urn:style", "qname")).toBe("v:keep");
    expect(next.byId.get(styleId)!.lookupNamespaceURI("v")).toBe("urn:style");
    expect(next.byId.get(styleId)!.firstElementChild!.getAttributeNS("urn:style", "keep")).toBe("exact");
    const cleared = readBpmnDocument(applyBpmnDocumentTransaction(applyBpmnDocumentTransaction(owner, plan.transaction), planBpmnAppearance(applyBpmnDocumentTransaction(owner, plan.transaction), operationScope, "ADI", { fill: null }).transaction));
    expect(readBpmnDocumentAppearance(cleared.byId.get("ADI")!).fill).toBeUndefined();
  });

  it("refuses structural deletion/DTD/identity edits and source-address scope escapes before mutation", () => {
    const owner = operationOwner(), snapshot = structuredClone(owner);
    expect(() => planBpmnStandardProperty(owner, operationScope, { kind: "removeProperty", target: { elementId: "A" } })).toThrow("ownership-aware");
    expect(() => planBpmnStandardProperty(owner, operationScope, { kind: "appendProperty", parent: { elementId: "B" }, xml: '<!DOCTYPE a [<!ENTITY x "bad">]><script>&x;</script>' })).toThrow("DTD");
    expect(() => planBpmnStandardProperty(owner, { ...operationScope, diagramId: "SubDiagram", scopeElementId: "S" }, { kind: "attribute", target: { elementId: "B" }, name: "name", value: "outside" })).toThrow("outside");
    expect(() => planBpmnStandardProperty(owner, operationScope, { kind: "text", target: { elementId: "G" }, value: "invalid" })).toThrow("actual text property");
    expect(owner).toEqual(snapshot);
  });

  it("declares new paint/expression attribute namespaces explicitly without resolving retained opaque QName literals", () => {
    const owner = operationOwner();
    owner.structure!.payload.documentXml = (owner.structure!.payload.documentXml as string).replace('<bd:BPMNShape id="ADI"', '<bd:BPMNShape v:literal="colorNs:Unresolved" id="ADI"');
    const painted = readBpmnDocument(applyBpmnDocumentTransaction(owner, planBpmnAppearance(owner, operationScope, "ADI", { fill: "teal" }).transaction));
    const di = painted.byId.get("ADI")!;
    expect(di.lookupNamespaceURI("colorNs")).toBeNull();
    expect(di.getAttributeNS(colors.color, "background-color")).toBe("teal");
    expect(di.getAttributeNS("urn:vendor", "literal")).toBe("colorNs:Unresolved");
    const original = applyBpmnDocumentTransaction(owner, planBpmnCondition(owner, operationScope, "F", "'xsiNs:Unresolved'").transaction);
    const typed = readBpmnDocument(applyBpmnDocumentTransaction(original, planBpmnCondition(original, operationScope, "F", "'xsiNs:Unresolved'", "urn:language").transaction));
    const expression = typed.byId.get("F")!.getElementsByTagNameNS(ns.model, "conditionExpression")[0];
    expect(expression.lookupNamespaceURI("xsiNs")).toBeNull();
    expect(expression.getAttributeNS("http://www.w3.org/2001/XMLSchema-instance", "type")).toMatch(/tFormalExpression$/);
  });
});
