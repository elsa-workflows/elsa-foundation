import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDefinitionImplementationEditorProps } from "@elsa-workflows/studio-sdk";
import { ActivityGraphImplementationEditor } from "../ActivityGraphImplementationEditor";
import { forEachActivity, forEachNode, leafNode, sequenceActivity, sequenceNode, writeLine } from "./fixtures";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

vi.mock("../api/activityDesign", () => ({
  useWorkflowActivities: () => ({
    data: { activities: catalogItems },
    isPending: false,
    isError: false
  })
}));
vi.mock("../api/expressions", () => ({
  listExpressionDescriptors: async () => [],
  listConversionProfiles: async () => [],
  listVariableTypeDescriptors: async () => []
}));

const branchKind = "tests.sequence-branch";
const singleSequenceKind = "tests.single-sequence-body";
const originalConfirm = window.confirm;
let catalogItems: ActivityCatalogItem[] = [sequenceActivity, forEachActivity, writeLine, branchCatalogItem()];
const mounted: Array<{ root: Root; container: HTMLDivElement }> = [];

afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.container.remove();
  }
  window.confirm = originalConfirm;
  catalogItems = [sequenceActivity, forEachActivity, writeLine, branchCatalogItem()];
});

describe("Activity Graph Sequence host", () => {
  it("keeps authored list order authoritative and makes reorder undoable", () => {
    const first = leafNode("first");
    first.activityVersionId = "activity-write-line-v1";
    const second = { ...leafNode("second"), activityVersionId: "activity-write-line-v1" };
    catalogItems = [sequenceActivity, writeLine];
    const value = implementationValue(sequenceNode("root", [first, second]), [
      { nodeId: "first", data: { x: 900, y: 20 } },
      { nodeId: "second", data: { x: 20, y: 20 } }
    ]);
    const rendered = renderControlledDesigner(value);

    expect(stepIds(rendered.container)).toEqual(["first", "second"]);
    click(step(rendered.container, "second"));
    expect(rendered.container.querySelector(".wf-inspector-context h3")?.textContent).toBe("Write Line");
    click(button(rendered.container, "Move Write Line up", "aria-label"));

    expect(stepIds(rendered.container)).toEqual(["second", "first"]);
    expect(sequenceActivities(rendered.value()).map(activity => activity.nodeId)).toEqual(["second", "first"]);
    expect(rendered.value().layout).toEqual(value.layout);
    click(button(rendered.container, "Undo Activity Graph edit", "aria-label"));
    expect(sequenceActivities(rendered.value()).map(activity => activity.nodeId)).toEqual(["first", "second"]);
  });

  it("inserts from the Sequence boundary picker without persisting generated positions", () => {
    catalogItems = [sequenceActivity, writeLine];
    const value = implementationValue(sequenceNode("root", [leafNode("first"), leafNode("second")]), [
      { nodeId: "first", data: { x: 180, y: 40 } },
      { nodeId: "second", data: { x: 20, y: 160 } }
    ]);
    const rendered = renderControlledDesigner(value);

    click(rendered.container.querySelector<HTMLElement>(".wf-sequence-insert[data-sequence-insert-index='1']")!);
    const option = [...rendered.container.querySelectorAll<HTMLButtonElement>(".wf-connect-menu [role='option']")]
      .find(candidate => candidate.querySelector("strong")?.textContent === "Write Line");
    expect(option).toBeTruthy();
    click(option!);

    const activities = sequenceActivities(rendered.value());
    expect(activities[0].nodeId).toBe("first");
    expect(activities[1].activityVersionId).toBe(writeLine.activityVersionId);
    expect(activities[2].nodeId).toBe("second");
    expect(rendered.value().layout).toEqual(value.layout);
  });

  it("navigates through a single Body container to its actual nested Sequence slot", async () => {
    catalogItems = [sequenceActivity, forEachActivity, writeLine];
    const inner = sequenceNode("inner-sequence", [{ ...leafNode("body-first"), activityVersionId: writeLine.activityVersionId }]);
    const root = sequenceNode("root", [forEachNode("foreach", inner)]);
    const rendered = renderControlledDesigner(implementationValue(root));

    click(button(rendered.container, "Open Body, 1 activity", "aria-label"));

    expect(rendered.container.querySelector(".wf-breadcrumb")?.textContent).toContain("For Each / Body");
    await vi.waitFor(() => expect(stepIds(rendered.container)).toEqual(["body-first"]));
    click(button(rendered.container, "Root"));
    await vi.waitFor(() => expect(stepIds(rendered.container)).toEqual(["foreach"]));
  });

  it("opens and edits a secondary Sequence slot without changing the primary slot", async () => {
    catalogItems = [sequenceActivity, branchCatalogItem(), writeLine];
    const body = [{ ...leafNode("body-step"), activityVersionId: writeLine.activityVersionId }];
    const secondary = [
      { ...leafNode("secondary-first"), activityVersionId: writeLine.activityVersionId },
      { ...leafNode("secondary-second"), activityVersionId: writeLine.activityVersionId }
    ];
    const root = sequenceNode("root", [branchNode("branch", body, secondary)]);
    const rendered = renderControlledDesigner(implementationValue(root));

    click(button(rendered.container, "Open Secondary, 2 steps", "aria-label"));
    expect(rendered.container.querySelector(".wf-breadcrumb")?.textContent).toContain("Branch / Secondary");
    await vi.waitFor(() => expect(stepIds(rendered.container)).toEqual(["secondary-first", "secondary-second"]));
    click(step(rendered.container, "secondary-second"));
    click(button(rendered.container, "Move Write Line up", "aria-label"));

    const branch = sequenceActivities(rendered.value())[0];
    const branchPayload = branch.structure?.payload as Record<string, unknown>;
    expect(branchPayload.body).toEqual(body);
    expect((branchPayload.secondary as ActivityNode[]).map(activity => activity.nodeId))
      .toEqual(["secondary-second", "secondary-first"]);
  });

  it("keeps a single-cardinality Sequence slot on the existing projected editor", () => {
    const singleSequence = singleSequenceNode("single-sequence", { ...leafNode("single-child"), activityVersionId: writeLine.activityVersionId });
    catalogItems = [sequenceActivity, singleSequenceCatalogItem(), writeLine];
    const rendered = renderControlledDesigner(implementationValue(sequenceNode("root", [singleSequence])));

    click(button(rendered.container, "Open Body, 1 step", "aria-label"));

    expect(rendered.container.querySelector(".wf-sequence-designer")).toBeNull();
    expect(rendered.container.querySelector(".wf-sequence-insert")).toBeNull();
    expect(rendered.container.querySelector(".wf-canvas")).not.toBeNull();
    expect(stepIds(rendered.container)).not.toContain("single-child");
  });

  it("duplicates after the source with remapped nested ids and layout, then deletes and undoes", async () => {
    catalogItems = [sequenceActivity, branchCatalogItem(), writeLine];
    const authoredInputs = [
      { referenceKey: "message", value: { expressionType: "Literal", value: "preserve me" } },
      { referenceKey: "workflow-variable", value: { expressionType: "Variable", value: { referenceKey: "customer-id", declaringScopeId: "workflow" } } }
    ];
    const nested = { ...leafNode("nested-original"), activityVersionId: writeLine.activityVersionId, inputs: authoredInputs };
    const source = branchNode("branch-source", [nested], []);
    const tail = { ...leafNode("tail"), activityVersionId: writeLine.activityVersionId };
    const value = implementationValue(sequenceNode("root", [source, tail]), [
      { nodeId: "branch-source", data: { x: 240, y: 10 } },
      { nodeId: "nested-original", data: { x: 80, y: 90, width: 240 } },
      { nodeId: "tail", data: { x: 20, y: 220 } }
    ]);
    const rendered = renderControlledDesigner(value);

    click(step(rendered.container, "branch-source"));
    click(button(rendered.container, "Duplicate Branch", "aria-label"));

    const duplicated = sequenceActivities(rendered.value());
    expect(duplicated).toHaveLength(3);
    const clone = duplicated[1];
    const clonedNested = ((clone.structure?.payload as Record<string, unknown>).body as ActivityNode[])[0];
    expect(duplicated.map(activity => activity.nodeId)[0]).toBe("branch-source");
    expect(duplicated.map(activity => activity.nodeId)[2]).toBe("tail");
    expect(clone.nodeId).not.toBe("branch-source");
    expect(clonedNested.nodeId).not.toBe("nested-original");
    expect(clonedNested.inputs).toEqual(authoredInputs);
    expect(rendered.value().layout).toContainEqual({
      nodeId: clonedNested.nodeId,
      data: { x: 80, y: 90, width: 240 }
    });
    expect(rendered.value().layout.map(record => record.nodeId)).not.toContain(clone.nodeId);

    const cloneStep = step(rendered.container, clone.nodeId);
    await vi.waitFor(() => expect(document.activeElement).toBe(cloneStep));
    click(cloneStep);
    confirmOnce();
    click(button(rendered.container, "Delete Branch", "aria-label"));
    expect(sequenceActivities(rendered.value()).map(activity => activity.nodeId)).toEqual(["branch-source", "tail"]);
    expect(rendered.value().layout.map(record => record.nodeId)).not.toContain(clonedNested.nodeId);

    click(button(rendered.container, "Undo Activity Graph edit", "aria-label"));
    expect(sequenceActivities(rendered.value()).map(activity => activity.nodeId)).toContain(clone.nodeId);
    expect(rendered.value().layout.map(record => record.nodeId)).toContain(clonedNested.nodeId);
  });

  it("returns focus to the Sequence canvas after deleting its only step", async () => {
    const rendered = renderControlledDesigner(implementationValue(sequenceNode("root", [leafNode("only-step")])));
    click(step(rendered.container, "only-step"));
    confirmOnce();

    click(button(rendered.container, "Delete Write Line", "aria-label"));

    expect(sequenceActivities(rendered.value())).toEqual([]);
    const canvas = rendered.container.querySelector<HTMLElement>(".wf-sequence-designer");
    expect(canvas).not.toBeNull();
    await vi.waitFor(() => expect(document.activeElement).toBe(canvas));
  });

  it("keeps read-only sequence steps immutable while allowing nested slot navigation", async () => {
    catalogItems = [sequenceActivity, forEachActivity, writeLine];
    const inner = sequenceNode("inner-sequence", [{ ...leafNode("body-step"), activityVersionId: writeLine.activityVersionId }]);
    const rendered = renderDesigner({ value: implementationValue(sequenceNode("root", [forEachNode("foreach", inner)])), readOnly: true });

    expect(button(rendered.container, "Auto-layout Activity Graph", "aria-label").disabled).toBe(true);
    expect(rendered.container.querySelector(".wf-sequence-insert")).toBeNull();
    expect(rendered.container.querySelector(".wf-sequence-actions")).toBeNull();
    click(button(rendered.container, "Open Body, 1 activity", "aria-label"));
    await vi.waitFor(() => expect(stepIds(rendered.container)).toEqual(["body-step"]));
    expect(rendered.container.querySelector(".wf-sequence-insert")).toBeNull();
  });
});

function branchCatalogItem(): ActivityCatalogItem {
  return {
    ...writeLine,
    activityVersionId: "activity-branch-v1",
    activityTypeKey: "Tests.Branch",
    category: "Control Flow",
    displayName: "Branch",
    designFacets: [{
      kind: branchKind,
      schemaVersion: "1.0.0",
      payload: {
        mode: "sequence",
        supportsScopedVariables: true,
        slots: [
          { name: "Branch.Body", property: "body", displayName: "Body", cardinality: "many" },
          { name: "Branch.Secondary", property: "secondary", displayName: "Secondary", cardinality: "many" }
        ],
        initialPayload: { body: [], secondary: [] }
      }
    }]
  };
}

function branchNode(nodeId: string, body: ActivityNode[], secondary: ActivityNode[]): ActivityNode {
  return {
    nodeId,
    activityVersionId: "activity-branch-v1",
    inputs: [],
    outputs: [],
    structure: {
      kind: branchKind,
      schemaVersion: "1.0.0",
      payload: { body, secondary }
    }
  };
}

function singleSequenceCatalogItem(): ActivityCatalogItem {
  return {
    ...writeLine,
    activityVersionId: "activity-single-sequence-v1",
    activityTypeKey: "Tests.SingleSequenceBody",
    category: "Control Flow",
    displayName: "Single Sequence Body",
    designFacets: [{
      kind: singleSequenceKind,
      schemaVersion: "1.0.0",
      payload: {
        mode: "sequence",
        supportsScopedVariables: true,
        slots: [{ name: "SingleSequence.Body", property: "body", displayName: "Body", cardinality: "single" }],
        initialPayload: { body: null }
      }
    }]
  };
}

function singleSequenceNode(nodeId: string, body: ActivityNode): ActivityNode {
  return {
    nodeId,
    activityVersionId: "activity-single-sequence-v1",
    inputs: [],
    outputs: [],
    structure: {
      kind: singleSequenceKind,
      schemaVersion: "1.0.0",
      payload: { body }
    }
  };
}

function implementationValue(rootActivity: ActivityNode, layout: Array<{ nodeId: string; data: Record<string, unknown> }> = []) {
  return {
    payload: { rootActivity, variables: [], outputMappings: [], outcomeMappings: [] },
    layout
  } as StudioActivityDefinitionImplementationEditorProps["value"];
}

function renderDesigner(overrides: Partial<StudioActivityDefinitionImplementationEditorProps> = {}) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  flushSync(() => root.render(<ActivityGraphImplementationEditor {...designerProps(overrides)} />));
  const rendered = { root, container };
  mounted.push(rendered);
  return rendered;
}

function renderControlledDesigner(initialValue: StudioActivityDefinitionImplementationEditorProps["value"]) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  let currentValue = initialValue;
  const props = designerProps({ value: initialValue });
  function Controlled() {
    const [value, setValue] = React.useState(initialValue);
    const onChange = React.useCallback((next: typeof initialValue) => {
      currentValue = next;
      setValue(next);
    }, []);
    return <ActivityGraphImplementationEditor {...props} value={value} onChange={onChange} />;
  }
  flushSync(() => root.render(<Controlled />));
  const rendered = { root, container, value: () => currentValue };
  mounted.push(rendered);
  return rendered;
}

function designerProps(overrides: Partial<StudioActivityDefinitionImplementationEditorProps> = {}): StudioActivityDefinitionImplementationEditorProps {
  return {
    context: {
      baseUrl: "test://activity-graph",
      http: { getJson: vi.fn(async () => ({ items: [] })) }
    } as unknown as StudioActivityDefinitionImplementationEditorProps["context"],
    definitionId: "definition-1",
    draftId: "draft-1",
    revision: 1,
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: "2",
    manifestFingerprint: "sha256:test",
    contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] },
    propertyEditors: [],
    expressionEditors: [],
    graphAuthoringPanels: [],
    historyResetKey: "draft-1:active",
    value: implementationValue(sequenceNode("root", [])),
    readOnly: false,
    onChange: () => {},
    ...overrides
  };
}

function sequenceActivities(value: StudioActivityDefinitionImplementationEditorProps["value"]) {
  const payload = value.payload as { rootActivity: ActivityNode };
  return (payload.rootActivity.structure?.payload.activities ?? []) as ActivityNode[];
}

function stepIds(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLElement>(".wf-sequence-step")].map(element => element.dataset.id);
}

function step(container: HTMLElement, nodeId: string) {
  const element = [...container.querySelectorAll<HTMLElement>(".wf-sequence-step")]
    .find(candidate => candidate.dataset.id === nodeId);
  if (!element) throw new Error(`Sequence step not found: ${nodeId}`);
  return element;
}

function button(container: HTMLElement, label: string, attribute?: string) {
  const element = [...container.querySelectorAll<HTMLButtonElement>("button")]
    .find(candidate => attribute ? candidate.getAttribute(attribute) === label : candidate.textContent?.trim() === label);
  if (!element) throw new Error(`Button not found: ${label}`);
  return element;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function confirmOnce() {
  const original = window.confirm;
  window.confirm = () => {
    window.confirm = original;
    return true;
  };
}
