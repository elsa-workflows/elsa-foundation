import React, { useRef, useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Node } from "@xyflow/react";
import type { StudioActivityDefinitionImplementationEditorProps, StudioActivityDefinitionImplementationState } from "@elsa-workflows/studio-sdk";
import type { WorkflowNodeData } from "../workflowAdapter";
import type { GraphDocumentAdapter } from "../graph-authoring/graphDocumentAdapter";
import { useGraphAuthoringCanvas } from "../graph-authoring/useGraphAuthoringCanvas";

interface Document { value: number }
type Canvas = ReturnType<typeof useGraphAuthoringCanvas<Document>>;
type CanvasOptions = Parameters<typeof useGraphAuthoringCanvas<Document>>[0];
const activity = { nodeId: "root", activityVersionId: "root@1", inputs: [], outputs: [] };
const adapter: GraphDocumentAdapter<Document> = {
  resourceKind: "activity-definition-graph", readRoot: () => activity, readLayout: () => [],
  replaceRoot: document => document, replaceLayout: document => document, replaceGraph: document => document
};
function node(value: number): Node<WorkflowNodeData> {
  return { id: "root", position: { x: value, y: 0 }, data: { label: String(value), activityVersionId: "root@1", childSlots: [], acceptsInbound: true, sourcePorts: [] } };
}
const buildModel = (_root: typeof activity, _layout: unknown, document: Document) => ({ nodes: [node(document.value)], edges: [] });
const applyModel = (_document: Document, nodes: Node<WorkflowNodeData>[]) => ({ value: nodes[0].position.x });
const cleanup: (() => void)[] = [];
afterEach(() => { for (const unmount of cleanup.splice(0)) unmount(); });

function renderCanvas(legacyChange?: CanvasOptions["onChange"]) {
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  let canvas!: Canvas;
  let current = { value: 0 };
  let echo!: () => void;
  const policy = { refuse: false, throw: false, echo: true, acknowledgement: undefined as (() => unknown) | undefined };
  const historyDuringAck: boolean[] = [];
  const changes = vi.fn<(next: Document, expected: Document) => boolean>();
  const legacyNotification = vi.fn<(next: Document) => void>();
  function Harness() {
    const [document, setDocument] = useState(current);
    const live = useRef(current);
    echo = () => flushSync(() => setDocument(live.current));
    changes.mockImplementation((next, expected) => {
      if (policy.throw) throw new Error("Parent failed before admission");
      if (policy.acknowledgement) return policy.acknowledgement() as boolean;
      if (policy.refuse || expected?.value !== live.current.value) return false;
      current = next; live.current = next;
      if (policy.echo) flushSync(() => setDocument(next));
      historyDuringAck.push(canvas.canUndo);
      return true;
    });
    canvas = useGraphAuthoringCanvas({ document, adapter, resetKey: "resource", buildModel, applyModel,
      onChange: legacyChange ?? legacyNotification, tryChange: legacyChange ? undefined : changes });
    return <output>{canvas.nodes[0]?.position.x}</output>;
  }
  flushSync(() => root.render(<Harness />));
  cleanup.push(() => { flushSync(() => root.unmount()); container.remove(); });
  return { get canvas() { return canvas; }, get current() { return current; }, policy, changes, legacyNotification, historyDuringAck, echo: () => echo(), container };
}
async function commit(fixture: ReturnType<typeof renderCanvas>, value: number) {
  expect(fixture.canvas.commitDocument({ value })).toBe(true);
  await vi.waitFor(() => expect(fixture.canvas.nodes[0].position.x).toBe(value));
}

describe("graph parent admission and canonical history", () => {
  it("accepts existing async and concise numeric legacy callbacks against the real SDK and hook types", async () => {
    const implementation = { payload: {}, layout: [] };
    const implementations: StudioActivityDefinitionImplementationState[] = [];
    const sdkConsumers: Pick<StudioActivityDefinitionImplementationEditorProps, "onChange">[] = [
      { onChange: async value => { implementations.push(value); } },
      { onChange: value => implementations.push(value) }
    ];
    for (const consumer of sdkConsumers) await consumer.onChange(implementation);
    expect(implementations).toEqual([implementation, implementation]);
    const documents: Document[] = [];
    const hookConsumers: CanvasOptions["onChange"][] = [
      async value => { documents.push(value); },
      value => documents.push(value)
    ];
    for (const onChange of hookConsumers) await onChange({ value: 1 });
    expect(documents).toEqual([{ value: 1 }, { value: 1 }]);
  });

  it("keeps prior history through a synchronous parent props acknowledgement and passes the exact baseline", async () => {
    const h = renderCanvas();
    await commit(h, 1); await commit(h, 2);
    expect(h.historyDuringAck).toEqual([false, true]);
    expect(h.changes.mock.calls.map(([, expected]) => expected?.value)).toEqual([0, 1]);
    expect(h.canvas.undo()).toBe(true);
    await vi.waitFor(() => expect(h.current.value).toBe(1));
    expect(h.canvas.undo()).toBe(true);
    await vi.waitFor(() => { expect(h.current.value).toBe(0); expect(h.canvas.canUndo).toBe(false); });
    expect(h.canvas.redo()).toBe(true);
    await vi.waitFor(() => expect(h.current.value).toBe(1));
    expect(h.canvas.redo()).toBe(true);
    await vi.waitFor(() => expect(h.current.value).toBe(2));
  });

  it("does not clear existing future history or canonical state when an ordinary edit is refused", async () => {
    const h = renderCanvas(); await commit(h, 1); await commit(h, 2);
    h.canvas.undo(); await vi.waitFor(() => expect(h.canvas.canRedo).toBe(true));
    h.policy.refuse = true;
    expect(h.canvas.commitDocument({ value: 99 })).toBe(false);
    expect(h.current.value).toBe(1);
    expect(h.canvas.canUndo).toBe(true); expect(h.canvas.canRedo).toBe(true);
    h.policy.refuse = false;
    expect(h.canvas.redo()).toBe(true); await vi.waitFor(() => expect(h.current.value).toBe(2));
  });

  it.each([
    ["undefined", (): unknown => undefined],
    ["a Promise", (): unknown => Promise.resolve(true)],
    ["a rejected Promise", (): unknown => Promise.reject(new Error("Invalid async host"))],
    ["a truthy number", (): unknown => 1]
  ] as const)("refuses %s as a controlled acknowledgement without losing canonical state or redo", async (_name, acknowledgement) => {
    const h = renderCanvas(); await commit(h, 1); await commit(h, 2);
    h.canvas.undo(); await vi.waitFor(() => expect(h.canvas.canRedo).toBe(true));
    h.policy.acknowledgement = acknowledgement;
    const admitted = flushSync(() => h.canvas.commitDocument({ value: 99 }));
    await vi.waitFor(() => expect(h.canvas.nodes[0].position.x).toBe(1));
    expect(h.current.value).toBe(1);
    expect(h.canvas.canUndo).toBe(true); expect(h.canvas.canRedo).toBe(true);
    expect(admitted).toBe(false);
    expect(h.legacyNotification).not.toHaveBeenCalled();
    h.policy.acknowledgement = undefined;
    expect(h.canvas.redo()).toBe(true); await vi.waitFor(() => expect(h.current.value).toBe(2));
  });

  it.each(["async", "numeric", "false"] as const)("keeps legacy %s callback returns as notifications without treating them as receipts", async result => {
    const observed: number[] = [];
    const h = renderCanvas(next => {
      observed.push(next.value);
      if (result === "async") return Promise.resolve();
      return result === "false" ? false : observed.length;
    });
    await commit(h, 1);
    expect(h.canvas.undo()).toBe(true);
    await vi.waitFor(() => expect(h.canvas.nodes[0].position.x).toBe(0));
    expect(observed).toEqual([1, 0]);
    expect(h.changes).not.toHaveBeenCalled();
  });

  it.each(["undo", "redo"] as const)("keeps the previous canonical history when %s is refused", async action => {
    const h = renderCanvas(); await commit(h, 1); await commit(h, 2);
    if (action === "redo") { h.canvas.undo(); await vi.waitFor(() => expect(h.canvas.canRedo).toBe(true)); }
    const previous = h.current.value;
    h.policy.refuse = true;
    expect(h.canvas[action]()).toBe(false);
    expect(h.current.value).toBe(previous);
    h.policy.refuse = false;
    expect(h.canvas[action]()).toBe(true);
    await vi.waitFor(() => expect(h.current.value).toBe(action === "undo" ? 1 : 2));
  });

  it("restores a prior pending emission marker and visible canonical canvas when parent admission throws", async () => {
    const h = renderCanvas(); h.policy.echo = false; await commit(h, 1);
    flushSync(() => h.canvas.setNodes([node(99)]));
    h.policy.throw = true;
    expect(() => h.canvas.commitCanvas([node(99)])).toThrow(/Parent failed/);
    await vi.waitFor(() => expect(h.canvas.nodes[0].position.x).toBe(1));
    expect(h.current.value).toBe(1);
    h.policy.throw = false; h.echo();
    await vi.waitFor(() => expect(h.canvas.canUndo).toBe(true));
    expect(h.canvas.undo()).toBe(true);
    await vi.waitFor(() => expect(h.current.value).toBe(0));
  });
});
