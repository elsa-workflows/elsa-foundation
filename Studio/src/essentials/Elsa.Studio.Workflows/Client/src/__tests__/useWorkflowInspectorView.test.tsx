import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { useWorkflowInspectorView } from "../workflow-editor/useWorkflowInspectorView";

describe("useWorkflowInspectorView", () => {
  it("keeps Configuration when a retained activity-selection callback runs", () => {
    const select = vi.fn();
    const selectNodes = vi.fn();
    const harness = mountInspectorView(select, selectNodes);

    try {
      flushSync(() => harness.state.current!.showRunData());
      const retainedSelection = harness.state.current!.selectEditorNode;
      flushSync(() => harness.state.current!.setInspectorView("configuration"));
      flushSync(() => retainedSelection("write-two"));

      expect(harness.state.current!.inspectorView).toBe("configuration");
      expect(select).toHaveBeenCalledWith("write-two");
    } finally {
      harness.unmount();
    }
  });

  it("requests Run data for explicit execution navigation", () => {
    const harness = mountInspectorView(vi.fn(), vi.fn());

    try {
      flushSync(() => harness.state.current!.showRunData());

      expect(harness.state.current!.inspectorView).toBe("run-data");
    } finally {
      harness.unmount();
    }
  });
});

function mountInspectorView(select: (nodeId: string | null) => void, selectNodes: (nodeIds: string[], primaryNodeId?: string | null) => void) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const state: { current: ReturnType<typeof useWorkflowInspectorView> | null } = { current: null };

  function Harness() {
    state.current = useWorkflowInspectorView(select, selectNodes);
    return <output>{state.current.inspectorView}</output>;
  }

  flushSync(() => root.render(<Harness />));

  return {
    state,
    unmount: () => {
      root.unmount();
      container.remove();
    }
  };
}