import { describe, expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentCommands, applyBpmnDocumentTransaction, bpmnNamespaces, readBpmnDocument,
  type BpmnDocumentConfigurationChange } from "../bpmn/bpmnDocument";
import { loadBpmnDocumentAuthoring } from "../bpmn/bpmnDocumentAuthoring";

const activity = (nodeId: string): ActivityNode => ({ nodeId, activityVersionId: `exact-${nodeId}`, inputs: [], outputs: [], module: { keep: true } });
function owner(): ActivityNode {
  return { ...activity("owner"), structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    documentXml: `<?xml version="1.0"?><b:definitions xmlns:b="${bpmnNamespaces.model}" xmlns:v="urn:vendor" targetNamespace="urn:transaction">
      <b:process id="P"><b:task id="T" name="A &amp; B"/><b:subProcess id="S"><b:task id="C"/><b:extensionElements><v:mixed>before <v:part/> after</v:mixed></b:extensionElements></b:subProcess></b:process>
      <b:process id="P2"><b:task id="Other"/></b:process></b:definitions>`,
    processId: "P", variables: [], activities: [activity("native"), activity("child"), activity("other")],
    bindings: [{ elementId: "T", childNodeId: "native", vendor: { keep: true } }, { elementId: "C", childNodeId: "child" }, { elementId: "Other", childNodeId: "other" }],
    scopeHeaders: [{ scopeElementId: "S", nodeId: "scope", activityVersionId: "exact-bpmn", inputs: [], outputs: [], variables: [], vendor: { keep: true } },
      { scopeElementId: "P2", nodeId: "process2", activityVersionId: "exact-bpmn2", inputs: [], outputs: [], variables: [] }],
    opaque: { untouched: [1, 2] }
  } } };
}
const payload = (value: ActivityNode) => readBpmnDocument(value).payload;

describe("one BPMN source/configuration transaction", () => {
  it("creates standard syntax and its exact native binding in one edit", () => {
    const before = owner(), original = structuredClone(before), data = payload(before);
    const next = applyBpmnDocumentTransaction(before, { commands: [{ kind: "append", parent: { elementId: "P" }, xml: `<b:serviceTask xmlns:b="${bpmnNamespaces.model}" id="New" name="New work"/>` }],
      configuration: { activities: [...data.activities, activity("new-native")], bindings: [...data.bindings, { elementId: "New", childNodeId: "new-native", opaque: "new" }] } });
    expect(before).toEqual(original);
    expect(payload(next).activities).toEqual([...data.activities, activity("new-native")]);
    expect(payload(next).bindings.at(-1)).toEqual({ elementId: "New", childNodeId: "new-native", opaque: "new" });
    expect(payload(next).scopeHeaders).toEqual(data.scopeHeaders);
    expect(payload(next).opaque).toEqual(data.opaque);
    expect(readBpmnDocument(next).byId.get("New")?.localName).toBe("serviceTask");
  });

  it("deletes bound syntax only with the matching native ownership transformation", () => {
    const before = owner(), original = structuredClone(before), data = payload(before);
    expect(() => applyBpmnDocumentCommands(before, [{ kind: "remove", target: { elementId: "T" } }])).toThrow(/native-binding/);
    const next = applyBpmnDocumentTransaction(before, { commands: [{ kind: "remove", target: { elementId: "T" } }],
      configuration: { activities: data.activities.filter(item => item.nodeId !== "native"), bindings: data.bindings.filter(item => item.elementId !== "T") } });
    expect(readBpmnDocument(next).byId.has("T")).toBe(false);
    expect(payload(next).activities).toEqual(data.activities.slice(1));
    expect(payload(next).bindings).toEqual(data.bindings.slice(1));
    expect(before).toEqual(original);
  });

  it("creates a configured standard scope and descendant without a second body", () => {
    const before = owner(), data = payload(before);
    const header = { scopeElementId: "NewScope", nodeId: "new-scope", activityVersionId: "exact-bpmn", inputs: [], outputs: [], variables: [], module: { retained: true } };
    const next = applyBpmnDocumentTransaction(before, { commands: [{ kind: "append", parent: { elementId: "P2" }, xml: `<b:transaction xmlns:b="${bpmnNamespaces.model}" id="NewScope"><b:task id="NewChild"/></b:transaction>` }],
      configuration: { activities: [...data.activities, activity("new-child")], bindings: [...data.bindings, { elementId: "NewChild", childNodeId: "new-child" }],
        scopeHeaders: [...data.scopeHeaders!, header] } });
    expect(payload(next).scopeHeaders!.at(-1)).toEqual(header);
    expect(payload(next).scopeHeaders!.at(-1)).not.toHaveProperty("structure");
    expect(readBpmnDocument(next).byId.get("NewChild")?.parentElement?.getAttribute("id")).toBe("NewScope");
  });

  it("removes a standard scope together with all owned configuration", () => {
    const before = owner(), data = payload(before);
    const commands = [{ kind: "remove" as const, target: { elementId: "S" } }];
    expect(() => applyBpmnDocumentTransaction(before, { commands, configuration: {
      activities: data.activities.filter(item => item.nodeId !== "child"), bindings: data.bindings.filter(item => item.elementId !== "C")
    } })).toThrow(/scope header/);
    const next = applyBpmnDocumentTransaction(before, { commands, configuration: {
      activities: data.activities.filter(item => item.nodeId !== "child"), bindings: data.bindings.filter(item => item.elementId !== "C"),
      scopeHeaders: data.scopeHeaders!.filter(header => header.scopeElementId !== "S")
    } });
    expect(readBpmnDocument(next).byId.has("S")).toBe(false);
    expect(payload(next).scopeHeaders).toEqual([data.scopeHeaders![1]]);
    expect(payload(next).bindings).toEqual([data.bindings[0], data.bindings[2]]);
  });

  it("keeps exact source bytes on native-only edits", () => {
    const before = owner(), data = payload(before);
    const next = applyBpmnDocumentTransaction(before, { configuration: {
      activities: data.activities.map(item => item.nodeId === "native" ? { ...item, inputs: [{ referenceKey: "Value", value: { expressionType: "Literal", value: 42 } }] } : item)
    } });
    expect(payload(next).documentXml).toBe(data.documentXml);
    expect(payload(next).bindings).toEqual(data.bindings);
    expect(payload(next).activities[0].inputs).toEqual([{ referenceKey: "Value", value: { expressionType: "Literal", value: 42 } }]);
    expect(data.activities[0].inputs).toEqual([]);
  });

  it("retains configured scope identities on standard reparenting", () => {
    const before = owner(), data = payload(before);
    const next = applyBpmnDocumentTransaction(before, { commands: [{ kind: "reparent", target: { elementId: "S" }, parent: { elementId: "P2" } }] });
    expect(readBpmnDocument(next).byId.get("S")?.parentElement?.getAttribute("id")).toBe("P2");
    expect(payload(next).scopeHeaders).toEqual(data.scopeHeaders);
    expect(payload(next).activities).toEqual(data.activities);
    expect(payload(next).bindings).toEqual(data.bindings);
    expect(readBpmnDocument(next).document.getElementsByTagNameNS("urn:vendor", "mixed")[0].textContent).toBe("before  after");
  });

  it("refuses an inconsistent final binding and leaves both inputs intact", () => {
    const before = owner(), original = structuredClone(before), data = payload(before);
    const configuration = { bindings: data.bindings.slice(1) };
    const copy = structuredClone(configuration);
    expect(() => applyBpmnDocumentTransaction(before, { commands: [{ kind: "remove", target: { elementId: "T" } }], configuration })).toThrow(/no binding/);
    expect(before).toEqual(original);
    expect(configuration).toEqual(copy);
  });

  it("refuses competing configuration authority before any source mutation", () => {
    const before = owner(), original = structuredClone(before);
    expect(() => applyBpmnDocumentTransaction(before, { commands: [{ kind: "remove", target: { elementId: "S" } }],
      configuration: { documentXml: "replacement" } as BpmnDocumentConfigurationChange })).toThrow(/semantic source/);
    expect(before).toEqual(original);
  });

  it("requires explicit arrays rather than accidentally erasing native configuration", () => {
    expect(() => applyBpmnDocumentTransaction(owner(), { configuration: { bindings: undefined } })).toThrow(/explicit arrays/);
  });

  it("returns the original owner and bytes for unchanged configuration", () => {
    const before = owner(), data = payload(before);
    const configuration: BpmnDocumentConfigurationChange = { activities: structuredClone(data.activities), bindings: structuredClone(data.bindings), variables: [] };
    expect(applyBpmnDocumentTransaction(before, { configuration })).toBe(before);
    expect(applyBpmnDocumentTransaction(before, {})).toBe(before);
  });

  it("exposes the same source authority through the deferred optional-canvas port", async () => {
    const port = await loadBpmnDocumentAuthoring();
    const before = owner();
    expect(port.readBpmnDocument(before).payload.documentXml).toBe(payload(before).documentXml);
    expect(port.applyBpmnDocumentTransaction(before, {})).toBe(before);
    expect(port.bpmnNamespaces).toBe(bpmnNamespaces);
  });
});
