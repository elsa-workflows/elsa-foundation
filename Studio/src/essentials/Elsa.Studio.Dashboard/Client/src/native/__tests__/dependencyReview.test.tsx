import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DashboardApi } from "../api";
import { DashboardDependencyReview, ShareDialog } from "../ResourceDialogs";
import type { DashboardResource } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";

const question = (id = "q1"): DashboardResource => ({ id, kind: "question", revision: "question-r1", name: `Question ${id}`, description: "", ownerId: "owner", capabilities: { canRead: true, canEdit: false, canShare: false, canDelete: false, canCopy: true }, definitionState: "available", createdAt: "", updatedAt: "" });
const dashboard = (): DashboardResource => ({ ...question("dashboard"), kind: "dashboard", revision: "dashboard-r1", name: "Operations", capabilities: { canRead: true, canEdit: true, canShare: true, canDelete: true, canCopy: true }, tiles: [{ id: "one", questionId: "q1", size: "medium", referenceState: "available" }, { id: "two", questionId: "q1", size: "wide", referenceState: "available" }, { id: "hidden-tile", questionId: "DO-NOT-DISCLOSE", size: "medium", referenceState: "unavailable" }] });
const cleanups: (() => void)[] = [];
function mount(children: React.ReactNode) {
  const view = document.createElement("div"); document.body.append(view); const root = createRoot(view);
  flushSync(() => root.render(children)); cleanups.push(() => { flushSync(() => root.unmount()); view.remove(); }); return view;
}
function action(view: HTMLElement, name: string) {
  const button = [...view.querySelectorAll("button")].find(item => item.textContent?.trim() === name);
  expect(button, `Action ${name}`).toBeDefined(); return button!;
}
function click(view: HTMLElement, name: string) { flushSync(() => action(view, name).click()); }
function edit(input: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}
afterEach(() => { cleanups.splice(0).forEach(cleanup => cleanup()); vi.restoreAllMocks(); });

describe("current dashboard dependency review", () => {
  it("groups duplicate question references and reviews owner-only audiences without disclosing unavailable identities", async () => {
    const api = new DashboardApi(stubApi().backend); const ownerQuestion = { ...question(), capabilities: { ...question().capabilities, canShare: true } };
    const get = vi.spyOn(api, "get").mockImplementation(async (kind) => kind === "dashboard" ? dashboard() : ownerQuestion);
    const audience = vi.spyOn(api, "audience").mockResolvedValue({ items: [{ kind: "role", id: "allowed-owner-only-role", access: "viewer" }] });
    const ready = vi.fn(); const view = mount(<DashboardDependencyReview api={api} resource={dashboard()} mode="share" onReadyChange={ready} />);
    await waitUntil(() => ready.mock.calls.some(call => call[0] === true));
    expect(get.mock.calls.filter(call => call[0] === "question")).toHaveLength(1);
    expect(audience).toHaveBeenCalledOnce(); expect(audience.mock.calls[0][0].id).toBe("q1");
    expect(view.textContent).toContain("Question q1 — Tiles 1, 2"); expect(view.textContent).toContain("0 user grants and 1 role grants");
    expect(view.textContent).toContain("Unavailable question — Tile 3"); expect(view.innerHTML).not.toContain("DO-NOT-DISCLOSE"); expect(view.textContent).not.toContain("allowed-owner-only-role");
    expect(view.querySelector('a[href="/questions/q1"]')?.textContent).toContain("review sharing");
    expect(view.textContent).toContain("does not establish a recipient's data permissions");
  });

  it("blocks dashboard sharing until a failed dependency review is retried successfully", async () => {
    const api = new DashboardApi(stubApi().backend);
    vi.spyOn(api, "audience").mockResolvedValue({ items: [] });
    const get = vi.spyOn(api, "get").mockRejectedValueOnce(new Error("PRIVATE-SOURCE-DETAIL")).mockImplementation(async kind => kind === "dashboard" ? dashboard() : question());
    const share = vi.spyOn(api, "share").mockResolvedValue(dashboard()); const accepted = vi.fn();
    const view = mount(<ShareDialog api={api} resource={dashboard()} onClose={vi.fn()} onSaved={accepted} />);
    await waitUntil(() => view.textContent?.includes("Retry included question review") === true);
    expect(action(view, "Save audience").disabled).toBe(true); expect(view.textContent).not.toContain("PRIVATE-SOURCE-DETAIL");
    click(view, "Save audience"); expect(share).not.toHaveBeenCalled();
    click(view, "Retry included question review"); await waitUntil(() => action(view, "Save audience").disabled === false);
    expect(get.mock.calls.filter(call => call[0] === "dashboard")).toHaveLength(2);
    click(view, "Save audience"); await waitUntil(() => accepted.mock.calls.length === 1); expect(share).toHaveBeenCalledOnce();
  });

  it("creates a separate keyed question copy without changing dashboard bindings or reading a non-owner audience", async () => {
    const api = new DashboardApi(stubApi().backend); const saved = dashboard();
    vi.spyOn(api, "get").mockImplementation(async kind => kind === "dashboard" ? saved : question());
    const audience = vi.spyOn(api, "audience"); const save = vi.spyOn(api, "save"); const share = vi.spyOn(api, "share");
    const copy = vi.spyOn(api, "copy").mockRejectedValueOnce(new Error("Response lost")).mockResolvedValueOnce({ ...question("private"), name: "Independent operations" });
    const ready = vi.fn(); const view = mount(<DashboardDependencyReview api={api} resource={saved} mode="copy" onReadyChange={ready} />);
    await waitUntil(() => ready.mock.calls.some(call => call[0] === true)); expect(audience).not.toHaveBeenCalled();
    click(view, "Copy Question q1 as an independent question"); edit(view.querySelector("input")!, "Independent operations");
    click(view, "Create independent private question"); await waitUntil(() => view.textContent?.includes("The question copy could not be confirmed") === true);
    click(view, "Create independent private question"); await waitUntil(() => view.textContent?.includes("Private question created") === true);
    expect(copy).toHaveBeenCalledTimes(2); expect(copy.mock.calls[0][0]).toMatchObject({ id: "q1", revision: "question-r1" });
    expect(copy.mock.calls[0][2]).toBe(copy.mock.calls[1][2]); expect(copy.mock.calls[1][1]).toBe("Independent operations");
    expect(view.querySelector('a[href="/questions/private"]')).not.toBeNull(); expect(view.textContent).toContain("still references the original question");
    expect(saved.tiles?.map(tile => tile.questionId)).toEqual(["q1", "q1", "DO-NOT-DISCLOSE"]); expect(save).not.toHaveBeenCalled(); expect(share).not.toHaveBeenCalled();
  });

  it("keeps changed dashboard revisions blocked and makes newly denied questions generic placeholders", async () => {
    const api = new DashboardApi(stubApi().backend); const saved = dashboard(); const ready = vi.fn();
    const get = vi.spyOn(api, "get").mockResolvedValueOnce({ ...saved, revision: "changed" }).mockImplementation(async kind => {
      if (kind === "dashboard") return saved;
      throw Object.assign(new Error("Protected question identity"), { status: 404 });
    });
    const view = mount(<DashboardDependencyReview api={api} resource={saved} mode="copy" onReadyChange={ready} />);
    await waitUntil(() => view.textContent?.includes("Retry included question review") === true); expect(ready.mock.calls.some(call => call[0] === true)).toBe(false);
    click(view, "Retry included question review"); await waitUntil(() => ready.mock.calls.some(call => call[0] === true));
    expect(get.mock.calls.filter(call => call[0] === "question")).toHaveLength(1); expect(view.textContent).not.toContain("Question q1"); expect(view.innerHTML).not.toContain("Protected question identity");
    expect(view.querySelectorAll(".named-picker-list li")).toHaveLength(2); expect(view.textContent).toContain("Tiles 1, 2");
  });
});
