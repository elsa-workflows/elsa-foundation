import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { WorkflowReadOnlyMiniMap } from "../workflow-editor/WorkflowReadOnlyMiniMap";

vi.mock("@xyflow/react", () => ({
  MiniMap: () => <div data-testid="read-only-minimap" />
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("WorkflowReadOnlyMiniMap", () => {
  it("renders nothing for a sparse graph", () => {
    flushSync(() => root.render(<WorkflowReadOnlyMiniMap nodeCount={2} />));
    expect(container.querySelector("[data-testid='read-only-minimap']")).toBeNull();
  });

  it("renders the MiniMap once the graph has more than two nodes", () => {
    flushSync(() => root.render(<WorkflowReadOnlyMiniMap nodeCount={3} />));
    expect(container.querySelector("[data-testid='read-only-minimap']")).toBeTruthy();
  });
});
