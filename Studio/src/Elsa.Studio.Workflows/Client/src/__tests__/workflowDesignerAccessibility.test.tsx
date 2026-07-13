import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Position } from "@xyflow/react";
import { ActivityPalettePanel } from "../workflow-editor/ActivityPalettePanel";
import { ConnectMenu, WorkflowFlowEdge } from "../workflow-editor/graph";
import { WorkflowEdgeActionsContext } from "../workflow-editor/contexts";
import type { ActivityCatalogItem } from "../workflowTypes";

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    BaseEdge: () => <svg aria-hidden="true" />,
    EdgeLabelRenderer: ({ children }: { children: React.ReactNode }) => <>{children}</>
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
