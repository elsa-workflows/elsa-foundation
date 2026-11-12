import { describe, expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentCommands, bpmnNamespaces, readBpmnDocument, type BpmnXmlAddress } from "../bpmn/bpmnDocument";

function owner(): ActivityNode {
  return { nodeId: "owner", activityVersionId: "bpmn", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    processId: "P", activities: [], bindings: [], variables: [],
    documentXml: `<b:definitions xmlns:b="${bpmnNamespaces.model}" xmlns:v="urn:vendor" targetNamespace="urn:root"><b:process id="P"><b:task id="A"/><b:task id="B"/><b:task id="C"/></b:process><b:extensionElements><v:keep>exact</v:keep></b:extensionElements></b:definitions>`
  } } };
}

describe("explicit idless BPMN definitions address", () => {
  it("retains comments/instructions on no-op text and refuses an ambiguous replacement atomically", () => {
    const before = owner();
    before.structure!.payload.documentXml = `<b:definitions xmlns:b="${bpmnNamespaces.model}" targetNamespace="urn:root"><b:process id="P"><b:task id="A"><b:documentation>Before<!-- retained --><?vendor exact?>After</b:documentation></b:task></b:process></b:definitions>`;
    const target = { elementId: "A", path: [{ namespaceURI: bpmnNamespaces.model, localName: "documentation" }] };
    expect(applyBpmnDocumentCommands(before, [{ kind: "text", target, value: "BeforeAfter" }])).toBe(before);
    const original = structuredClone(before);
    expect(() => applyBpmnDocumentCommands(before, [{ kind: "text", target, value: "Edited" }])).toThrow("explicit correspondence");
    expect(before).toEqual(original);
  });

  it("keeps incoming before outgoing and both before Activity configuration on a reconnected self-loop", () => {
    const before = owner();
    const xml = `<b:definitions xmlns:b="${bpmnNamespaces.model}" targetNamespace="urn:root"><b:process id="P"><b:task id="A"><b:extensionElements/><b:standardLoopCharacteristics/></b:task><b:task id="B"/><b:sequenceFlow id="F" sourceRef="A" targetRef="B"/></b:process></b:definitions>`;
    before.structure!.payload.documentXml = xml;
    const after = readBpmnDocument(applyBpmnDocumentCommands(before, [{ kind: "reconnect", elementId: "F", sourceRef: "A", targetRef: "A" }]));
    const children = [...after.byId.get("A")!.children];
    expect(children.map(child => child.localName)).toEqual(["extensionElements", "incoming", "outgoing", "standardLoopCharacteristics"]);
    expect(children.filter(child => ["incoming", "outgoing"].includes(child.localName)).map(child => child.textContent)).toEqual(["F", "F"]);
  });

  it("adds document-level definitions without creating an ID or rewriting opaque content", () => {
    const before = owner();
    const next = applyBpmnDocumentCommands(before, [{ kind: "append", parent: { documentRoot: true },
      xml: `<b:message xmlns:b="${bpmnNamespaces.model}" id="Message" name="Order"/>` }]);
    const parsed = readBpmnDocument(next);
    expect(parsed.document.documentElement.hasAttribute("id")).toBe(false);
    expect(parsed.byId.get("Message")?.parentElement).toBe(parsed.document.documentElement);
    expect(parsed.document.getElementsByTagNameNS("urn:vendor", "keep")[0].textContent).toBe("exact");
    expect(readBpmnDocument(before).byId.has("Message")).toBe(false);
  });

  it("addresses idless standard root properties without selecting a same-named vendor element", () => {
    const next = applyBpmnDocumentCommands(owner(), [{ kind: "attribute", target: { documentRoot: true }, name: "exporter", value: "Elsa" }]);
    expect(readBpmnDocument(next).document.documentElement.getAttribute("exporter")).toBe("Elsa");
    expect(() => applyBpmnDocumentCommands(next, [{ kind: "text", target: { documentRoot: true,
      path: [{ namespaceURI: bpmnNamespaces.model, localName: "extensionElements" }] }, value: "discard" }])).toThrow("discard child");
  });

  it.each([{ documentRoot: true, elementId: "P" }, { documentRoot: false }, {}])("refuses an ambiguous or missing XML address %j atomically", address => {
    const before = owner(), snapshot = structuredClone(before);
    expect(() => applyBpmnDocumentCommands(before, [{ kind: "attribute", target: address as BpmnXmlAddress, name: "name", value: "bad" }])).toThrow();
    expect(before).toEqual(snapshot);
  });

  it("never permits deleting or changing the kind of the definitions root", () => {
    expect(() => applyBpmnDocumentCommands(owner(), [{ kind: "remove", target: { documentRoot: true } }])).toThrow();
    expect(() => applyBpmnDocumentCommands(owner(), [{ kind: "replaceType", target: { documentRoot: true }, localName: "process" }])).toThrow();
  });

  it("preserves source order when moving retained elements before an explicit anchor or to the end", () => {
    const moved = applyBpmnDocumentCommands(owner(), [{ kind: "reparent", target: { elementId: "C" }, parent: { elementId: "P" }, before: { elementId: "A" } }]);
    expect([...readBpmnDocument(moved).process.children].map(element => element.id)).toEqual(["C", "A", "B"]);
    const end = applyBpmnDocumentCommands(moved, [{ kind: "reparent", target: { elementId: "A" }, parent: { elementId: "P" }, before: null }]);
    expect([...readBpmnDocument(end).process.children].map(element => element.id)).toEqual(["C", "B", "A"]);
  });

  it("refuses a reparent anchor outside the requested parent", () => {
    expect(() => applyBpmnDocumentCommands(owner(), [{ kind: "reparent", target: { elementId: "A" }, parent: { documentRoot: true }, before: { elementId: "B" } }])).toThrow("direct child");
  });
});
