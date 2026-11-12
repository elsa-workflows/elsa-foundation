import { describe, expect, it } from "vitest";
import {
  READ_ONLY_MINIMAP_MAX_HIDDEN_NODES,
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
      bottom: 0.24,
      left: 0.12
    });
  });
});

describe("read-only MiniMap token colors", () => {
  it("uses studio text for node rects and a translucent text wash for the viewport mask", () => {
    expect(readOnlyMinimapNodeColor).toBe("var(--studio-text)");
    expect(readOnlyMinimapMaskColor).toMatch(/--studio-text/);
  });
});
