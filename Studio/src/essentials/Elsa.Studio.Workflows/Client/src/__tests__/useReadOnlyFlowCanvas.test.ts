import { describe, expect, it } from "vitest";
import { mergeMeasuredNodeDimensions } from "../workflow-editor/useReadOnlyFlowCanvas";

describe("mergeMeasuredNodeDimensions", () => {
  it("copies measured boxes onto matching nodes so MiniMap can paint them", () => {
    const nodes = [
      { id: "a", type: "workflowActivity", position: { x: 0, y: 0 }, data: {} },
      { id: "b", type: "workflowActivity", position: { x: 0, y: 80 }, data: {} }
    ];
    const measured = [
      { id: "a", type: "workflowActivity", position: { x: 0, y: 0 }, data: {}, measured: { width: 168, height: 72 } }
    ];

    expect(mergeMeasuredNodeDimensions(nodes, measured)).toEqual([
      { id: "a", type: "workflowActivity", position: { x: 0, y: 0 }, data: {}, measured: { width: 168, height: 72 } },
      { id: "b", type: "workflowActivity", position: { x: 0, y: 80 }, data: {} }
    ]);
  });

  it("ignores measured boxes when the node type changed", () => {
    const nodes = [{ id: "a", type: "bpmnElement", position: { x: 0, y: 0 }, data: {} }];
    const measured = [{ id: "a", type: "workflowActivity", position: { x: 0, y: 0 }, data: {}, measured: { width: 120, height: 40 } }];
    expect(mergeMeasuredNodeDimensions(nodes, measured)[0]).not.toHaveProperty("measured");
  });
});
