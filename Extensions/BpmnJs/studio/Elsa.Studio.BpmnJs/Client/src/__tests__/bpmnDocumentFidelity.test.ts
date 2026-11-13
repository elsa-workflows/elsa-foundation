import { describe, expect, it } from "vitest";
import { projectBpmnNativeDocument } from "../bpmnDocumentProjection";
import { nativeImportSignature, nativeSignature, nativeXml } from "../bpmnDocumentXml";
import { mapBpmnNativeCommandGroup } from "../bpmnDocumentCommands";
import { normalizeNativeDiIds } from "../bpmnDocumentDiAliases";
import { authoring, edit, modeled, owner, rawXml, scope } from "./bpmnDocumentFixture";

const ns = authoring.bpmnNamespaces;
const expression = (binding: string) => nativeXml(`<b:definitions xmlns:b="${ns.model}" xmlns:q="${binding}"><b:conditionExpression>q:Decision</b:conditionExpression></b:definitions>`);

describe("native source fidelity boundaries", () => {
  it("compares inherited opaque QName meaning in both import and detached subtree signatures", () => {
    const first = expression("urn:first"), second = expression("urn:second");
    expect(nativeImportSignature(first.documentElement, ns.model)).not.toBe(nativeImportSignature(second.documentElement, ns.model));
    expect(nativeSignature(first.documentElement.firstElementChild!)).not.toBe(nativeSignature(second.documentElement.firstElementChild!));
  });

  it("compares default and split-text QName context without rewriting opaque values", () => {
    for (const body of ["Decision", "q:<!-- kept --><![CDATA[Decision]]>"]) {
      const tree = (uri: string) => nativeXml(`<b:script xmlns:b="${ns.model}" xmlns="${uri}" xmlns:q="${uri}">${body}</b:script>`).documentElement;
      expect(nativeImportSignature(tree("urn:first"), ns.model)).not.toBe(nativeImportSignature(tree("urn:second"), ns.model));
      expect(nativeSignature(tree("urn:first"))).not.toBe(nativeSignature(tree("urn:second")));
    }
    const inherited = nativeXml(`<b:script xmlns:b="${ns.model}" xmlns:q="urn:same">q:Decision</b:script>`).documentElement;
    expect(nativeSignature(inherited)).toBe(nativeSignature(inherited.cloneNode(true) as Element));
  });

  it("preserves a restored root namespace beneath a shadow while constructing the native projection", () => {
    const source = owner(rawXml.replace('<bpmn:sequenceFlow id="flow" sourceRef="work" targetRef="gateway"/>',
      '<bpmn:sequenceFlow xmlns:q="urn:shadow" id="flow" sourceRef="work" targetRef="gateway"><bpmn:conditionExpression xmlns:q="urn:process">q:Decision</bpmn:conditionExpression></bpmn:sequenceFlow>'));
    const before = structuredClone(source), projected = projectBpmnNativeDocument(authoring, source, scope);
    const condition = nativeXml(projected.xml).getElementsByTagNameNS(ns.model, "conditionExpression")[0];
    expect(condition.lookupNamespaceURI("q")).toBe("urn:process");
    expect(condition.textContent).toBe("q:Decision");
    expect(source).toEqual(before);
  });

  it("retains restored namespace meaning while normalizing native DI aliases", () => {
    const xml = `<b:definitions xmlns:b="${ns.model}" xmlns:bd="${ns.diagram}" xmlns:q="urn:process"><bd:BPMNDiagram><bd:BPMNPlane xmlns:q="urn:shadow"><bd:BPMNShape id="native" xmlns:q="urn:process" label="q:Kept"/><bd:BPMNEdge id="edge" sourceElement="native"/></bd:BPMNPlane></bd:BPMNDiagram></b:definitions>`;
    const tree = nativeXml(normalizeNativeDiIds(authoring, xml, new Map([["native", "retained"]])));
    const shape = tree.getElementsByTagNameNS(ns.diagram, "BPMNShape")[0];
    expect(shape.getAttribute("id")).toBe("retained");
    expect(shape.getAttribute("label")).toBe("q:Kept");
    expect(shape.lookupNamespaceURI("q")).toBe("urn:process");
    expect(tree.getElementsByTagNameNS(ns.diagram, "BPMNEdge")[0].getAttribute("sourceElement")).toBe("retained");
  });

  it("preserves opaque QName context when the actual writer aliases a nested declaration", async () => {
    const source = owner(rawXml.replace('<bpmn:sequenceFlow id="flow" sourceRef="work" targetRef="gateway"/>',
      '<bpmn:sequenceFlow id="flow" sourceRef="work" targetRef="gateway"><bpmn:conditionExpression xmlns:q="urn:expression">q:Decision</bpmn:conditionExpression></bpmn:sequenceFlow>'));
    // The actual pinned writer aliases the nested declaration without rewriting opaque body text.
    const result = await modeled(source);
    expect(result.projection.sourceXml).toBe(source.structure!.payload.documentXml);
    expect(result.baseline.xml).not.toBe(result.projection.sourceXml);
  });

  it("retains the namespace meaning of a newly authored unprefixed typed QName", async () => {
    const source = owner(rawXml.replace('<bpmn:subProcess id="sub">', '<bpmn:callActivity id="call" calledElement="v:Original"/><bpmn:subProcess id="sub">'));
    const { baseline } = await modeled(source);
    const next = edit(baseline.xml, (_document, index) => { const call = index.get("call")!;
      call.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns", ns.model); call.setAttribute("calledElement", "ExternalProcess"); });
    const commands = mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{
      command: "element.updateProperties", elementId: "call", properties: { calledElement: "ExternalProcess" }
    }] }, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(source, commands)).byId.get("call")!;
    const value = retained.getAttribute("calledElement")!, prefix = value.includes(":") ? value.split(":")[0] : null;
    expect(retained.lookupNamespaceURI(prefix)).toBe(ns.model);
    expect(value.split(":").at(-1)).toBe("ExternalProcess");
    expect(authoring.readBpmnDocument(source).byId.get("call")!.getAttribute("calledElement")).toBe("v:Original");
  });

  it("checks the declared scalar value on an exact idless property path", async () => {
    const source = owner(rawXml.replace('</bpmndi:BPMNDiagram>', '<bpmndi:BPMNLabelStyle id="style"><dc:Font name="Arial" size="14"/></bpmndi:BPMNLabelStyle></bpmndi:BPMNDiagram>'));
    const { baseline } = await modeled(source);
    const next = edit(baseline.xml, (_document, index) => index.get("style")!.firstElementChild!.setAttribute("size", "19"));
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{
      command: "element.updateModdleProperties", moddleOwnerId: "style", moddlePath: ["font"], propertyNames: ["size"], properties: { size: 18 }
    }] }, next)).toThrow("command intent");
  });

  it("refuses undeclared sibling fields on the exact idless modeled property", async () => {
    const source = owner(rawXml.replace('</bpmndi:BPMNDiagram>', '<bpmndi:BPMNLabelStyle id="style"><dc:Font name="Arial" size="14"/></bpmndi:BPMNLabelStyle></bpmndi:BPMNDiagram>'));
    const { baseline } = await modeled(source), before = structuredClone(source);
    const next = edit(baseline.xml, (_document, index) => { const font = index.get("style")!.firstElementChild!;
      font.setAttribute("size", "18"); font.setAttribute("name", "Unexpected"); });
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{
      command: "element.updateModdleProperties", moddleOwnerId: "style", moddlePath: ["font"], propertyNames: ["size"], properties: { size: 18 }
    }] }, next)).toThrow(/unaccounted|command intent/);
    expect(source).toEqual(before);
  });
});
