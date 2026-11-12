import { getViewportForBounds } from "@xyflow/react";
import { describe, expect, it } from "vitest";
import {
  getReadOnlyFitViewOptions,
  READ_ONLY_CONTROLS_BUTTON_SIZE,
  READ_ONLY_CONTROLS_CLEARANCE,
  READ_ONLY_CONTROLS_ORIENTATION,
  READ_ONLY_MINIMAP_CLEARANCE_BOTTOM,
  READ_ONLY_MINIMAP_MAX_HIDDEN_NODES,
  READ_ONLY_PANEL_MARGIN,
  readOnlyMinimapFitViewOptions,
  readOnlyMinimapMaskColor,
  readOnlyMinimapNodeColor,
  shouldShowReadOnlyMiniMap
} from "../workflow-editor/readOnlyMinimap";

describe("shouldShowReadOnlyMiniMap", () => {
  it("hides the MiniMap for empty, one-node and two-node graphs", () => {
    expect(shouldShowReadOnlyMiniMap(0)).toBe(false);
    expect(shouldShowReadOnlyMiniMap(1)).toBe(false);
    expect(shouldShowReadOnlyMiniMap(READ_ONLY_MINIMAP_MAX_HIDDEN_NODES)).toBe(false);
  });

  it("shows the MiniMap once the graph has more than two nodes", () => {
    expect(shouldShowReadOnlyMiniMap(3)).toBe(true);
    expect(shouldShowReadOnlyMiniMap(8)).toBe(true);
  });
});

describe("readOnlyMinimapFitViewOptions", () => {
  it("pads the bottom-right corner so fitted nodes stay clear of the MiniMap", () => {
    expect(readOnlyMinimapFitViewOptions.padding).toEqual({
      top: 0.12,
      right: 0.24,
      bottom: "105px",
      left: 0.12
    });
    // 15px panel margin + 80px MiniMap + 2px border + 8px gap.
    expect(READ_ONLY_MINIMAP_CLEARANCE_BOTTOM).toBe(105);
  });
});

describe("read-only MiniMap token colors", () => {
  it("uses studio text for node rects and a translucent text wash for the viewport mask", () => {
    expect(readOnlyMinimapNodeColor).toBe("var(--studio-text)");
    expect(readOnlyMinimapMaskColor).toMatch(/--studio-text/);
  });
});

describe("read-only zoom controls (#4196)", () => {
  it("lay out as one row so a short pane keeps its left edge for activities", () => {
    expect(READ_ONLY_CONTROLS_ORIENTATION).toBe("horizontal");
    // 15px panel margin + one 26px button + 8px gap.
    expect(READ_ONLY_CONTROLS_CLEARANCE).toBe(49);
  });

  it.each([1, 2, 3, 8])("Fit View keeps a tall %i-node graph above the controls row on a 360x245 pane", nodeCount => {
    const width = 360, height = 245;
    const tall = { x: 0, y: 0, width: 240, height: 900 };
    const view = getViewportForBounds(tall, width, height, 0.1, 2, getReadOnlyFitViewOptions(nodeCount).padding ?? 0);
    const controlsTop = height - READ_ONLY_PANEL_MARGIN - READ_ONLY_CONTROLS_BUTTON_SIZE;
    expect((tall.y + tall.height) * view.zoom + view.y).toBeLessThanOrEqual(controlsTop);
  });
});
