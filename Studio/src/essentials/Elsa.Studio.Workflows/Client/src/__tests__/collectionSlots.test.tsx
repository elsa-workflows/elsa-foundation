import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { addCollectionItem, collectionSlotItems, getCollectionSlots, removeCollectionItem, renameCollectionItem } from "../collectionSlots";
import { createActivityNode } from "../workflowAdapter";
import { CollectionSlotEditor } from "../workflow-editor/CollectionSlotEditor";
import { collectionActivity, sequenceNode, leafNode } from "./fixtures";
import type { ActivityNode } from "../workflowTypes";

const catalog = collectionActivity("switch");
const empty = () => createActivityNode(catalog, "switch");
const descriptor = getCollectionSlots(empty(), catalog)[0];
let mounted: { root: Root; element: HTMLElement } | undefined;

afterEach(() => {
  if (mounted) { flushSync(() => mounted!.root.unmount()); mounted.element.remove(); mounted = undefined; }
  vi.restoreAllMocks();
});

function mount(initial: ActivityNode, readOnly = false) {
  let current = initial;
  let replace: (value: ActivityNode) => void;
  function Harness() {
    const [value, setValue] = useState(initial);
    replace = next => { current = next; setValue(next); };
    return <CollectionSlotEditor activity={value} collection={descriptor} readOnly={readOnly} onChange={replace} renderBranch={() => <span>Branch</span>} />;
  }
  const element = document.createElement("div"); document.body.append(element);
  const root = createRoot(element); mounted = { root, element };
  flushSync(() => root.render(<Harness />));
  return { element, current: () => current, replace: (next: ActivityNode) => flushSync(() => replace(next)) };
}

function click(element: Element) { flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true }))); }
function type(input: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
function enter(input: HTMLInputElement) { flushSync(() => input.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }))); }

describe("catalog collection slots", () => {
  it.each(["switch", "parallel"] as const)("admits empty %s collections and creates unique labels", kind => {
    const item = collectionActivity(kind), node = createActivityNode(item, kind), slot = getCollectionSlots(node, item)[0];
    expect(slot).toBeDefined();
    const once = addCollectionItem(node, slot), twice = addCollectionItem(once, slot);
    expect(collectionSlotItems(twice, slot)?.map(value => value[slot.labelProperty])).toEqual(kind === "switch" ? ["Case 1", "Case 2"] : ["Branch 1", "Branch 2"]);
    expect(collectionSlotItems(node, slot)).toEqual([]);
  });

  it("renames exact strings while preserving nested activities and unknown payload fields", () => {
    const branch = sequenceNode("body", [leafNode("nested")]);
    const node = empty();
    node.structure!.payload = { cases: [{ match: "one", activity: branch, annotation: { keep: true } }], default: leafNode("fallback"), extra: [1] };
    for (const label of ["", " ", "ONE", "[$&]{match}東京"]) {
      const next = renameCollectionItem(node, descriptor, 0, "one", label);
      expect(next.structure!.payload).toEqual({ ...node.structure!.payload, cases: [{ match: label, activity: branch, annotation: { keep: true } }] });
      expect(collectionSlotItems(next, descriptor)![0].activity).toBe(branch);
    }
  });

  it("rejects exact duplicates and stale item addresses without changing the document", () => {
    const node = addCollectionItem(addCollectionItem(empty(), descriptor), descriptor);
    expect(renameCollectionItem(node, descriptor, 1, "Case 2", "Case 1")).toBe(node);
    expect(renameCollectionItem(node, descriptor, 1, "old", "new")).toBe(node);
    expect(removeCollectionItem(node, descriptor, 0, "old")).toBe(node);
    expect(removeCollectionItem(node, descriptor, 1, "Case 2").structure!.payload.default).toBeNull();
  });

  it("does not claim malformed, mismatched or multiple-child collections", () => {
    const node = empty();
    node.structure!.payload.cases = [{ match: 7, activity: null }];
    expect(getCollectionSlots(node, catalog)).toEqual([]);
    node.structure!.payload.cases = [{ match: "one", activity: { arbitrary: true } }];
    expect(getCollectionSlots(node, catalog)).toEqual([]);
    node.structure!.schemaVersion = "future";
    expect(getCollectionSlots(node, catalog)).toEqual([]);
    const multi = structuredClone(catalog);
    const facet = (multi.designFacets as { payload: { slots: Record<string, unknown>[] } }[])[0].payload;
    facet.slots.push({ ...facet.slots[0], childProperty: "compensation" });
    expect(getCollectionSlots(empty(), multi)).toEqual([]);
  });

  it.each([null, [], "opaque"])("declines a malformed structure payload %j without disrupting the inspector", payload => {
    const node = empty();
    node.structure!.payload = payload as unknown as Record<string, unknown>;
    expect(getCollectionSlots(node, catalog)).toEqual([]);
    expect(addCollectionItem(node, descriptor)).toBe(node);
  });
});

describe("rendered collection editor", () => {
  it("keeps invalid local text out of the document and commits valid exact strings", () => {
    const view = mount(addCollectionItem(addCollectionItem(empty(), descriptor), descriptor));
    const input = view.element.querySelectorAll("input")[1];
    type(input, "Case 1"); enter(input);
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(view.element.querySelector('[role="alert"]')?.textContent).toContain("already used");
    expect(collectionSlotItems(view.current(), descriptor)![1].match).toBe("Case 2");
    type(input, ""); enter(input);
    expect(collectionSlotItems(view.current(), descriptor)![1].match).toBe("");
    expect(input.getAttribute("aria-invalid")).toBeNull();
  });

  it("discards pending text when an external edit changes the item", () => {
    const view = mount(addCollectionItem(empty(), descriptor));
    const input = view.element.querySelector("input")!;
    type(input, "pending");
    view.replace(renameCollectionItem(view.current(), descriptor, 0, "Case 1", "remote"));
    expect(input.value).toBe("remote");
    enter(input);
    expect(collectionSlotItems(view.current(), descriptor)![0].match).toBe("remote");
  });

  it("confirms populated deletion, supports cancellation and removes empty cases directly", () => {
    const node = addCollectionItem(empty(), descriptor);
    collectionSlotItems(node, descriptor)![0].activity = sequenceNode("body", [leafNode("nested")]);
    const view = mount(node);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    click(view.element.querySelector('[aria-label="Remove case 1"]')!);
    expect(collectionSlotItems(view.current(), descriptor)).toHaveLength(1);
    confirm.mockReturnValue(true);
    click(view.element.querySelector('[aria-label="Remove case 1"]')!);
    expect(collectionSlotItems(view.current(), descriptor)).toHaveLength(0);
    click(view.element.querySelector("button")!);
    click(view.element.querySelector('[aria-label="Remove case 1"]')!);
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(collectionSlotItems(view.current(), descriptor)).toHaveLength(0);
  });

  it("omits mutation actions for read-only content", () => {
    const view = mount(addCollectionItem(empty(), descriptor), true);
    expect(view.element.querySelector("button")).toBeNull();
    expect(view.element.querySelector("input")!.readOnly).toBe(true);
  });
});
