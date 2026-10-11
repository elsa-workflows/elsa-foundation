import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Position } from "@xyflow/react";
import { WorkflowFlowEdge } from "../workflow-editor/graph";
import { GraphAuthoringWorkbench } from "../graph-authoring/GraphAuthoringWorkbench";
import type { useSidePanelLayout } from "../workflow-editor/useSidePanelLayout";

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    BaseEdge: ({ path }: { path: string }) => <svg><path data-edge-path d={path} /></svg>,
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
});

function sidePanelLayout(): ReturnType<typeof useSidePanelLayout> {
  return {
    paletteWidth: 260,
    inspectorWidth: 320,
    paletteCollapsed: false,
    inspectorCollapsed: false,
    maximizedSidePanel: null,
    paletteExpanded: true,
    inspectorExpanded: true,
    editorBodyClassName: "wf-editor-body",
    editorBodyStyle: {},
    toggleSidePanelCollapsed: () => {},
    toggleSidePanelMaximized: () => {},
    startSidePanelResize: () => {},
    handleSidePanelResizeKeyDown: () => {}
  } as unknown as ReturnType<typeof useSidePanelLayout>;
}
