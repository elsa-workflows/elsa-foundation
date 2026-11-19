import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Position } from "@xyflow/react";
import { ActivityPalettePanel } from "../workflow-editor/ActivityPalettePanel";
import { ConnectMenu, WorkflowFlowEdge } from "../workflow-editor/graph";
import { WorkflowNodeContextToolbar } from "../workflow-editor/WorkflowNodeContextToolbar";
import { WorkflowEdgeActionsContext, WorkflowNodeActionsContext } from "../workflow-editor/contexts";
import type { ActivityCatalogItem } from "../workflowTypes";

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    BaseEdge: () => <svg aria-hidden="true" />,
    EdgeLabelRenderer: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    NodeToolbar: ({ children }: { children: React.ReactNode }) => <>{children}</>
  };
});

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("workflow designer keyboard accessibility", () => {
  it("shows behavioral hash prefixes when activity choices share a short version", () => {
    const first = {
      ...activity("send-email-a", "Send Email"),
      activityDefinitionId: "send-email",
      activityDefinitionVersion: "1.0.0+aaaabbbbcccc"
    };
    const second = {
      ...activity("send-email-b", "Send Email"),
      activityDefinitionId: "send-email",
      activityDefinitionVersion: "1.0.0+bbbbccccdddd"
    };

    flushSync(() => root.render(<PaletteHarness activities={[first, second]} onInsert={() => {}} />));

    const chips = [...container.querySelectorAll<HTMLElement>(".wf-palette-version")];
    expect(chips.map(chip => chip.textContent)).toEqual(["v1.0.0+aaaabbb", "v1.0.0+bbbbccc"]);
    expect(chips.map(chip => chip.title)).toEqual([
      "Exact version 1.0.0+aaaabbbbcccc",
      "Exact version 1.0.0+bbbbccccdddd"
    ]);
  });

  it("moves from palette search through the tree and inserts the focused activity", () => {
    const onInsert = vi.fn();
    const writeLine = activity("write-line", "Write Line");
    const sendEmail = activity("send-email", "Send Email");

    flushSync(() => root.render(<PaletteHarness activities={[writeLine, sendEmail]} onInsert={onInsert} />));

    const search = container.querySelector<HTMLInputElement>("input[aria-label='Search activity palette']")!;
    fill(search, "send");
    expect(container.querySelector(".wf-palette-list")?.textContent).not.toContain("Write Line");
    search.focus();
    press("ArrowDown");
    expect(document.activeElement?.textContent).toContain("Primitives");

    press("ArrowDown");
    expect(document.activeElement?.textContent).toContain("Send Email");
    press("Enter");
    expect(onInsert).toHaveBeenCalledWith(sendEmail);

    fill(search, "");
    search.focus();
    press("ArrowDown");
    press("ArrowDown");
    expect(document.activeElement?.textContent).toContain("Write Line");
    press("ArrowDown");
    expect(document.activeElement?.textContent).toContain("Send Email");
    press("Home");
    expect(document.activeElement?.textContent).toContain("Primitives");
    press("End");
    expect(document.activeElement?.textContent).toContain("Send Email");
    expect([...container.querySelectorAll<HTMLElement>("[role='treeitem']")].filter(item => item.tabIndex === 0)).toHaveLength(1);
  });

  it("expands and collapses palette categories with directional keys", () => {
    flushSync(() => root.render(<PaletteHarness activities={[activity("write-line", "Write Line")]} onInsert={() => {}} />));
    const category = container.querySelector<HTMLButtonElement>(".wf-palette-category-toggle")!;
    category.focus();

    press("ArrowLeft");
    expect(category.getAttribute("aria-expanded")).toBe("false");
    expect(container.querySelector(".wf-palette-activity")).toBeNull();

    press("ArrowRight");
    expect(category.getAttribute("aria-expanded")).toBe("true");
    expect(container.querySelector(".wf-palette-activity")?.textContent).toContain("Write Line");
    press("ArrowRight");
    expect(document.activeElement?.textContent).toContain("Write Line");
    press("ArrowLeft");
    expect(document.activeElement).toBe(category);
  });

  it("restores focus to the invoking connection control when the activity picker is cancelled", async () => {
    const writeLine = activity("write-line", "Write Line");
    flushSync(() => root.render(<ConnectMenuHarness activity={writeLine} />));
    const trigger = container.querySelector<HTMLButtonElement>("button[data-connect-trigger]")!;
    trigger.focus();
    flushSync(() => trigger.click());
    await nextFrame();
    expect(document.activeElement?.getAttribute("aria-label")).toBe("Search activities");

    press("Escape");
    await nextFrame();
    expect(document.activeElement).toBe(trigger);
  });

  it("exposes keyboard-reachable insert and delete controls for a selected connection", () => {
    const deleteEdge = vi.fn();
    const requestInsertActivity = vi.fn();
    flushSync(() => root.render(
      <WorkflowEdgeActionsContext.Provider value={{ highlightedEdgeId: null, deleteEdge, requestInsertActivity }}>
        <WorkflowFlowEdge
          id="connection-1"
          source="write-line-1"
          target="send-email-1"
          sourceX={0}
          sourceY={0}
          targetX={120}
          targetY={0}
          sourcePosition={Position.Right}
          targetPosition={Position.Left}
          selected
        />
      </WorkflowEdgeActionsContext.Provider>
    ));

    const insert = container.querySelector<HTMLButtonElement>("button[aria-label='Insert activity into connection']")!;
    const remove = container.querySelector<HTMLButtonElement>("button[aria-label='Delete connection']")!;
    insert.focus();
    expect(document.activeElement).toBe(insert);
    flushSync(() => insert.dispatchEvent(new MouseEvent("click", { bubbles: true, detail: 0 })));
    flushSync(() => remove.click());

    expect(requestInsertActivity).toHaveBeenCalledWith("connection-1", 0, 0);
    expect(deleteEdge).toHaveBeenCalledWith("connection-1");
  });
});

describe("activity palette version chip", () => {
  it.each(["1.0.0", "1.0.0-rc.1"])("keeps the %s tooltip concise while retaining exact insertion identity", shortVersion => {
    const exactVersion = `${shortVersion}+892535311ec55c1b9930041e93cee112b2494283`;
    const onInsert = vi.fn();
    const writeLine = { ...activity("write-line", "Write Line"), activityDefinitionVersion: exactVersion };

    flushSync(() => root.render(<PaletteHarness activities={[writeLine]} onInsert={onInsert} />));

    expect(container.querySelector(".wf-palette-version")).toBeNull();
    const row = container.querySelector<HTMLElement>(".wf-palette-activity")!;
    expect(row.title).toBe(`Write Line activity — Version ${shortVersion}`);
    expect(row.title).not.toContain("+");
    expect(row.getAttribute("aria-label")).toContain(`Exact version ${exactVersion}`);
    flushSync(() => row.click());
    expect(onInsert).toHaveBeenCalledWith(writeLine);
    expect(writeLine.activityDefinitionVersion).toBe(exactVersion);
  });

  it("shows compact versions when multiple selectable versions would otherwise be ambiguous", () => {
    const exactVersion = "1.0.0+892535311ec55c1b9930041e93cee112b2494283";
    const writeLine = {
      ...activity("write-line-v1", "Write Line"),
      activityDefinitionId: "write-line",
      activityDefinitionVersion: exactVersion
    };
    const writeLineV2 = {
      ...activity("write-line-v2", "Write Line"),
      activityDefinitionId: "write-line",
      activityDefinitionVersion: "2.0.0"
    };

    flushSync(() => root.render(<PaletteHarness activities={[writeLine, writeLineV2]} onInsert={() => {}} />));

    const chip = container.querySelector<HTMLElement>(".wf-palette-version")!;
    expect(chip.textContent).toBe("v1.0.0");
    expect(chip.textContent).not.toContain("+");
    expect(chip.getAttribute("title")).toBe("Version 1.0.0");
    expect(chip.getAttribute("aria-label")).toBe(`Exact version ${exactVersion}`);
    // The activity name must remain visible alongside the compact chip.
    expect(container.querySelector(".wf-palette-activity-text strong")?.textContent).toBe("Write Line");
  });

  it("shows a per-activity palette icon coloured by category tone", () => {
    const writeLine = activity("write-line", "Write Line");
    const sendEmail = { ...activity("send-email", "Send Email"), category: "Email" };

    flushSync(() => root.render(<PaletteHarness activities={[writeLine, sendEmail]} onInsert={() => {}} />));

    const icons = [...container.querySelectorAll<HTMLElement>(".wf-palette-activity .wf-activity-icon")];
    expect(icons).toHaveLength(2);
    expect(icons[0].getAttribute("data-icon")).toBe("terminal");
    expect(icons[0].getAttribute("data-tone")).toBe("terminal");
    expect(icons[1].getAttribute("data-icon")).toBe("email");
    expect(icons[1].getAttribute("data-tone")).toBe("trigger");
    expect(icons[0].querySelector("svg")).toBeTruthy();
    expect(icons[1].querySelector("svg")).toBeTruthy();
  });
});

describe("selected workflow node actions", () => {
  it("toggles workflow starts and includes the toggle in keyboard navigation", () => {
    const actions = { selectedNodeCount: 1, canAddAfter: false, addAfter: vi.fn(), duplicate: vi.fn(), delete: vi.fn(), setWorkflowStart: vi.fn() };
    const data = { label: "Http Endpoint", activityVersionId: "http-v1", childSlots: [], acceptsInbound: true, sourcePorts: [], workflowStartEnabled: true };
    const render = (enabled: boolean) => flushSync(() => root.render(
      <WorkflowNodeActionsContext.Provider value={actions}>
        <WorkflowNodeContextToolbar nodeId="endpoint" data={{ ...data, workflowStartEnabled: enabled }} selected />
      </WorkflowNodeActionsContext.Provider>
    ));
    render(true);
    const toggle = container.querySelector<HTMLButtonElement>("button[aria-label='Start new workflows from Http Endpoint']")!;
    expect(toggle).toBeTruthy();
    expect(toggle.getAttribute("aria-pressed")).toBe("true");
    expect(toggle.title).toBe("Disable workflow start");
    toggle.focus();
    press("ArrowRight");
    expect(document.activeElement?.getAttribute("aria-label")).toBe("Duplicate Http Endpoint");
    press("Home");
    expect(document.activeElement).toBe(toggle);
    flushSync(() => toggle.click());
    expect(actions.setWorkflowStart).toHaveBeenCalledWith("endpoint", false);
    render(false);
    expect(toggle.getAttribute("aria-pressed")).toBe("false");
    flushSync(() => toggle.click());
    expect(actions.setWorkflowStart).toHaveBeenLastCalledWith("endpoint", true);
  });
  it("uses one tab stop and moves across toolbar actions with horizontal keys", () => {
    const actions = { selectedNodeCount: 1, canAddAfter: true, addAfter: vi.fn(), duplicate: vi.fn(), delete: vi.fn() };
    const data = {
      label: "Review order",
      activityVersionId: "review-order-v1",
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [{ name: "Approved", displayName: "Approved" }, { name: "Rejected", displayName: "Rejected" }]
    };

    flushSync(() => root.render(
      <WorkflowNodeActionsContext.Provider value={actions}>
        <WorkflowNodeContextToolbar nodeId="review-order" data={data} selected />
      </WorkflowNodeActionsContext.Provider>
    ));

    const buttons = [...container.querySelectorAll<HTMLButtonElement>("[role='toolbar'] button")];
    const [add, duplicate, remove] = buttons;
    expect(container.querySelector("[role='toolbar']")?.getAttribute("aria-orientation")).toBe("horizontal");
    expect(buttons.map(button => button.tabIndex)).toEqual([0, -1, -1]);

    add.focus();
    press("ArrowRight");
    expect(document.activeElement).toBe(duplicate);
    expect(buttons.map(button => button.tabIndex)).toEqual([-1, 0, -1]);

    press("ArrowRight");
    expect(document.activeElement).toBe(remove);
    press("ArrowRight");
    expect(document.activeElement).toBe(add);
    press("ArrowLeft");
    expect(document.activeElement).toBe(remove);
    press("Home");
    expect(document.activeElement).toBe(add);
    press("End");
    expect(document.activeElement).toBe(remove);
  });

  it("offers one Add button and routes a selected outcome through the existing action", () => {
    const actions = {
      selectedNodeCount: 1,
      canAddAfter: true,
      addAfter: vi.fn(),
      duplicate: vi.fn(),
      delete: vi.fn()
    };
    const data = {
      label: "Review order",
      activityVersionId: "review-order-v1",
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [{ name: "ApprovedBranch", displayName: "Approved" }, { name: "RejectedBranch", displayName: "Rejected" }]
    };

    flushSync(() => root.render(
      <WorkflowNodeActionsContext.Provider value={actions}>
        <WorkflowNodeContextToolbar nodeId="review-order" data={data} selected />
      </WorkflowNodeActionsContext.Provider>
    ));

    const toolbar = container.querySelector<HTMLElement>("[role='toolbar'][aria-label='Actions for Review order']")!;
    const add = container.querySelector<HTMLButtonElement>("button[aria-label='Add next activity']")!;
    const duplicate = container.querySelector<HTMLButtonElement>("button[aria-label='Duplicate Review order']")!;
    const remove = container.querySelector<HTMLButtonElement>("button[aria-label='Delete Review order']")!;

    expect(toolbar).toBeTruthy();
    expect(container.querySelectorAll("button[aria-label='Add next activity']")).toHaveLength(1);
    add.focus();
    expect(document.activeElement).toBe(add);
    flushSync(() => add.click());
    const approved = document.body.querySelector<HTMLButtonElement>("button[role='menuitem']")!;
    expect(approved.textContent).toBe("Approved");
    const rejected = [...document.body.querySelectorAll<HTMLButtonElement>("button[role='menuitem']")].find(item => item.textContent === "Rejected")!;
    flushSync(() => rejected.click());
    flushSync(() => duplicate.click());
    flushSync(() => remove.click());

    expect(actions.addAfter).toHaveBeenCalledOnce();
    expect(actions.addAfter).toHaveBeenCalledWith("review-order", "RejectedBranch", expect.any(Object));
    expect(actions.duplicate).toHaveBeenCalledWith("review-order");
    expect(actions.delete).toHaveBeenCalledWith("review-order");
  });

  it("adds directly through the actual port when there is one outcome", () => {
    const actions = { selectedNodeCount: 1, canAddAfter: true, addAfter: vi.fn(), duplicate: vi.fn(), delete: vi.fn() };
    const data = {
      label: "Write line",
      activityVersionId: "write-line-v1",
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [{ name: "Done", displayName: "Done" }]
    };

    flushSync(() => root.render(
      <WorkflowNodeActionsContext.Provider value={actions}>
        <WorkflowNodeContextToolbar nodeId="write-line" data={data} selected />
      </WorkflowNodeActionsContext.Provider>
    ));
    const add = container.querySelector<HTMLButtonElement>("button[aria-label='Add next activity']")!;
    vi.spyOn(add, "getBoundingClientRect").mockReturnValue(new DOMRect(10, 20, 30, 30));
    flushSync(() => add.dispatchEvent(new MouseEvent("click", { bubbles: true, detail: 0 })));

    expect(actions.addAfter).toHaveBeenCalledWith("write-line", "Done", { clientX: 25, clientY: 35 });
    expect(document.body.querySelector("[role='menu']")).toBeNull();
  });

  it("omits Add when the node has no outcomes", () => {
    const actions = { selectedNodeCount: 1, canAddAfter: true, addAfter: vi.fn(), duplicate: vi.fn(), delete: vi.fn() };
    const data = { label: "Empty", activityVersionId: "empty-v1", childSlots: [], acceptsInbound: true, sourcePorts: [] };

    flushSync(() => root.render(
      <WorkflowNodeActionsContext.Provider value={actions}>
        <WorkflowNodeContextToolbar nodeId="empty" data={data} selected />
      </WorkflowNodeActionsContext.Provider>
    ));

    expect(container.querySelector("button[aria-label='Add next activity']")).toBeNull();
    expect(container.querySelector("button[aria-label='Duplicate Empty']")).toBeTruthy();
  });

  it("returns focus to Add when Escape closes the outcome menu", async () => {
    const canvasKeyDown = vi.fn();
    const actions = { selectedNodeCount: 1, canAddAfter: true, addAfter: vi.fn(), duplicate: vi.fn(), delete: vi.fn() };
    const data = {
      label: "Review order",
      activityVersionId: "review-order-v1",
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [{ name: "ApprovedBranch", displayName: "Approved" }, { name: "RejectedBranch", displayName: "Rejected" }]
    };

    flushSync(() => root.render(
      <div onKeyDown={canvasKeyDown}><WorkflowNodeActionsContext.Provider value={actions}>
        <WorkflowNodeContextToolbar nodeId="review-order" data={data} selected />
      </WorkflowNodeActionsContext.Provider></div>
    ));
    const add = container.querySelector<HTMLButtonElement>("button[aria-label='Add next activity']")!;
    add.focus();
    flushSync(() => add.click());
    await nextFrame();
    expect(document.activeElement?.textContent).toBe("Approved");

    press("Escape");
    await nextFrame();
    expect(document.activeElement).toBe(add);
    expect(canvasKeyDown).not.toHaveBeenCalled();
  });

  it("hides node actions for multiselection and canvases without authoring actions", () => {
    const data = {
      label: "Review order",
      activityVersionId: "review-order-v1",
      childSlots: [],
      acceptsInbound: true,
      sourcePorts: [{ name: "Done", displayName: "Done" }]
    };
    const multiselectActions = { selectedNodeCount: 2, canAddAfter: true, addAfter: vi.fn(), duplicate: vi.fn(), delete: vi.fn() };

    flushSync(() => root.render(
      <WorkflowNodeActionsContext.Provider value={multiselectActions}>
        <WorkflowNodeContextToolbar nodeId="review-order" data={data} selected />
      </WorkflowNodeActionsContext.Provider>
    ));
    expect(container.querySelector("[role='toolbar']")).toBeNull();

    flushSync(() => root.render(<WorkflowNodeContextToolbar nodeId="review-order" data={data} selected />));
    expect(container.querySelector("[role='toolbar']")).toBeNull();
  });
});

function PaletteHarness({ activities, onInsert }: { activities: ActivityCatalogItem[]; onInsert(activity: ActivityCatalogItem): void }) {
  const [expanded, setExpanded] = useState(() => new Set(["Primitives"]));
  const [search, setSearch] = useState("");
  const filtered = activities.filter(activity => activity.displayName?.toLowerCase().includes(search.toLowerCase()));
  return (
    <ActivityPalettePanel
      paletteSearch={search}
      onSearchChange={setSearch}
      groups={filtered.length > 0 ? [{ category: "Primitives", activities: filtered }] : []}
      expandedCategories={expanded}
      onToggleCategory={category => setExpanded(current => {
        const next = new Set(current);
        if (next.has(category)) next.delete(category);
        else next.add(category);
        return next;
      })}
      onActivityClick={onInsert}
      onActivityDragStart={() => {}}
      onActivityDragEnd={() => {}}
      onActivityPointerDown={() => {}}
    />
  );
}

function activity(activityVersionId: string, displayName: string): ActivityCatalogItem {
  return {
    activityVersionId,
    activityTypeKey: `Acme.Activities.${displayName.replaceAll(" ", "")}`,
    version: "1.0.0",
    category: "Primitives",
    displayName,
    description: `${displayName} activity`,
    executionType: "Action",
    inputs: [],
    outputs: [],
    designFacets: []
  };
}

function ConnectMenuHarness({ activity }: { activity: ActivityCatalogItem }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" data-connect-trigger onClick={() => setOpen(true)}>Insert into connection</button>
      {open ? (
        <ConnectMenu
          clientX={20}
          clientY={20}
          activities={[activity]}
          onPick={() => setOpen(false)}
          onClose={() => setOpen(false)}
        />
      ) : null}
    </>
  );
}

async function nextFrame() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

function press(key: string) {
  flushSync(() => document.activeElement?.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
}

function fill(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  flushSync(() => {
    setter?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
