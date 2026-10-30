import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { ReactFlowProvider, useStoreApi, type Edge, type Node } from "@xyflow/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useGraphPortLayout } from "../graph-authoring/useGraphPortLayout";

const updateInternals = vi.hoisted(() => vi.fn());
vi.mock("@xyflow/react", async importOriginal => ({
  ...await importOriginal<typeof import("@xyflow/react")>(), useUpdateNodeInternals: () => updateInternals
}));

let root: Root;
let host: HTMLDivElement;
let store: ReturnType<typeof useStoreApi>;
const n = (id: string, x: number, y: number): Node => ({ id, position: { x, y }, data: {}, measured: { width: 240, height: 64 } });
const edge = (source: string, target: string, extra: Partial<Edge> = {}): Edge => ({ id: `${source}-${target}`, source, target, sourceHandle: "Done", ...extra });

function Harness({ ids = ["Done"] }: { ids?: string[] }) {
  store = useStoreApi();
  const layout = useGraphPortLayout("a", ids, true);
  return <output>{JSON.stringify(layout)}</output>;
}

function mount(nodes: Node[], edges: Edge[]) {
  host = document.createElement("div"); document.body.appendChild(host); root = createRoot(host);
  flushSync(() => root.render(<ReactFlowProvider initialNodes={nodes} initialEdges={edges}><Harness /></ReactFlowProvider>));
}

const result = () => JSON.parse(host.querySelector("output")!.textContent!);
afterEach(() => { if (root) flushSync(() => root.unmount()); host?.remove(); updateInternals.mockClear(); });

describe("graph port store integration", () => {
  it("updates both ends from actual neighbour movement", () => {
    mount([n("a", 0, 0), n("b", 0, 300), n("c", 0, -300)], [edge("a", "b"), edge("c", "a")]);
    expect(result().target.side).toBe("top");
    expect(result().sources[0].side).toBe("bottom");
    flushSync(() => store.getState().setNodes([n("a", 0, 0), n("b", 500, 0), n("c", -500, 0)]));
    expect(result().target.side).toBe("left");
    expect(result().sources[0].side).toBe("right");
    expect(updateInternals).toHaveBeenCalledWith("a");
  });

  it("ignores self loops, hidden edges/nodes and missing neighbours", () => {
    mount([n("a", 0, 0), n("b", 0, 300), { ...n("c", 0, 500), hidden: true }], [
      edge("a", "a"), edge("a", "b", { hidden: true }), edge("a", "c"), edge("missing", "a")
    ]);
    expect(result().target.side).toBe("left");
    expect(result().sources[0].side).toBe("right");
  });

  it("handles shared outcomes and fan-in, then restores defaults after disconnect", () => {
    mount([n("a", 0, 0), n("b", 0, 300), n("c", 20, 500), n("d", 0, -300), n("e", 20, -500)],
      [edge("a", "b"), edge("a", "c"), edge("d", "a"), edge("e", "a")]);
    expect(result().target.side).toBe("top");
    expect(result().sources[0].side).toBe("bottom");
    flushSync(() => store.getState().setEdges([]));
    expect(result().target.side).toBe("left");
    expect(result().sources[0].side).toBe("right");
  });

  it("waits for measurement and refreshes when the node becomes measurable", () => {
    mount([{ ...n("a", 0, 0), measured: {} }, n("b", 0, 300)], [edge("a", "b")]);
    expect(result().sources[0].side).toBe("right");
    expect(updateInternals).not.toHaveBeenCalled();
    flushSync(() => store.getState().setNodes([n("a", 0, 0), n("b", 0, 300)]));
    expect(result().sources[0].side).toBe("bottom");
    expect(updateInternals).toHaveBeenCalledWith("a");
  });
});
