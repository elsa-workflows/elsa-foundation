import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { emptyGraphElements, graphElementDragDataType, mergeGraphPaletteGroups, type GraphPaletteElement } from "../graph-authoring/graphPaletteElements";
import { useGraphCanvasInteractions } from "../graph-authoring/useGraphCanvasInteractions";
import { ActivityPalettePanel } from "../workflow-editor/ActivityPalettePanel";
import { bpmnPaletteElements } from "../bpmn/bpmnPaletteElements";
import { writeLine } from "./fixtures";

// A different designer's element proves the shared palette and placement path need no BPMN types.
const comment: GraphPaletteElement = {
  id: "flowchart:comment", category: "Annotations", label: "Comment", keywords: ["note", "explain"],
  icon: <svg data-comment-icon />,
  createNode: position => ({ id: "comment-1", type: "annotation", position, data: { text: "A note" } })
};
const mounted: Root[] = [];
afterEach(() => {
  mounted.splice(0).forEach(root => flushSync(() => root.unmount()));
  document.body.replaceChildren();
  vi.restoreAllMocks();
});

describe("designer-owned palette elements", () => {
  it("merges categories and searches labels, categories, descriptions and keywords across element families", () => {
    const groups = mergeGraphPaletteGroups([{ category: "BPMN", activities: [writeLine] }], [...bpmnPaletteElements, comment], "");
    expect(groups.map(group => group.category)).toEqual(["Annotations", "BPMN"]);
    expect(groups[1].activities).toEqual([writeLine]);
    expect(groups[1].elements).toHaveLength(7);
    expect(mergeGraphPaletteGroups([], [comment], "note")[0].elements).toEqual([comment]);
    expect(mergeGraphPaletteGroups([], bpmnPaletteElements, "xor")[0].elements[0].label).toBe("Exclusive gateway");
    expect(mergeGraphPaletteGroups([], bpmnPaletteElements, "no match")).toEqual([]);
  });

  it("uses the same keyboard tree and row layout for structural elements and activities", () => {
    const inserted = vi.fn();
    const container = document.createElement("div");
    document.body.append(container);
    const root = createRoot(container);
    mounted.push(root);
    flushSync(() => root.render(<ActivityPalettePanel
      paletteSearch="" onSearchChange={() => {}} groups={[{ category: "Annotations", activities: [writeLine] }]}
      elements={[comment]} expandedCategories={new Set(["Annotations"])} onToggleCategory={() => {}}
      onElementClick={inserted} onActivityClick={() => {}} onActivityDragStart={() => {}} onActivityDragEnd={() => {}} onActivityPointerDown={() => {}}
    />));
    expect(container.querySelector("input")?.placeholder).toBe("Search elements");
    const items = [...container.querySelectorAll<HTMLButtonElement>("[role='treeitem']")];
    items[0].focus();
    document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));
    expect(document.activeElement).toBe(items[1]);
    document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
    expect(inserted).toHaveBeenCalledOnce();
    expect(inserted).toHaveBeenCalledWith(comment);
    expect(items[1].querySelector("[data-comment-icon]")).not.toBeNull();
    expect(items[1].className).toBe(items[2].className);
  });

  it("places an arbitrary structural payload without manufacturing an ActivityNode", () => {
    const fixture = harness();
    flushSync(() => fixture.hook.current!.onElementClick(comment));
    expect(fixture.commit).toHaveBeenCalledWith([
      expect.objectContaining({ type: "annotation", selected: true, data: { text: "A note" } })
    ], [], { createdActivities: undefined });
  });

  it("routes native element drops once and refuses unknown element identities", () => {
    const fixture = harness();
    const data = new Map<string, string>();
    const event = { dataTransfer: { setData: (type: string, value: string) => data.set(type, value), getData: (type: string) => data.get(type) ?? "" }, clientX: 240, clientY: 180, preventDefault: vi.fn(), stopPropagation: vi.fn() } as unknown as React.DragEvent<HTMLButtonElement>;
    fixture.hook.current!.onElementDragStart(event, comment);
    expect(data.get(graphElementDragDataType)).toBe(comment.id);
    flushSync(() => fixture.hook.current!.onCanvasDrop(event));
    flushSync(() => fixture.hook.current!.onElementDragEnd(event, comment));
    expect(fixture.commit).toHaveBeenCalledTimes(1);
    expect(fixture.commit.mock.calls[0][0][0].position).toEqual({ x: 240, y: 180 });
    data.set(graphElementDragDataType, "other:unknown");
    flushSync(() => fixture.hook.current!.onCanvasDrop(event));
    expect(fixture.commit).toHaveBeenCalledTimes(1);
  });

  it("supports pointer placement without a native drag and suppresses the follow-up click", () => {
    const fixture = harness();
    fixture.hook.current!.onElementPointerDown({ button: 0, clientX: 10, clientY: 10 } as React.PointerEvent<HTMLButtonElement>, comment);
    window.dispatchEvent(new MouseEvent("pointermove", { clientX: 220, clientY: 180 }));
    flushSync(() => window.dispatchEvent(new MouseEvent("pointerup", { clientX: 220, clientY: 180 })));
    flushSync(() => fixture.hook.current!.onElementClick(comment));
    expect(fixture.commit).toHaveBeenCalledTimes(1);
    expect(fixture.commit.mock.calls[0][0][0].position).toEqual({ x: 220, y: 180 });
  });

  it("cancels a pointer gesture without placing an element", () => {
    const fixture = harness();
    fixture.hook.current!.onElementPointerDown({ button: 0, clientX: 10, clientY: 10 } as React.PointerEvent<HTMLButtonElement>, comment);
    window.dispatchEvent(new MouseEvent("pointermove", { clientX: 220, clientY: 180 }));
    window.dispatchEvent(new MouseEvent("pointercancel", { clientX: 220, clientY: 180 }));
    flushSync(() => window.dispatchEvent(new MouseEvent("pointerup", { clientX: 220, clientY: 180 })));
    expect(fixture.commit).not.toHaveBeenCalled();
  });

  it("rejects stale element gestures after scope changes, and edits in read-only views", () => {
    const fixture = harness();
    fixture.render(emptyGraphElements, true);
    flushSync(() => fixture.hook.current!.onElementClick(comment));
    fixture.render([comment], false);
    flushSync(() => fixture.hook.current!.onElementClick(comment));
    expect(fixture.commit).not.toHaveBeenCalled();
  });
});

function harness() {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  mounted.push(root);
  const commit = vi.fn();
  const hook: { current: ReturnType<typeof useGraphCanvasInteractions> | null } = { current: null };
  function Harness({ elements, editable }: { elements: readonly GraphPaletteElement[]; editable: boolean }) {
    const interactions = useGraphCanvasInteractions({ nodes: [], edges: [], setNodes: vi.fn(), setEdges: vi.fn(), mode: "flowchart", scopeKey: "test", canAddActivities: editable, elements, selectedNodeId: null, catalogByVersion: new Map(), select: vi.fn(), commitCanvas: commit, placeActivity: () => null });
    hook.current = interactions;
    return <div ref={interactions.canvasRef} />;
  }
  const render = (elements: readonly GraphPaletteElement[] = [comment], editable = true) => {
    flushSync(() => root.render(<Harness elements={elements} editable={editable} />));
    vi.spyOn(container.firstElementChild!, "getBoundingClientRect").mockReturnValue({ left: 0, top: 0, right: 800, bottom: 600, width: 800, height: 600, x: 0, y: 0, toJSON: () => ({}) });
  };
  render();
  return { hook, commit, render };
}
