import { getViewportForBounds, type Node } from "@xyflow/react";
import { describe, expect, it } from "vitest";
import { getReadableFitViewport, pickReadOnlyFocusNode, READ_ONLY_READABLE_ZOOM, readableEdgeMargin, readableMinimapEdgeMargin } from "../workflow-editor/readableFitViewport";
import { READ_ONLY_CONTROLS_BUTTON_SIZE, READ_ONLY_PANEL_MARGIN, readOnlyMinimapFitViewOptions } from "../workflow-editor/readOnlyMinimap";

const padding = { top: "24px", right: "112px", bottom: "32px", left: "32px" } as const;
// A User Task (Approve/Reject/Unmatched) followed by Set Output, as the composed journey lays it out.
const task = { x: 0, y: 0, width: 240, height: 96 };
const output = { x: 420, y: 8, width: 240, height: 80 };
const graph = { x: 0, y: 0, width: 660, height: 96 };

function node(id: string, x: number, y: number, extra: Partial<Node> = {}, runtime?: Record<string, unknown>): Node {
  return { id, position: { x, y }, data: runtime ? { runtime: { faultCount: 0, incidentCount: 0, hasBlockingIncident: false, selected: false, ...runtime } } : {}, ...extra };
}

describe("getReadableFitViewport", () => {
  it("keeps the whole-graph fit when it is already readable", () => {
    const input = { graph, focus: task, width: 1100, height: 700, padding, minZoom: 0.1, maxZoom: 2, readableZoom: READ_ONLY_READABLE_ZOOM };
    const view = getReadableFitViewport(input);
    expect(view).toEqual(getViewportForBounds(graph, 1100, 700, 0.1, 2, padding));
    expect(view.zoom).toBeGreaterThanOrEqual(1);
  });

  it("uses the readable zoom on a narrow pane and keeps the focus activity and its labels in view", () => {
    const width = 340;
    const view = getReadableFitViewport({ graph, focus: task, width, height: 600, padding, minZoom: 0.1, maxZoom: 2, readableZoom: READ_ONLY_READABLE_ZOOM });
    expect(getViewportForBounds(graph, width, 600, 0.1, 2, padding).zoom).toBeLessThan(0.4);
    expect(view.zoom).toBe(1);
    const left = task.x * view.zoom + view.x;
    // The graph's start never leaves empty canvas wider than the leading margin.
    expect(left).toBeLessThanOrEqual(32);
    expect(left).toBeGreaterThanOrEqual(0);
    // Outcome labels sit up to ~90px to the right of the node box.
    expect(left + task.width * view.zoom + 90).toBeLessThanOrEqual(width);
  });

  it("centres vertically when the graph fits that axis at the readable zoom", () => {
    const view = getReadableFitViewport({ graph, focus: task, width: 340, height: 600, padding, minZoom: 0.1, maxZoom: 2, readableZoom: 1 });
    const top = graph.y * view.zoom + view.y;
    const bottom = (graph.y + graph.height) * view.zoom + view.y;
    expect(Math.abs((top - 24) - (600 - readableEdgeMargin.bottom - bottom))).toBeLessThan(1);
  });

  it("does not scroll past the graph's far edge when the focus activity is the last one", () => {
    const width = 340;
    const view = getReadableFitViewport({ graph, focus: output, width, height: 600, padding, minZoom: 0.1, maxZoom: 2, readableZoom: 1 });
    const right = (graph.x + graph.width) * view.zoom + view.x;
    expect(right).toBeGreaterThanOrEqual(width - 112);
    const outputLeft = output.x * view.zoom + view.x;
    expect(outputLeft).toBeGreaterThanOrEqual(0);
  });

  it("centres the first activity clear of the MiniMap on a short pane", () => {
    // A three-node graph on a 360x245 phone pane: the first activity's box and outcome labels
    // (about 90px past its right edge) share the MiniMap's columns, so only the height can keep them apart.
    const graph3 = { x: 0, y: 0, width: 660, height: 416 };
    const first = { x: 0, y: 160, width: 240, height: 96 };
    const width = 360, height = 245;
    const input = { graph: graph3, focus: first, width, height, padding: readOnlyMinimapFitViewOptions.padding,
      minZoom: 0.1, maxZoom: 2, readableZoom: 1 };
    // The 120x80 MiniMap (1px border) sits 15px from the bottom-right corner.
    const minimapLeft = width - 15 - 122, minimapTop = height - 15 - 82;
    const underMinimap = (view: { x: number; y: number }) =>
      first.x + first.width + 90 + view.x > minimapLeft && first.y + first.height + view.y > minimapTop;
    expect(underMinimap(getReadableFitViewport(input))).toBe(true);
    const view = getReadableFitViewport({ ...input, margin: readableMinimapEdgeMargin });
    expect(view.zoom).toBe(1);
    expect(underMinimap(view)).toBe(false);
    expect(first.y + view.y).toBeGreaterThanOrEqual(0);
  });

  it("falls back to the whole-graph fit when there is no focus activity", () => {
    const view = getReadableFitViewport({ graph, focus: null, width: 340, height: 600, padding, minZoom: 0.1, maxZoom: 2, readableZoom: 1 });
    expect(view).toEqual(getViewportForBounds(graph, 340, 600, 0.1, 2, padding));
  });
});

describe("pickReadOnlyFocusNode", () => {
  it("starts where the graph starts, whatever a later activity's runtime state", () => {
    expect(pickReadOnlyFocusNode([
      node("selected", 420, 0, { selected: true }),
      node("faulted", 300, 0, {}, { status: "Faulted", hasBlockingIncident: true, incidentCount: 1 }),
      node("start", 0, 40),
      node("above", 0, 0, {}, { status: "Completed" })
    ])?.id).toBe("above");
  });
});

it("keeps the start of a focus activity wider than the pane in view", () => {
  const wide = { x: 0, y: 0, width: 300, height: 96 };
  const view = getReadableFitViewport({ graph: { x: 0, y: 0, width: 720, height: 96 }, focus: wide, width: 320, height: 600, padding, minZoom: 0.1, maxZoom: 2, readableZoom: 1 });
  expect(wide.x * view.zoom + view.x).toBe(8);
});

describe("readable first view on a short phone pane (#4196)", () => {
  // The zoom controls are one 26px row, 15px above the pane's bottom-left corner.
  const width = 360, height = 245;
  const controlsTop = height - READ_ONLY_PANEL_MARGIN - READ_ONLY_CONTROLS_BUTTON_SIZE;

  it.each([
    ["without the MiniMap", readableEdgeMargin],
    ["with the MiniMap", readableMinimapEdgeMargin]
  ])("keeps the graph above the controls row %s", (_name, margin) => {
    const view = getReadableFitViewport({ graph, focus: task, width, height, padding, minZoom: 0.1, maxZoom: 2, readableZoom: 1, margin });
    expect(view.zoom).toBe(1);
    expect(graph.y * view.zoom + view.y).toBeGreaterThanOrEqual(0);
    expect((graph.y + graph.height) * view.zoom + view.y).toBeLessThanOrEqual(controlsTop - 8);
  });
});
