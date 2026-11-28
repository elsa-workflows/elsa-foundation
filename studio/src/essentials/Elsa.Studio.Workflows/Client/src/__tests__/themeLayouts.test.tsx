import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Position } from "@xyflow/react";
import { WorkflowFlowEdge } from "../workflow-editor/graph";
import { WorkflowEdgeActionsContext } from "../workflow-editor/contexts";
import { GraphAuthoringWorkbench } from "../graph-authoring/GraphAuthoringWorkbench";
import type { useSidePanelLayout } from "../workflow-editor/useSidePanelLayout";

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    BaseEdge: ({ path, label }: { path: string; label?: React.ReactNode }) => <svg><path data-edge-path d={path} />{label != null && <text data-edge-label>{label}</text>}</svg>,
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
  document.documentElement.removeAttribute("data-theme-layout");
});

function renderEdge() {
  flushSync(() => root.render(
    <WorkflowFlowEdge
      id="connection-1"
      source="a"
      target="b"
      sourceX={0}
      sourceY={0}
      targetX={200}
      targetY={80}
      sourcePosition={Position.Right}
      targetPosition={Position.Left}
    />
  ));
  return container.querySelector("[data-edge-path]")!.getAttribute("d")!;
}

describe("theme layouts in the designer", () => {
  it.each([
    ["classic", false],
    ["workbench", false],
    ["floating", true],
    ["editorial", true]
  ] as const)("draws %s connections %s", (layout, curved) => {
    document.documentElement.setAttribute("data-theme-layout", layout);

    // A bezier path is a single cubic segment; the squared smooth-step path has none.
    expect(renderEdge().includes("C")).toBe(curved);
  });

  it("redraws connections when the theme's layout changes", async () => {
    const squared = renderEdge();

    document.documentElement.setAttribute("data-theme-layout", "floating");
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.querySelector("[data-edge-path]")!.getAttribute("d")).not.toBe(squared);
  });

  it("gives the palette's columns to the canvas when a resource has no left panel", () => {
    flushSync(() => root.render(
      <GraphAuthoringWorkbench
        resourceKind="workflow-definition"
        layout={sidePanelLayout()}
        canvas={<main aria-label="Graph canvas" />}
        inspector={{ ariaLabel: "Inspector panel", tabLabel: "Inspector tabs", tabs: [{ id: "inspector", title: "Inspector", order: 0, icon: null, render: () => null }], activeTabId: "inspector", onSelect: () => {} }}
      />
    ));

    const workspace = container.querySelector("[data-graph-authoring-resource]")!;
    expect(workspace.classList.contains("palette-absent")).toBe(true);
    expect(container.querySelector(".wf-palette")).toBeNull();
    expect(container.querySelector("[aria-label='Resize activities panel']")).toBeNull();
    expect(container.querySelector("[aria-label='Inspector panel']")).not.toBeNull();
  });

  it("offers the way back to the canvas from a phone inspector sheet and drops its resize handles", () => {
    const showCanvas = vi.fn();
    const renderWorkbench = (layout: ReturnType<typeof useSidePanelLayout>) => flushSync(() => root.render(
      <GraphAuthoringWorkbench
        resourceKind="workflow-definition"
        layout={layout}
        palette={{ ariaLabel: "Activities panel", tabLabel: "Activities tabs", tabs: [{ id: "activities", title: "Activities", order: 0, icon: null, render: () => null }], activeTabId: "activities", onSelect: () => {} }}
        canvas={<main aria-label="Graph canvas" />}
        inspector={{ ariaLabel: "Inspector panel", tabLabel: "Inspector tabs", tabs: [{ id: "inspector", title: "Inspector", order: 0, icon: null, render: () => null }], activeTabId: "inspector", onSelect: () => {} }}
      />
    ));

    renderWorkbench(sidePanelLayout({ phoneWidth: true, inspectorSheet: true, paletteCollapsed: true, paletteExpanded: false, showCanvas }));
    const back = container.querySelector<HTMLButtonElement>("[aria-label='Back to canvas']");
    expect(back?.textContent).toBe("Canvas");
    expect(container.querySelector(".wf-palette [aria-label='Back to canvas']")).toBeNull();
    expect(container.querySelector("[role='separator']")).toBeNull();
    flushSync(() => back!.click());
    expect(showCanvas).toHaveBeenCalledOnce();

    renderWorkbench(sidePanelLayout({ phoneWidth: true, inspectorSheet: false, inspectorCollapsed: true, inspectorExpanded: false }));
    expect(container.querySelector("[aria-label='Back to canvas']")).toBeNull();

    renderWorkbench(sidePanelLayout());
    expect(container.querySelector("[aria-label='Back to canvas']")).toBeNull();
    expect(container.querySelectorAll("[role='separator']")).toHaveLength(2);
  });

  it("moves focus from a hidden canvas control to the way back when the sheet opens", () => {
    const render = (inspectorSheet: boolean) => flushSync(() => root.render(
      <GraphAuthoringWorkbench
        resourceKind="workflow-definition"
        layout={sidePanelLayout({ phoneWidth: true, inspectorSheet })}
        canvas={<section className="wf-canvas-shell"><button type="button" className="wf-sequence-step">Write Line</button></section>}
        inspector={{ ariaLabel: "Inspector panel", tabLabel: "Inspector tabs", tabs: [{ id: "inspector", title: "Inspector", order: 0, icon: null, render: () => null }], activeTabId: "inspector", onSelect: () => {} }}
      />
    ));
    render(false);
    container.querySelector<HTMLButtonElement>(".wf-sequence-step")!.focus();
    render(true);
    expect(document.activeElement).toBe(container.querySelector("[aria-label='Back to canvas']"));

    render(false);
    container.querySelector<HTMLButtonElement>("[aria-label='Collapse inspector panel']")!.focus();
    render(true);
    expect(document.activeElement).toBe(container.querySelector("[aria-label='Collapse inspector panel']"));
  });

  it.each([
    ["the selected activity", true],
    ["the inspector's expand control without a selection", false]
  ] as const)("returns focus to %s when a phone inspector sheet closes", async (_, selected) => {
    flushSync(() => root.render(
      <GraphAuthoringWorkbench
        resourceKind="workflow-definition"
        layout={sidePanelLayout({ phoneWidth: true, inspectorSheet: true })}
        canvas={<section className="wf-canvas-shell"><button type="button" className="wf-sequence-step" aria-pressed={selected}>Write Line</button></section>}
        inspector={{ ariaLabel: "Inspector panel", tabLabel: "Inspector tabs", tabs: [{ id: "inspector", title: "Inspector", order: 0, icon: null, render: () => null }], activeTabId: "inspector", onSelect: () => {} }}
      />
    ));

    container.querySelector<HTMLButtonElement>("[aria-label='Back to canvas']")!.click();
    await new Promise(resolve => requestAnimationFrame(resolve));

    expect(document.activeElement).toBe(selected
      ? container.querySelector(".wf-sequence-step")
      : container.querySelector(".wf-inspector .wf-panel-actions button"));
  });
});

function sidePanelLayout(overrides: Partial<ReturnType<typeof useSidePanelLayout>> = {}): ReturnType<typeof useSidePanelLayout> {
  return {
    containerRef: () => {},
    paletteWidth: 260,
    inspectorWidth: 320,
    paletteMinimumWidth: 180,
    inspectorMinimumWidth: 260,
    paletteCollapsed: false,
    inspectorCollapsed: false,
    maximizedSidePanel: null,
    paletteExpanded: true,
    inspectorExpanded: true,
    phoneWidth: false,
    inspectorSheet: false,
    showCanvas: () => {},
    editorBodyClassName: "wf-editor-body",
    editorBodyStyle: { "--wf-palette-width": "260px", "--wf-inspector-width": "320px" },
    setInspectorCollapsed: () => {},
    toggleSidePanelCollapsed: () => {},
    toggleSidePanelMaximized: () => {},
    startSidePanelResize: () => {},
    handleSidePanelResizeKeyDown: () => {},
    ...overrides
  };
}

it.each([
  [false, "Approve", "Approve", null],
  [true, "Approve", "Approve", "Approve"],
  [false, "Approve", "Manager approved", "Manager approved"],
  [false, undefined, "Custom connection", "Custom connection"]
] as const)("preserves custom/editable edge labels and removes a read-only port duplicate (%s, %s, %s)", (editable, sourceHandleId, label, expected) => {
  const actions = editable ? { highlightedEdgeId: null, deleteEdge: vi.fn(), requestInsertActivity: vi.fn() } : null;
  flushSync(() => root.render(<WorkflowEdgeActionsContext.Provider value={actions}>
    <WorkflowFlowEdge id="edge" source="a" target="b" sourceX={0} sourceY={0} targetX={200} targetY={80}
      sourcePosition={Position.Bottom} targetPosition={Position.Top} sourceHandleId={sourceHandleId} label={label} />
  </WorkflowEdgeActionsContext.Provider>));
  expect(container.querySelector("[data-edge-label]")?.textContent ?? null).toBe(expected);
});
