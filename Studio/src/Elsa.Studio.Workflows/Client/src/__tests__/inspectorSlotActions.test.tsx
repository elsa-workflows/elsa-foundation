import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { InspectorPanel } from "../workflow-editor/InspectorPanel";
import type { ChildSlot } from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { writeLine } from "./fixtures";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function rerender(ui: React.ReactElement) {
  flushSync(() => active!.root.render(ui));
}

function click(el: Element) {
  flushSync(() => el.dispatchEvent(new MouseEvent("click", { bubbles: true, clientX: 40, clientY: 60 })));
}

const readLine: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-read-line-v1",
  activityTypeKey: "Elsa.Activities.Primitives.Activities.ReadLine",
  displayName: "Read Line"
};

const catalog = [writeLine, readLine];
const catalogByVersion = new Map(catalog.map(activity => [activity.activityVersionId, activity]));

const writeLineChild: ActivityNode = {
  nodeId: "writeline-1",
  activityVersionId: writeLine.activityVersionId,
  inputs: [],
  outputs: []
};

const forEachNode: ActivityNode = {
  nodeId: "foreach-1",
  activityVersionId: "activity-foreach-v1",
  inputs: [],
  outputs: []
};

function bodySlot(activities: ActivityNode[]): ChildSlot {
  return {
    id: "elsa.foreach.structure:body",
    label: "Body",
    property: "body",
    cardinality: "single",
    mode: "generic",
    activities
  };
}

type PanelHandlers = {
  onEnterSlot?: ReturnType<typeof vi.fn>;
  onReplaceSlotActivity?: ReturnType<typeof vi.fn>;
};

function panelElement(slots: ChildSlot[], handlers: PanelHandlers = {}, overrides: Partial<React.ComponentProps<typeof InspectorPanel>> = {}) {
  return (
    <InspectorPanel
      context={{} as StudioEndpointContext}
      selectedNode={forEachNode}
      selectedNodeLabel="For Each"
      selectedActivityType="Elsa.Activities.ForEach.Activities.ForEach"
      selectedDescriptor={null}
      selectedNodeAvailability={null}
      selectedSlots={slots}
      catalog={catalog}
      catalogByVersion={catalogByVersion}
      selectedSupportsScopedVariables={false}
      propertyEditors={[]}
      expressionEditors={[]}
      expressionDescriptors={[]}
      expressionDescriptorStatus="ready"
      descriptorStatus="ready"
      onRetryExpressionDescriptors={() => undefined}
      scopedVariableAnalysis={{ visibleVariables: [], shadowingWarnings: [], status: "unavailable" }}
      onSelectedActivityChange={vi.fn()}
      onEnterSlot={handlers.onEnterSlot ?? vi.fn()}
      onReplaceSlotActivity={handlers.onReplaceSlotActivity ?? vi.fn()}
      {...overrides}
    />
  );
}

function renderPanel(slot: ChildSlot, handlers: PanelHandlers = {}) {
  return render(panelElement([slot], handlers));
}

function findButton(container: HTMLElement, text: string) {
  return Array.from(container.querySelectorAll("button")).find(button => button.textContent?.includes(text));
}

describe("InspectorPanel slot actions", () => {
  it("enters the slot (with the full slot descriptor) when the slot row is clicked", () => {
    const onEnterSlot = vi.fn();
    const slot = bodySlot([writeLineChild]);
    const container = renderPanel(slot, { onEnterSlot });

    click(findButton(container, "Body")!);

    expect(onEnterSlot).toHaveBeenCalledTimes(1);
    expect(onEnterSlot).toHaveBeenCalledWith(forEachNode.nodeId, slot, "For Each / Body");
  });

  it("shows the assigned activity name on a filled single slot row", () => {
    const container = renderPanel(bodySlot([writeLineChild]));
    expect(container.querySelector(".wf-slot-row")?.textContent).toContain("Write Line");
  });

  it("offers a change action on a filled single slot that replaces via the activity picker", () => {
    const onReplaceSlotActivity = vi.fn();
    const slot = bodySlot([writeLineChild]);
    const container = renderPanel(slot, { onReplaceSlotActivity });

    click(container.querySelector('[aria-label="Change Body activity"]')!);
    // The picker menu opens with the browsable catalog.
    const readLineOption = findButton(container.ownerDocument.body as HTMLElement, "Read Line");
    expect(readLineOption).toBeTruthy();
    click(readLineOption!);

    expect(onReplaceSlotActivity).toHaveBeenCalledTimes(1);
    expect(onReplaceSlotActivity).toHaveBeenCalledWith(forEachNode.nodeId, slot, "For Each / Body", readLine);
  });

  it("labels the picker action 'Choose' when the single slot is still empty", () => {
    const container = renderPanel(bodySlot([]));
    expect(container.querySelector('[aria-label="Choose Body activity"]')).toBeTruthy();
  });

  it("does not offer the picker action on many-cardinality slots", () => {
    const container = renderPanel({ ...bodySlot([]), cardinality: "many" });
    expect(container.querySelector(".wf-slot-change")).toBeNull();
  });

  it("closes an open picker when the selection moves to another node", () => {
    const slot = bodySlot([writeLineChild]);
    const container = renderPanel(slot);

    click(container.querySelector('[aria-label="Change Body activity"]')!);
    expect(container.querySelector(".wf-connect-menu")).toBeTruthy();

    const otherNode: ActivityNode = { ...forEachNode, nodeId: "foreach-2" };
    rerender(panelElement([slot], {}, { selectedNode: otherNode }));
    expect(container.querySelector(".wf-connect-menu")).toBeNull();
  });

  it("discards a pick whose slot no longer exists on the selected node", () => {
    const onReplaceSlotActivity = vi.fn();
    const slot = bodySlot([writeLineChild]);
    const container = renderPanel(slot, { onReplaceSlotActivity });

    click(container.querySelector('[aria-label="Change Body activity"]')!);
    // The slot disappears (e.g. a concurrent edit removed it) while the picker stays open.
    rerender(panelElement([], { onReplaceSlotActivity }));
    expect(container.querySelector(".wf-connect-menu")).toBeTruthy();

    click(findButton(container.ownerDocument.body as HTMLElement, "Read Line")!);

    expect(onReplaceSlotActivity).not.toHaveBeenCalled();
    expect(container.querySelector(".wf-connect-menu")).toBeNull();
  });

  it("replaces via the live slot descriptor, not the one captured when the picker opened", () => {
    const onReplaceSlotActivity = vi.fn();
    const staleSlot = bodySlot([writeLineChild]);
    const container = renderPanel(staleSlot, { onReplaceSlotActivity });

    click(container.querySelector('[aria-label="Change Body activity"]')!);
    // The slot's contents change while the picker is open; the pick must use the fresh descriptor.
    const freshSlot = bodySlot([{ ...writeLineChild, nodeId: "writeline-2" }]);
    rerender(panelElement([freshSlot], { onReplaceSlotActivity }));

    click(findButton(container.ownerDocument.body as HTMLElement, "Read Line")!);

    expect(onReplaceSlotActivity).toHaveBeenCalledTimes(1);
    expect(onReplaceSlotActivity.mock.calls[0][1]).toBe(freshSlot);
  });
});
