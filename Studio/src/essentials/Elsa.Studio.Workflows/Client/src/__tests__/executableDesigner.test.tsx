import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it } from "vitest";
import { buildExecutableActivityGraph } from "../executableGraph";
import { buildExecutableInspectorCanvas, WorkflowExecutableCanvas } from "../workflow-editor/WorkflowExecutableInspector";
import { sequenceActivity, writeLine, bpmnActivity } from "./fixtures";
import { formatActivitySummary } from "../activitySummary";
import type { ScopeFrame } from "../workflowAdapter";
import type { WorkflowExecutableDetails, WorkflowExecutableNode } from "../workflowTypes";

const catalog = [sequenceActivity, writeLine, bpmnActivity];
const leaf = (id: string): WorkflowExecutableNode => ({ executableNodeId: `exec-${id}`, authoredActivityId: id,
  activityType: writeLine.activityTypeKey, activityTypeVersion: writeLine.version, inputBindings: [], childSlots: [] });
const sequence = (id: string, children: WorkflowExecutableNode[]): WorkflowExecutableNode => ({ ...leaf(id),
  activityType: sequenceActivity.activityTypeKey, structureKind: "elsa.sequence.structure",
  childSlots: [{ name: "Sequence.Activities", activities: children }] });
const detail = { artifactId: "artifact-pinned", references: [] } as unknown as WorkflowExecutableDetails;
const containers: { element: HTMLDivElement; root: ReturnType<typeof createRoot> }[] = [];
afterEach(() => containers.splice(0).forEach(({ element, root }) => { flushSync(() => root.unmount()); element.remove(); }));

function mount(tree: WorkflowExecutableNode) {
  const graph = buildExecutableActivityGraph(tree, catalog);
  function Host() {
    const [frames, setFrames] = useState<ScopeFrame[]>([]);
    const [selectedNodeId, select] = useState<string | null>(null);
    return <WorkflowExecutableCanvas detail={detail} activityCatalog={catalog} graph={graph} chosenReference={null}
      displayName="Pinned workflow"
      frames={frames} onNavigateToScope={setFrames} selectedNodeId={selectedNodeId} onSelectNode={select} />;
  }
  const element = document.createElement("div"); document.body.append(element);
  const root = createRoot(element); containers.push({ element, root });
  flushSync(() => root.render(<Host />));
  return { element, graph };
}

describe("Executable designer parity", () => {
  it("previews pinned authored inputs using the same formatter as the editor", () => {
    const line = { ...writeLine, inputs: [{ name: "Text", referenceKey: "Text", type: "String", isBrowsable: true }] };
    const sources = [{ executableNodeId: "exec-first", inputKey: "Text", expressionType: "Literal", value: "Pinned hello" }];
    const graph = buildExecutableActivityGraph(sequence("root", [leaf("first")]), [sequenceActivity, line], sources, [], "visible");
    const canvas = buildExecutableInspectorCanvas(graph, [sequenceActivity, line], [], [], () => {});
    expect(canvas.nodes[0].data.summary).toBe('"Pinned hello"');
    expect(canvas.nodes[0].data.summary).toBe(formatActivitySummary({ ...graph.root, inputs: [{ referenceKey: "Text", value: { expressionType: "Literal", value: "Pinned hello" } }] }, line));
  });

  it("previews the unique reusable root sidecar under its authored identity", () => {
    const line = { ...writeLine, inputs: [{ name: "Text", referenceKey: "Text", type: "String", isBrowsable: true }] };
    const graph = buildExecutableActivityGraph({ ...leaf("root"), executableNodeId: "placed-1" }, [line],
      [{ executableNodeId: "root", inputKey: "Text", expressionType: "Literal", value: "Pinned root", accessState: "allowed" }], [], "allowed");
    const canvas = buildExecutableInspectorCanvas(graph, [line], [], [], () => {});
    expect(canvas.nodes[0].data.summary).toBe('"Pinned root"');
    const hidden = buildExecutableActivityGraph({ ...leaf("root"), executableNodeId: "placed-1" }, [line],
      [{ executableNodeId: "root", inputKey: "Text", expressionType: "Literal", value: "MUST NOT APPEAR", isSensitive: true }], [], "allowed");
    expect(hidden.root.inputs).toEqual([]);
  });

  it("never borrows an ambiguous authored sidecar for repeated placements", () => {
    const tree = sequence("root", [{ ...leaf("root"), executableNodeId: "placed-1" }]);
    const graph = buildExecutableActivityGraph(tree, catalog,
      [{ executableNodeId: "root", inputKey: "Text", expressionType: "Literal", value: "AMBIGUOUS" }], [], "allowed");
    expect(graph.root.inputs).toEqual([]);
    expect(JSON.stringify(graph.root)).not.toContain("AMBIGUOUS");
  });

  it.each(["permissionHidden", "sensitive", "bindingSensitive", "recordHidden"])("does not preview a %s input", protection => {
    const line = { ...writeLine, inputs: [{ name: "Text", referenceKey: "Text", type: "String", isBrowsable: true }] };
    const sources = [{ executableNodeId: "exec-first", inputKey: "Text", expressionType: "Literal", value: "MUST NOT APPEAR",
      isSensitive: protection === "sensitive", accessState: protection === "recordHidden" ? "denied" : "visible" }];
    const bindings = protection === "bindingSensitive" ? [{ executableNodeId: "exec-first", binding: { inputName: "Text", inputKey: "Text", source: "Literal", isSensitive: true } }] : [];
    const graph = buildExecutableActivityGraph(sequence("root", [leaf("first")]), [sequenceActivity, line], sources, bindings, protection === "permissionHidden" ? "permissionHidden" : "visible");
    const canvas = buildExecutableInspectorCanvas(graph, [sequenceActivity, line], [], [], () => {});
    expect(canvas.nodes[0].data.summary).toBeUndefined();
    expect(graph.root.structure?.payload).not.toBeNull();
    expect(JSON.stringify(graph.root)).not.toContain("MUST NOT APPEAR");
  });
  it("uses the definition Sequence designer with selection, nested scope navigation and no editing", () => {
    const { element, graph } = mount(sequence("root", [leaf("first"), sequence("nested", [leaf("inner")])]));
    const before = structuredClone(graph.root);
    expect(element.querySelector('[aria-label="Sequence steps"]')).not.toBeNull();
    expect(element.querySelectorAll(".wf-sequence-step")).toHaveLength(2);
    const first = element.querySelector<HTMLButtonElement>('[data-id="first"]')!;
    flushSync(() => first.click());
    expect(first.getAttribute("aria-pressed")).toBe("true");
    expect(first.draggable).toBe(false);
    first.dispatchEvent(new KeyboardEvent("keydown", { key: "Delete", bubbles: true }));
    expect(element.querySelectorAll(".wf-sequence-step")).toHaveLength(2);
    expect(element.querySelector(".wf-sequence-insert, .wf-sequence-actions")).toBeNull();
    const open = element.querySelector<HTMLButtonElement>(".wf-sequence-slot")!;
    flushSync(() => open.click());
    expect(element.querySelectorAll(".wf-sequence-step")).toHaveLength(1);
    expect(element.querySelector('[data-id="inner"]')).not.toBeNull();
    expect(element.textContent).toContain("Sequence / Activities");
    const back = element.querySelector<HTMLButtonElement>('.wf-instance-breadcrumb button')!;
    flushSync(() => back.click());
    expect(element.querySelectorAll(".wf-sequence-step")).toHaveLength(2);
    expect(graph.root).toEqual(before);
  });

  it("uses paired compensation steps from the pinned executable", () => {
    const tree = { ...leaf("scope"), activityType: "CompensationScope", structureKind: "elsa.compensation-scope.structure",
      childSlots: [{ name: "CompensationScope.Step[Reserve]", activities: [leaf("reserve")] },
        { name: "CompensationScope.Compensation[Reserve]", activities: [leaf("release")] }] };
    const { element } = mount(tree);
    expect(element.querySelector(".wf-compensation-designer")).not.toBeNull();
    expect(element.querySelectorAll("input, .wf-sequence-insert")).toHaveLength(0);
    expect(element.textContent).toContain("Reserve");
  });

  it("keeps an empty scope's designer even when the server omits empty child slots", () => {
    const { element } = mount({ ...sequence("root", []), childSlots: [] });
    expect(element.textContent).toContain("This sequence has no steps.");
  });

  it("keeps a leaf without a supported designer on the read-only fallback", () => {
    const graph = buildExecutableActivityGraph(leaf("first"), catalog);
    const canvas = buildExecutableInspectorCanvas(graph, catalog, [], [], () => {});
    expect(canvas.mode).toBe("graph");
    expect(canvas.nodes.map(node => node.id)).toEqual(["first"]);
    expect(canvas.nodes[0].data.childSlots).toHaveLength(0);
  });

  it("shows the shared empty Sequence state without an add control", () => {
    const { element } = mount(sequence("root", []));
    expect(element.textContent).toContain("This sequence has no steps.");
    expect(element.querySelector(".wf-sequence-insert")).toBeNull();
  });

  it("keeps catalog-miss activities visibly unavailable in a Sequence", () => {
    const { element } = mount(sequence("root", [{ ...leaf("ghost"), activityType: "Missing.Activity" }]));
    expect(element.querySelector('[aria-label="Activity unavailable in this environment"]')).not.toBeNull();
    expect(element.querySelector(".wf-sequence-type")?.textContent).toBe("Unavailable activity");
  });

  it("uses BPMN elements and exact binding identities rather than a generic child graph", () => {
    const tree: WorkflowExecutableNode = { ...leaf("bpmn"), activityType: bpmnActivity.activityTypeKey,
      structureKind: "elsa.bpmn.structure", childSlots: [{ name: "Bpmn.Activities", activities: [leaf("first")] }],
      bpmnStructure: { elements: [{ elementId: "task", elementType: "task", childNodeId: "exec-first" }], sequenceFlows: [] } };
    const graph = buildExecutableActivityGraph(tree, catalog);
    const canvas = buildExecutableInspectorCanvas(graph, catalog, [], [], () => {}, "first");
    expect(canvas.mode).toBe("graph");
    expect(canvas.nodes).toEqual([expect.objectContaining({ id: "task", type: "bpmnElement", selected: true,
      draggable: false, connectable: false, deletable: false, data: expect.objectContaining({ runtimeNodeId: "first" }) })]);
  });
});
