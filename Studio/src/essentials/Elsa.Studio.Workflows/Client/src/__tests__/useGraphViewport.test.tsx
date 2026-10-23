import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactFlowInstance, Viewport } from "@xyflow/react";
import { useGraphViewport } from "../graph-authoring/useGraphViewport";

type ViewportInstance = Pick<ReactFlowInstance, "fitView" | "setViewport">;
type HookArgs = {
  nodes: readonly unknown[];
  scopeKey: string;
  instance: ViewportInstance | null;
};
type HookResult = ReturnType<typeof useGraphViewport>;

const mounted: Array<{ root: Root; container: HTMLDivElement }> = [];
let animationFrameId = 0;
let animationFrames: Map<number, FrameRequestCallback>;

beforeEach(() => {
  animationFrameId = 0;
  animationFrames = new Map();
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
    const id = ++animationFrameId;
    animationFrames.set(id, callback);
    return id;
  });
  vi.stubGlobal("cancelAnimationFrame", (id: number) => {
    animationFrames.delete(id);
  });
});

afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.container.remove();
  }
  animationFrames.clear();
  vi.unstubAllGlobals();
});

describe("useGraphViewport", () => {
  it.each(["projection before onInit", "onInit before projection"])(
    "fits the initial nonempty diagram when %s",
    order => {
      const sourceNodes = [node("source")];
      const projectedNodes = [node("projected")];
      const instance = flowInstance();
      const fixture = renderHook({ nodes: sourceNodes, scopeKey: "root", instance: null });

      if (order === "projection before onInit") {
        fixture.render({ nodes: projectedNodes, scopeKey: "root", instance: null });
        expect(animationFrames.size).toBe(0);
        fixture.render({ nodes: projectedNodes, scopeKey: "root", instance });
      } else {
        fixture.render({ nodes: sourceNodes, scopeKey: "root", instance });
        expect(animationFrames.size).toBe(0);
        fixture.render({ nodes: projectedNodes, scopeKey: "root", instance });
      }

      expect(instance.fitView).not.toHaveBeenCalled();
      flushAnimationFrames();

      expect(instance.fitView).toHaveBeenCalledTimes(1);
      expect(instance.fitView).toHaveBeenCalledWith({ padding: 0.2, maxZoom: 1 });
    }
  );

  it("settles an empty initial projection so first placement after a pan does not trigger a fit", () => {
    const instance = flowInstance();
    const fixture = renderHook({ nodes: [], scopeKey: "root", instance: null });
    const emptyProjection: unknown[] = [];
    const manuallyPannedViewport = viewport(180, 90, 0.9);

    fixture.render({ nodes: emptyProjection, scopeKey: "root", instance: null });
    fixture.render({ nodes: emptyProjection, scopeKey: "root", instance });
    flushAnimationFrames();
    expect(instance.fitView).not.toHaveBeenCalled();

    fixture.result.current.onMoveEnd(null, manuallyPannedViewport);
    fixture.render({ nodes: [node("first")], scopeKey: "root", instance });
    flushAnimationFrames();

    expect(instance.fitView).not.toHaveBeenCalled();
    expect(instance.setViewport).not.toHaveBeenCalled();
  });

  it("restores a scope viewport only after its fresh projection arrives", () => {
    const instance = flowInstance();
    const fixture = openSettledRoot(instance);
    const rootViewport = viewport(120, 70, 0.8);
    const childSourceNodes = fixture.nodes;
    const childNodes = [node("child")];

    fixture.result.current.onMoveEnd(null, rootViewport);
    fixture.render({ nodes: childSourceNodes, scopeKey: "root/child", instance });
    expect(animationFrames.size).toBe(0);

    fixture.render({ nodes: childNodes, scopeKey: "root/child", instance });
    flushAnimationFrames();
    expect(instance.fitView).toHaveBeenCalledTimes(2);

    const rootProjection = [node("root-return")];
    fixture.render({ nodes: childNodes, scopeKey: "root", instance });
    fixture.render({ nodes: rootProjection, scopeKey: "root", instance });

    expect(instance.setViewport).not.toHaveBeenCalled();
    flushAnimationFrames();

    expect(instance.setViewport).toHaveBeenCalledTimes(1);
    expect(instance.setViewport).toHaveBeenCalledWith(rootViewport);
    expect(instance.fitView).toHaveBeenCalledTimes(2);
  });

  it("cancels a queued fit when the scope changes before the animation frame", () => {
    const instance = flowInstance();
    const fixture = renderHook({ nodes: [node("source")], scopeKey: "root", instance: null });
    const rootProjection = [node("root")];

    fixture.render({ nodes: rootProjection, scopeKey: "root", instance });
    expect(animationFrames.size).toBe(1);

    fixture.render({ nodes: rootProjection, scopeKey: "root/child", instance });
    expect(animationFrames.size).toBe(0);
    flushAnimationFrames();
    expect(instance.fitView).not.toHaveBeenCalled();

    fixture.render({ nodes: [node("child")], scopeKey: "root/child", instance });
    flushAnimationFrames();
    expect(instance.fitView).toHaveBeenCalledTimes(1);
  });

  it("restores the remembered viewport to a replacement instance without reading the old instance", () => {
    const oldInstance = {
      ...flowInstance(),
      getViewport: vi.fn(() => {
        throw new Error("A replaced canvas instance must not be queried.");
      })
    };
    const replacementInstance = flowInstance();
    const fixture = openSettledRoot(oldInstance);
    const rememberedViewport = viewport(240, 110, 0.7);

    fixture.result.current.onMoveEnd(null, rememberedViewport);
    fixture.render({ nodes: fixture.nodes, scopeKey: "root", instance: replacementInstance });

    expect(animationFrames.size).toBe(1);
    flushAnimationFrames();

    expect(oldInstance.getViewport).not.toHaveBeenCalled();
    expect(replacementInstance.setViewport).toHaveBeenCalledTimes(1);
    expect(replacementInstance.setViewport).toHaveBeenCalledWith(rememberedViewport);
    expect(replacementInstance.fitView).not.toHaveBeenCalled();
  });

  it("does not refit after ordinary node edits in the same scope", () => {
    const instance = flowInstance();
    const fixture = openSettledRoot(instance);
    expect(instance.fitView).toHaveBeenCalledTimes(1);

    fixture.render({ nodes: [node("root-edited")], scopeKey: "root", instance });
    flushAnimationFrames();
    fixture.render({ nodes: [node("root-edited-again")], scopeKey: "root", instance });
    flushAnimationFrames();

    expect(instance.fitView).toHaveBeenCalledTimes(1);
    expect(instance.setViewport).not.toHaveBeenCalled();
  });
});

function openSettledRoot(instance: ViewportInstance) {
  const sourceNodes = [node("source")];
  const fixture = renderHook({ nodes: sourceNodes, scopeKey: "root", instance: null });
  const projectedNodes = [node("root")];

  fixture.render({ nodes: projectedNodes, scopeKey: "root", instance: null });
  fixture.render({ nodes: projectedNodes, scopeKey: "root", instance });
  flushAnimationFrames();

  expect(instance.fitView).toHaveBeenCalledTimes(1);
  return { ...fixture, nodes: projectedNodes };
}

function renderHook(initialArgs: HookArgs) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const result: { current: HookResult } = { current: undefined as unknown as HookResult };
  const fixture = {
    result,
    nodes: initialArgs.nodes,
    render(args: HookArgs) {
      fixture.nodes = args.nodes;
      flushSync(() => root.render(<HookHarness {...args} result={result} />));
    }
  };
  mounted.push({ root, container });
  fixture.render(initialArgs);
  return fixture;
}

function HookHarness({ result, ...hookArgs }: HookArgs & { result: { current: HookResult } }) {
  result.current = useGraphViewport(hookArgs);
  return null;
}

function flowInstance() {
  return {
    fitView: vi.fn(async () => true),
    setViewport: vi.fn(async () => true)
  } satisfies ViewportInstance;
}

function flushAnimationFrames() {
  const callbacks = [...animationFrames.values()];
  animationFrames.clear();
  callbacks.forEach(callback => callback(0));
}

function node(id: string) {
  return { id };
}

function viewport(x: number, y: number, zoom: number): Viewport {
  return { x, y, zoom };
}
