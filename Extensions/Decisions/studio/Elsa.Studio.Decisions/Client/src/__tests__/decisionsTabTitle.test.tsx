import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioWorkspaceTabProvider, type StudioWorkspaceTabHandle } from "@elsa-workflows/studio-sdk";
import { DecisionsPage, type DecisionsPageProps } from "../DecisionsPage";
import { asset, button, click, render, stubApi, type } from "./helpers";
const disposals: Array<() => void> = [];
afterEach(() => { disposals.splice(0).forEach(dispose => dispose()); });
function mountInTab(props: Omit<DecisionsPageProps, "navigate">) {
  const setTitle = vi.fn(), navigate = vi.fn();
  const tab: StudioWorkspaceTabHandle = { id: "decisions-tab", active: true, location: "/decisions", isActive: () => true, markPendingWork: vi.fn(), setTitle };
  const page = (next: Omit<DecisionsPageProps, "navigate">) => <StudioWorkspaceTabProvider value={tab}><DecisionsPage {...next} navigate={navigate} /></StudioWorkspaceTabProvider>;
  const result = render(page(props)); disposals.push(result.dispose);
  return { ...result, setTitle, navigate, update: (next: Omit<DecisionsPageProps, "navigate">) => result.update(page(next)) };
}
describe("Decisions workspace tab titles", () => {
  it("names the list tab Decisions", async () => {
    const { host, setTitle } = mountInTab({ api: stubApi() });
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    expect(setTitle).toHaveBeenLastCalledWith({ title: "Decisions", detail: undefined });
  });
  it("names an unsaved decision New decision, then the saved decision's name once it is opened", async () => {
    const api = stubApi(), { host, setTitle, navigate, update } = mountInTab({ api, isNew: true });
    api.backend.http.postJson.mockResolvedValue({ ...asset, name: "Purchase approval" } as never);
    await vi.waitFor(() => expect(host.querySelector('input[aria-label="Input 1 name"]')).not.toBeNull());
    expect(setTitle).toHaveBeenLastCalledWith({ title: "New decision", detail: undefined });
    const name = Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.parentElement?.textContent === "Name")!;
    type(name, "Purchase approval"); expect(setTitle).toHaveBeenLastCalledWith({ title: "New decision", detail: undefined });
    click(button(host, "Save decision"));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/decisions?decision=approval"));
    api.backend.http.getJson.mockImplementation(async (path: string) => path.endsWith("/versions") || path.endsWith("/cases") || path.endsWith("/runs") ? { items: [] } : { ...asset, name: "Purchase approval" });
    update({ api, assetId: asset.id });
    await vi.waitFor(() => expect(setTitle).toHaveBeenLastCalledWith({ title: "Purchase approval", detail: "Draft" }));
  });
  it("names an opened draft after its saved name, not the unsaved name being typed", async () => {
    const { host, setTitle } = mountInTab({ api: stubApi(), assetId: asset.id });
    await vi.waitFor(() => expect(setTitle).toHaveBeenLastCalledWith({ title: "Approval", detail: "Draft" }));
    expect(setTitle).not.toHaveBeenCalledWith(expect.objectContaining({ title: "Decisions" }));
    const name = Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.parentElement?.textContent === "Name")!;
    type(name, "Renamed approval"); expect(setTitle).toHaveBeenLastCalledWith({ title: "Approval", detail: "Draft" });
  });
  it("leaves the tab unnamed while a decision opened from the list in the same tab is still loading", async () => {
    const api = stubApi(), { host, setTitle, update } = mountInTab({ api });
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    let release: (value: unknown) => void = () => undefined;
    api.backend.http.getJson.mockImplementation(async (path: string) => path.endsWith("/versions") || path.endsWith("/cases") || path.endsWith("/runs") ? { items: [] } : new Promise(resolve => { release = resolve; }));
    setTitle.mockClear(); update({ api, assetId: asset.id });
    await vi.waitFor(() => expect(host.querySelector('[aria-label="Loading decisions"]')).not.toBeNull());
    expect(setTitle).not.toHaveBeenCalled();
    release(asset);
    await vi.waitFor(() => expect(setTitle).toHaveBeenLastCalledWith({ title: "Approval", detail: "Draft" }));
    expect(setTitle).toHaveBeenCalledTimes(1);
  });
  it("names a published version after its decision with the version number as detail", async () => {
    const api = stubApi(), published = { id: "v1", assetId: asset.id, number: 3, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async (path: string) => path.endsWith("/versions") ? { items: [published] } : path.endsWith("/cases") || path.endsWith("/runs") ? { items: [] } : path.endsWith("/versions/v1") ? published : asset);
    const { setTitle } = mountInTab({ api, assetId: asset.id, versionId: "v1" });
    await vi.waitFor(() => expect(setTitle).toHaveBeenLastCalledWith({ title: "Approval", detail: "Version 3" }));
  });
  it("names a decision that could not be loaded Decision rather than the list", async () => {
    const api = stubApi(); api.backend.http.getJson.mockRejectedValue(new Error("Forbidden") as never);
    const { host, setTitle } = mountInTab({ api, assetId: asset.id });
    await vi.waitFor(() => expect(host.textContent).toContain("Decision unavailable"));
    expect(setTitle).toHaveBeenLastCalledWith({ title: "Decision", detail: undefined });
  });
});
