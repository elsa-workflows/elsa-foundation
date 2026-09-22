import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDescriptor, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  InspectorPanel,
  resolveActivityInspectorTabId,
  type ActivityInspectorTabId
} from "../workflow-editor/InspectorPanel";
import type { ChildSlot } from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { setVariableActivity } from "./fixtures";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function keyDown(element: Element, key: string) {
  flushSync(() => element.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
}

const node: ActivityNode = {
  nodeId: "send-http-1",
  activityVersionId: "send-http-v1",
  inputs: [],
  outputs: []
};

const descriptor: StudioActivityDescriptor = {
  typeName: "Elsa.Activities.Http.Activities.SendHttpRequest",
  inputs: [],
  outputs: [],
  ports: []
};

const setVariableDescriptor: StudioActivityDescriptor = {
  typeName: setVariableActivity.activityTypeKey,
  inputs: setVariableActivity.inputs as StudioActivityDescriptor["inputs"],
  outputs: [],
  ports: []
};

const setVariableNode: ActivityNode = {
  nodeId: "set-variable-1",
  activityVersionId: setVariableActivity.activityVersionId,
  inputs: [],
  outputs: [],
  intrinsic: {
    kind: "Set",
    variable: { referenceKey: "counter", declaringScopeId: "workflow" },
    valueType: { alias: "Elsa.Any", collectionKind: "Single" }
  }
};

const bodySlot: ChildSlot = {
  id: "send-http.structure:body",
  label: "Body",
  property: "body",
  cardinality: "single",
  mode: "generic",
  activities: []
};

function Harness({
  initialTab = "inputs",
  supportsVariables = false,
  slots = [],
  activityDescriptor = descriptor,
  selectedNode = node,
  catalog = [],
  reusableDefinitionId = null
}: {
  initialTab?: ActivityInspectorTabId;
  supportsVariables?: boolean;
  slots?: ChildSlot[];
  activityDescriptor?: StudioActivityDescriptor | null;
  selectedNode?: ActivityNode;
  catalog?: ActivityCatalogItem[];
  reusableDefinitionId?: string | null;
}) {
  const [activeTabId, setActiveTabId] = React.useState<ActivityInspectorTabId>(initialTab);
  return (
    <InspectorPanel
      context={{} as StudioEndpointContext}
      selectedNode={selectedNode}
      selectedNodeLabel="Send HTTP Request"
      selectedActivityType={activityDescriptor?.typeName ?? descriptor.typeName}
      selectedDescriptor={activityDescriptor}
      selectedNodeAvailability={null}
      selectedReusableDefinitionId={reusableDefinitionId}
      selectedReusableSemanticVersion={reusableDefinitionId ? "1.0.0" : null}
      selectedSlots={slots}
      catalog={catalog}
      catalogByVersion={new Map(catalog.map(item => [item.activityVersionId, item]))}
      selectedSupportsScopedVariables={supportsVariables}
      propertyEditors={[]}
      expressionEditors={[]}
      expressionDescriptors={[]}
      expressionDescriptorStatus="ready"
      descriptorStatus="ready"
      onRetryExpressionDescriptors={() => undefined}
      scopedVariableAnalysis={{
        visibleVariables: [{ referenceKey: "counter", name: "Counter", scopeId: "workflow", isWorkflowScope: true }],
        shadowingWarnings: [],
        status: "ready"
      }}
      activeTabId={activeTabId}
      onActiveTabChange={setActiveTabId}
      onSelectedActivityChange={vi.fn()}
      onEnterSlot={vi.fn()}
      onReplaceSlotActivity={vi.fn()}
    />
  );
}

function tabs(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>("[role='tab']")];
}

describe("activity Inspector tabs", () => {
  it("starts on Inputs and orders the complete conditional tab set", () => {
    const container = render(<Harness supportsVariables slots={[bodySlot]} />);
    const allTabs = tabs(container);

    expect(allTabs.map(tab => tab.textContent)).toEqual([
      "Inputs",
      "Outputs",
      "Variables",
      "Slots",
      "Details",
      "Version"
    ]);
    expect(allTabs[0].getAttribute("aria-selected")).toBe("true");
    expect(allTabs.every(tab => tab.querySelector("svg") === null)).toBe(true);

    const inputsPanelId = allTabs[0].getAttribute("aria-controls");
    const inputsPanel = container.querySelector<HTMLElement>(`#${inputsPanelId}`);
    expect(inputsPanel?.getAttribute("aria-labelledby")).toBe(allTabs[0].id);
    expect(inputsPanel?.hidden).toBe(false);
    expect(container.querySelectorAll("[role='tabpanel']").length).toBe(6);
  });

  it("keeps activity context fixed while Details and Version own their metadata", () => {
    const container = render(<Harness />);

    expect(container.querySelector(".wf-inspector-context")?.textContent).toContain("Send HTTP Request");
    expect(container.querySelector(".wf-inspector-context")?.textContent).toContain("Node ID");
    expect(container.querySelector(".wf-inspector-context")?.textContent).toContain("send-http-1");
    expect(container.textContent).toContain("This activity has no configurable inputs.");

    click(tabs(container).find(tab => tab.textContent === "Details")!);
    const details = container.querySelector<HTMLElement>("[role='tabpanel']:not([hidden])")!;
    expect(details.textContent).not.toContain("Node ID");
    expect(details.textContent).toContain(descriptor.typeName);

    click(tabs(container).find(tab => tab.textContent === "Version")!);
    const version = container.querySelector<HTMLElement>("[role='tabpanel']:not([hidden])")!;
    expect(version.textContent).toContain("Activity version");
    expect(version.textContent).toContain("send-http-v1");
  });

  it("supports arrow, Home, and End navigation with focus following selection", () => {
    const container = render(<Harness />);
    const allTabs = tabs(container);
    allTabs[0].focus();

    keyDown(allTabs[0], "ArrowRight");
    expect(tabs(container)[1].getAttribute("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(tabs(container)[1]);

    keyDown(tabs(container)[1], "End");
    expect(tabs(container).at(-1)?.getAttribute("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(tabs(container).at(-1));

    keyDown(tabs(container).at(-1)!, "Home");
    expect(tabs(container)[0].getAttribute("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(tabs(container)[0]);
  });

  it("shows explicit empty states in the always-present Inputs and Outputs tabs", () => {
    const container = render(<Harness />);
    expect(container.textContent).toContain("This activity has no configurable inputs.");

    click(tabs(container).find(tab => tab.textContent === "Outputs")!);
    expect(container.querySelector("[role='tabpanel']:not([hidden])")?.textContent)
      .toContain("This activity has no outputs.");
  });

  it("keeps intrinsic destination authoring in Inputs and out of Variables", () => {
    const container = render(
      <Harness
        selectedNode={setVariableNode}
        activityDescriptor={setVariableDescriptor}
        catalog={[setVariableActivity]}
        supportsVariables
      />
    );

    const inputsPanel = container.querySelector<HTMLElement>("[role='tabpanel']:not([hidden])")!;
    expect(inputsPanel.querySelector(".wf-intrinsic-variable")?.textContent).toContain("Variable");

    click(tabs(container).find(tab => tab.textContent === "Variables")!);
    const variablesPanel = container.querySelector<HTMLElement>("[role='tabpanel']:not([hidden])")!;
    expect(variablesPanel.textContent).toContain("Container variables");
    expect(variablesPanel.querySelector(".wf-intrinsic-variable")).toBeNull();
  });

  it("keeps reusable identity and source actions in Version", () => {
    const container = render(<Harness reusableDefinitionId="send-http-definition" />);

    click(tabs(container).find(tab => tab.textContent === "Version")!);
    const versionPanel = container.querySelector<HTMLElement>("[role='tabpanel']:not([hidden])")!;
    expect(versionPanel.textContent).toContain("Reusable boundary");
    expect(versionPanel.textContent).toContain("send-http-definition");
    expect(versionPanel.querySelector("a")?.textContent).toContain("Open exact source definition");
  });

  it("falls back to Inputs only when a conditional active tab disappears", () => {
    expect(resolveActivityInspectorTabId("variables", true, false)).toBe("variables");
    expect(resolveActivityInspectorTabId("variables", false, false)).toBe("inputs");
    expect(resolveActivityInspectorTabId("slots", false, true)).toBe("slots");
    expect(resolveActivityInspectorTabId("slots", false, false)).toBe("inputs");
    expect(resolveActivityInspectorTabId("version", false, false)).toBe("version");
  });

  it("preserves a valid tab through outer-panel and activity changes, but keeps Inputs after fallback", () => {
    const container = render(<LifecycleHarness />);

    click(tabs(container).find(tab => tab.textContent === "Details")!);
    click(container.querySelector("[data-action='runtime']")!);
    expect(tabs(container)).toHaveLength(0);
    click(container.querySelector("[data-action='inspector']")!);
    expect(tabs(container).find(tab => tab.textContent === "Details")?.getAttribute("aria-selected")).toBe("true");

    click(tabs(container).find(tab => tab.textContent === "Variables")!);
    click(container.querySelector("[data-action='ordinary']")!);
    expect(tabs(container).find(tab => tab.textContent === "Inputs")?.getAttribute("aria-selected")).toBe("true");

    click(container.querySelector("[data-action='variable-owner']")!);
    expect(tabs(container).find(tab => tab.textContent === "Inputs")?.getAttribute("aria-selected")).toBe("true");
  });

  it("retains each mounted panel's scroll position and resets it with per-activity state", () => {
    const container = render(<LifecycleHarness />);
    const inputsTab = tabs(container).find(tab => tab.textContent === "Inputs")!;
    const inputsPanel = container.querySelector<HTMLElement>(`#${inputsTab.getAttribute("aria-controls")}`)!;
    inputsPanel.scrollTop = 137;

    click(tabs(container).find(tab => tab.textContent === "Outputs")!);
    click(tabs(container).find(tab => tab.textContent === "Inputs")!);
    expect(container.querySelector<HTMLElement>(`#${inputsTab.getAttribute("aria-controls")}`)?.scrollTop).toBe(137);

    click(container.querySelector("[data-action='second-variable-owner']")!);
    const nextInputsTab = tabs(container).find(tab => tab.textContent === "Inputs")!;
    expect(container.querySelector<HTMLElement>(`#${nextInputsTab.getAttribute("aria-controls")}`)?.scrollTop).toBe(0);
  });

  it("resets an open slot picker when the inspected activity changes", () => {
    const container = render(<SlotSelectionHarness />);

    click(container.querySelector("[aria-label='Choose Body activity']")!);
    expect(container.querySelector(".wf-connect-menu")).toBeTruthy();

    click(container.querySelector("[data-action='change-slot-owner']")!);
    expect(container.querySelector(".wf-connect-menu")).toBeNull();
  });
});

function LifecycleHarness() {
  const [activeTabId, setActiveTabId] = React.useState<ActivityInspectorTabId>("inputs");
  const [showInspector, setShowInspector] = React.useState(true);
  const [selection, setSelection] = React.useState<"variable-a" | "ordinary" | "variable-b">("variable-a");
  const supportsVariables = selection !== "ordinary";
  const selectedNode = selection === "variable-b"
    ? { ...node, nodeId: "send-http-2" }
    : node;

  return (
    <>
      <button type="button" data-action="runtime" onClick={() => setShowInspector(false)}>Runtime</button>
      <button type="button" data-action="inspector" onClick={() => setShowInspector(true)}>Inspector</button>
      <button type="button" data-action="ordinary" onClick={() => setSelection("ordinary")}>Ordinary activity</button>
      <button type="button" data-action="variable-owner" onClick={() => setSelection("variable-a")}>Variable owner</button>
      <button type="button" data-action="second-variable-owner" onClick={() => setSelection("variable-b")}>Second variable owner</button>
      {showInspector ? (
        <InspectorPanel
          key={selectedNode.nodeId}
          context={{} as StudioEndpointContext}
          selectedNode={selectedNode}
          selectedNodeLabel={selection === "variable-b" ? "Second request" : "Send HTTP Request"}
          selectedActivityType={descriptor.typeName}
          selectedDescriptor={descriptor}
          selectedNodeAvailability={null}
          selectedSlots={[]}
          catalog={[]}
          catalogByVersion={new Map()}
          selectedSupportsScopedVariables={supportsVariables}
          propertyEditors={[]}
          expressionEditors={[]}
          expressionDescriptors={[]}
          expressionDescriptorStatus="ready"
          descriptorStatus="ready"
          onRetryExpressionDescriptors={() => undefined}
          scopedVariableAnalysis={{ visibleVariables: [], shadowingWarnings: [], status: "ready" }}
          activeTabId={activeTabId}
          onActiveTabChange={setActiveTabId}
          onSelectedActivityChange={vi.fn()}
          onEnterSlot={vi.fn()}
          onReplaceSlotActivity={vi.fn()}
        />
      ) : <div data-testid="runtime-panel">Runtime content</div>}
    </>
  );
}

function SlotSelectionHarness() {
  const [selectedNode, setSelectedNode] = React.useState(node);
  return (
    <>
      <button
        type="button"
        data-action="change-slot-owner"
        onClick={() => setSelectedNode(current => ({ ...current, nodeId: "send-http-2" }))}
      >
        Change activity
      </button>
      <InspectorPanel
        key={selectedNode.nodeId}
        context={{} as StudioEndpointContext}
        selectedNode={selectedNode}
        selectedNodeLabel="Send HTTP Request"
        selectedActivityType={descriptor.typeName}
        selectedDescriptor={descriptor}
        selectedNodeAvailability={null}
        selectedSlots={[bodySlot]}
        catalog={[]}
        catalogByVersion={new Map()}
        selectedSupportsScopedVariables={false}
        propertyEditors={[]}
        expressionEditors={[]}
        expressionDescriptors={[]}
        expressionDescriptorStatus="ready"
        descriptorStatus="ready"
        onRetryExpressionDescriptors={() => undefined}
        scopedVariableAnalysis={{ visibleVariables: [], shadowingWarnings: [], status: "ready" }}
        activeTabId="slots"
        onActiveTabChange={() => undefined}
        onSelectedActivityChange={vi.fn()}
        onEnterSlot={vi.fn()}
        onReplaceSlotActivity={vi.fn()}
      />
    </>
  );
}
