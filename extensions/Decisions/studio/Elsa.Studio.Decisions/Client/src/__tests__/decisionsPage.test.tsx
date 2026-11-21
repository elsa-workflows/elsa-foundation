import React from "react";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DecisionsPage } from "../DecisionsPage";
import { CasePanel } from "../CasePanel";
import { DecisionDiagnostics } from "../DecisionDiagnostics";
import { DecisionVersionEditor } from "../DecisionVersionEditor";
import { EvaluationPanel } from "../EvaluationPanel";
import { createNotificationController, studioNavigationRequestedEvent, studioWorkspaceTabRequestedEvent, type StudioWorkspaceTabRequest } from "@elsa-workflows/studio-sdk";
import { editTable, newDecisionXml, parseTable } from "../decisionXml";
import { decisionFailure, failureMessage } from "../decisionsApi";
import { asset, button, click, render, select, stubApi, type } from "./helpers";
const disposals: Array<() => void> = [];
afterEach(() => { disposals.splice(0).forEach(dispose => dispose()); });
function mount(content: React.ReactNode) { const result = render(content); disposals.push(result.dispose); return result; }
function gridCell(host: HTMLElement, label: string) { return Array.from(host.querySelectorAll<HTMLElement>('[role="gridcell"]')).find(cell => cell.getAttribute("aria-label") === label)!; }
function decisionMenuItem(host: HTMLElement, label: string) {
  const trigger = host.querySelector<HTMLButtonElement>('button[aria-label="More decision actions"]')!;
  if (trigger.getAttribute("aria-expanded") !== "true") click(trigger);
  const menu = document.getElementById(trigger.getAttribute("aria-controls")!)!;
  return Array.from(menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')).find(item => item.textContent === label)!;
}
function decisionAction(host: HTMLElement, label: string) { click(label === "Publish" ? button(host, label) : decisionMenuItem(host, label)); }
function collectionMenuItem(host: HTMLElement, name: string, label: string) {
  const trigger = host.querySelector<HTMLButtonElement>(`button[aria-label="Actions for ${name}"]`)!;
  if (trigger.getAttribute("aria-expanded") !== "true") click(trigger);
  const menu = document.getElementById(trigger.getAttribute("aria-controls")!)!;
  return Array.from(menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')).find(item => item.textContent === label)!;
}
function caseMenuItem(host: HTMLElement, name: string, label: string) {
  const trigger = host.querySelector<HTMLButtonElement>(`button[aria-label="Actions for ${name}"]`)!;
  if (trigger.getAttribute("aria-expanded") !== "true") click(trigger);
  const menu = document.getElementById(trigger.getAttribute("aria-controls")!)!;
  return Array.from(menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')).find(item => item.textContent === label)!;
}
function editGridCell(host: HTMLElement, label: string, value: string) {
  const cell = gridCell(host, label); click(cell);
  flushSync(() => cell.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true, cancelable: true })));
  const editor = host.querySelector<HTMLInputElement>('[aria-label="Edit selected cell"]')!; type(editor, value);
  flushSync(() => editor.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true, cancelable: true })));
  return gridCell(host, label);
}
describe("Decisions Studio journeys", () => {
  it("does not redirect entry from Description when delayed initial focus finally runs", async () => {
    const frames: FrameRequestCallback[] = [];
    const animation = vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => frames.push(callback));
    try {
      const api = stubApi(), { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} isNew />);
      await vi.waitFor(() => expect(host.querySelector(".decision-metadata input")).not.toBeNull());
      await vi.waitFor(() => expect(frames).toHaveLength(1));
      const inputs = Array.from(host.querySelectorAll<HTMLInputElement>(".decision-metadata input"));
      const name = inputs.find(input => input.labels?.[0]?.textContent === "Name")!;
      const description = inputs.find(input => input.labels?.[0]?.textContent === "Description")!;
      type(name, "Approval policy"); description.focus();
      frames[0](0);
      expect(document.activeElement).toBe(description);
      type(description, "Determine purchase approval requirements.");
      api.backend.http.postJson.mockResolvedValue(asset as never);
      click(button(host, "Save"));
      await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions", expect.objectContaining({
        name: "Approval policy", description: "Determine purchase approval requirements."
      })));
    } finally { animation.mockRestore(); }
  });

  it("keeps initial keyboard focus when the author has not selected another control", async () => {
    const frames: FrameRequestCallback[] = [];
    const animation = vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => frames.push(callback));
    try {
      const { host } = mount(<DecisionsPage api={stubApi()} navigate={vi.fn()} isNew />);
      await vi.waitFor(() => expect(host.querySelector(".decision-metadata input")).not.toBeNull());
      expect(host.querySelector("h1#decision-title")?.textContent).toBe("New decision");
      expect(button(host, "Publish").disabled).toBe(true);
      await vi.waitFor(() => expect(frames).toHaveLength(1));
      frames[0](0);
      expect(document.activeElement).toBe(Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Name"));
    } finally { animation.mockRestore(); }
  });

  it("uses a breadcrumb to return from a decision and protects dirty edits", async () => {
    const api = stubApi(), navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    const breadcrumb = host.querySelector<HTMLElement>('nav[aria-label="Breadcrumb"]')!;
    const title = host.querySelector<HTMLHeadingElement>("h1#decision-title")!;
    const current = breadcrumb.querySelector<HTMLElement>('[aria-current="page"]')!;
    const link = breadcrumb.querySelector<HTMLAnchorElement>('a[href="/decisions"]')!;
    expect(title.textContent).toBe("Approval");
    expect(current.contains(title)).toBe(true);
    expect(link.textContent).toBe("Decisions");
    await vi.waitFor(() => expect(document.activeElement).toBe(title));

    let modifiedClickPrevented = true;
    const stopJSDOMNavigation = (event: MouseEvent) => {
      if (event.target === link) { modifiedClickPrevented = event.defaultPrevented; event.preventDefault(); }
    };
    document.addEventListener("click", stopJSDOMNavigation);
    const modifiedClick = new MouseEvent("click", { bubbles: true, cancelable: true, ctrlKey: true });
    link.dispatchEvent(modifiedClick);
    document.removeEventListener("click", stopJSDOMNavigation);
    expect(modifiedClickPrevented).toBe(false);
    expect(navigate).not.toHaveBeenCalled();

    click(host.querySelector<HTMLButtonElement>('[role="tab"][data-tab-id="settings"]')!);
    type(Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Description")!, "Unsaved description");
    api.dialogs.confirm.mockResolvedValue(false);
    click(link);
    await vi.waitFor(() => expect(api.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Unsaved decision", confirmLabel: "Discard changes" })));
    expect(navigate).not.toHaveBeenCalled();

    api.dialogs.confirm.mockResolvedValue(true);
    click(link);
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/decisions"));
  });

  it("blocks breadcrumb navigation while a decision save is pending", async () => {
    const api = stubApi(), navigate = vi.fn(); let complete!: (result: unknown) => void;
    api.backend.http.putJson.mockImplementation(() => new Promise(resolve => { complete = resolve; }) as never);
    const { host } = mount(<DecisionsPage api={api} navigate={navigate} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    click(host.querySelector<HTMLButtonElement>('[role="tab"][data-tab-id="settings"]')!);
    type(Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Description")!, "Pending save");
    click(button(host, "Save"));
    await vi.waitFor(() => expect(api.backend.http.putJson).toHaveBeenCalledOnce());
    const link = host.querySelector<HTMLAnchorElement>('nav[aria-label="Breadcrumb"] a[href="/decisions"]')!;
    expect(link.getAttribute("aria-disabled")).toBe("true");
    click(link);
    expect(navigate).not.toHaveBeenCalled();
    expect(api.dialogs.confirm).not.toHaveBeenCalled();
    complete({ ...asset, description: "Pending save", revision: "r2" });
    await vi.waitFor(() => expect(api.notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Decision saved" }));
    await vi.waitFor(() => expect(link.getAttribute("aria-disabled")).not.toBe("true"));
    expect(navigate).not.toHaveBeenCalled();
  });

  it("keeps a neutral breadcrumb identity while a decision loads or is unavailable", async () => {
    const api = stubApi();
    api.backend.http.getJson.mockRejectedValue(new Error("Decision unavailable"));
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId="missing" />);
    const breadcrumb = host.querySelector<HTMLElement>('nav[aria-label="Breadcrumb"]')!;
    expect(breadcrumb.querySelector('[aria-current="page"]')?.textContent).toContain("Loading decision");
    expect(host.textContent).not.toContain("Approval");
    await vi.waitFor(() => expect(host.querySelector("h1#decision-title")?.textContent).toBe("Decision unavailable"));
    expect(breadcrumb.querySelector('a[href="/decisions"]')?.textContent).toBe("Decisions");
    expect(host.textContent).toContain("The resource could not be loaded");
  });

  it("retires a pending new-decision focus callback when the route changes", async () => {
    const frames: FrameRequestCallback[] = [];
    const animation = vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => frames.push(callback));
    try {
      const api = stubApi(), { host, update } = mount(<DecisionsPage api={api} navigate={vi.fn()} isNew />);
      await vi.waitFor(() => expect(frames).toHaveLength(1));
      update(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
      expect(host.querySelector("h1#decision-title")?.textContent).toBe("Loading decision");
      await vi.waitFor(() => expect(host.querySelector<HTMLInputElement>(".decision-metadata input")?.value).toBe(asset.name));
      await vi.waitFor(() => expect(frames).toHaveLength(2));
      const prior = document.activeElement;
      frames[0](0);
      expect(document.activeElement).toBe(prior);
      frames[1](0);
      expect(document.activeElement).toBe(host.querySelector("h1#decision-title[tabindex]"));
    } finally { animation.mockRestore(); }
  });

  it("enables draft actions after a create navigates within the mounted editor", async () => {
    const api = stubApi(); api.backend.http.postJson.mockResolvedValue(asset as never);
    const navigate = (path: string) => update(<DecisionsPage api={api} navigate={navigate} assetId={new URL(path, "https://studio.test").searchParams.get("decision") ?? undefined} />);
    const { host, update } = mount(<DecisionsPage api={api} navigate={navigate} isNew />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    type(Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Name")!, asset.name);
    click(button(host, "Save"));
    await vi.waitFor(() => expect(host.querySelector(".decision-draft-bar")?.textContent).toContain("Saved draft"));
    expect(decisionMenuItem(host, "Validate").disabled).toBe(false);
    expect(button(host, "Publish").disabled).toBe(false);
    api.backend.http.postJson.mockResolvedValue({ success: true, diagnostics: [] } as never);
    decisionAction(host, "Validate");
    await vi.waitFor(() => expect(host.textContent).toContain("The saved draft is valid"));
    expect(api.notifications.notify).toHaveBeenCalledOnce();
  });

  it("distinguishes a populated collection, an unmatched search and an empty collection", async () => {
    const api = stubApi(), navigate = vi.fn(), { host, update } = mount(<DecisionsPage api={api} navigate={navigate} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    type(host.querySelector<HTMLInputElement>('[aria-label="Search decisions"]')!, "unknown");
    expect(host.textContent).toContain("No matching decisions");
    click(button(host, "Clear search")); expect(host.textContent).toContain("Purchase approval");
    const empty = stubApi(); empty.backend.http.getJson.mockResolvedValue({ items: [] } as never); update(<DecisionsPage api={empty} navigate={navigate} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No decisions yet"));
    expect(host.querySelector(".studio-collection-empty")?.textContent).toContain("New decision");
  });
  it("keeps decision collection errors distinct and does not offer create to read-only users", async () => {
    const readOnly = stubApi(["read"]); readOnly.backend.http.getJson.mockResolvedValue({ items: [] } as never);
    const view = mount(<DecisionsPage api={readOnly} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(view.host.textContent).toContain("No decisions yet"));
    expect(view.host.querySelector(".studio-collection-empty button")).toBeNull();

    const failed = stubApi(); failed.backend.http.getJson.mockRejectedValue(new Error("Decision collection unavailable."));
    const failedView = mount(<DecisionsPage api={failed} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(failedView.host.querySelector('[role="alert"]')).not.toBeNull());
    expect(failedView.host.querySelector(".studio-collection-empty")).toBeNull();
  });
  it("recycles a decision from its collection row menu and refreshes the active list", async () => {
    const api = stubApi(), navigate = vi.fn(); let recycled = false;
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/decisions") ? { items: recycled ? [] : [asset] } : path.endsWith("/recycle-bin") || path.endsWith("/versions") ? { items: [] } : asset as never);
    api.backend.http.postJson.mockImplementation(async path => { if (path.endsWith("/recycle")) recycled = true; return { id: asset.id, revision: "r2" } as never; });
    const { host } = mount(<DecisionsPage api={api} navigate={navigate} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    click(collectionMenuItem(host, "Approval", "Delete"));
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalledWith(
      expect.stringContaining("/decisions/approval/recycle"), expect.objectContaining({ expectedRevision: asset.revision, operationId: expect.any(String) })
    ));
    expect(api.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Move decision to recycle bin", confirmLabel: "Move to recycle bin", tone: "danger" }));
    await vi.waitFor(() => expect(host.textContent).toContain("Approval was moved to the recycle bin."));
    expect(host.querySelector("tbody tr")).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
    expect(api.notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Decision moved to recycle bin" });
  });
  it("keeps row deletion cancellable and disabled for read-only principals", async () => {
    const api = stubApi(), { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    api.dialogs.confirm.mockResolvedValue(false);
    click(collectionMenuItem(host, "Approval", "Delete"));
    await vi.waitFor(() => expect(api.dialogs.confirm).toHaveBeenCalled());
    expect(api.backend.http.postJson).not.toHaveBeenCalled();
    expect(host.querySelector("tbody tr")).not.toBeNull();

    const readOnly = stubApi(["read"]), view = mount(<DecisionsPage api={readOnly} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(view.host.textContent).toContain("Purchase approval"));
    expect(collectionMenuItem(view.host, "Approval", "Delete").disabled).toBe(true);
  });
  it("ignores a row-delete confirmation after leaving the active collection", async () => {
    const api = stubApi(); let confirmDelete!: (confirmed: boolean) => void;
    api.dialogs.confirm.mockImplementation(() => new Promise(resolve => { confirmDelete = resolve; }));
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    click(collectionMenuItem(host, "Approval", "Delete"));
    await vi.waitFor(() => expect(api.dialogs.confirm).toHaveBeenCalled());
    click(button(host, "Recycle bin"));
    await vi.waitFor(() => expect(host.textContent).toContain("Recycle bin is empty"));
    confirmDelete(true);
    await Promise.resolve();
    expect(api.backend.http.postJson).not.toHaveBeenCalled();
  });
  it("keeps a conflicted row delete in the collection and offers collection refresh", async () => {
    const api = stubApi(), conflict = Object.assign(new Error("The decision changed. Refresh and try again."), { status: 409 });
    api.backend.http.postJson.mockRejectedValue(conflict);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    click(collectionMenuItem(host, "Approval", "Delete"));
    await vi.waitFor(() => expect(host.textContent).toContain("The decision changed. Refresh and try again."));
    expect(host.querySelector("tbody tr")).not.toBeNull();
    expect(host.querySelector(".decision-conflict")).toBeNull();
    expect(button(host, "Refresh").disabled).toBe(false);
  });
  it("keeps one rules editor mounted while switching between Rules, Tests and Settings", async () => {
    const api = stubApi(), { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    const tabs = Array.from(host.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
    expect(tabs.map(tab => tab.textContent)).toEqual(["Rules", "Tests", "Settings"]);
    for (const [index, tab] of tabs.entries()) {
      click(tab);
      expect(tab.getAttribute("aria-selected")).toBe("true");
      expect(host.querySelectorAll('[role="grid"]')).toHaveLength(1);
      expect(host.querySelectorAll(".decision-table-section")).toHaveLength(1);
      const rulesPanelId = tabs[0].getAttribute("aria-controls")!;
      const rules = host.querySelector<HTMLElement>(`[id="${rulesPanelId}"]`)!;
      expect(rules.hidden).toBe(index !== 0);
    }
  });
  it("offers decision recycling in Settings with a revision-checked confirmation", async () => {
    const api = stubApi(), { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    click(host.querySelector<HTMLButtonElement>('[role="tab"][data-tab-id="settings"]')!);
    click(button(host, "Move to recycle bin"));
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalledWith(
      expect.stringContaining("/decisions/approval/recycle"), expect.objectContaining({ expectedRevision: asset.revision, operationId: expect.any(String) })
    ));
    expect(api.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Move decision to recycle bin", confirmLabel: "Move to recycle bin" }));
  });
  it("restores a recycled decision from the recycle bin", async () => {
    const api = stubApi(), recycled = { ...asset, deletedAt: "2026-11-20T12:00:00Z" };
    api.backend.http.getJson.mockImplementation(async (path: string) => path.endsWith("/recycle-bin") ? { items: [recycled] } : path.endsWith("/decisions") ? { items: [asset] } : asset as never);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    click(button(host, "Recycle bin"));
    await vi.waitFor(() => expect(host.textContent).toContain("Recycled"));
    click(button(host, "Restore"));
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalledWith(
      expect.stringContaining("/decisions/approval/restore"), expect.objectContaining({ expectedRevision: asset.revision, operationId: expect.any(String) })
    ));
    expect(host.textContent).toContain("restored as an unpublished decision");
  });
  it("requires a destructive confirmation before permanently purging a recycled decision", async () => {
    const api = stubApi(), recycled = { ...asset, deletedAt: "2026-11-20T12:00:00Z" };
    api.backend.http.getJson.mockImplementation(async (path: string) => path.endsWith("/recycle-bin") ? { items: [recycled] } : { items: [] });
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No decisions yet"));
    click(button(host, "Recycle bin"));
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    click(button(host, "Delete permanently"));
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalledWith(
      expect.stringContaining("/decisions/approval/purge"), expect.objectContaining({ expectedRevision: asset.revision, operationId: expect.any(String) })
    ));
    expect(api.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Permanently delete decision", tone: "danger", confirmLabel: "Delete permanently" }));
  });
  it("creates a decision through the authenticated API and navigates after the write settles", async () => {
    const api = stubApi(), navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} isNew />);
    api.backend.http.postJson.mockResolvedValue(asset as never);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    expect(host.querySelector(".decision-draft-bar")?.textContent).toContain("Unsaved decision"); expect(host.querySelector(".decision-draft-bar")?.textContent).not.toContain("Saved draft");
    const name = Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Name")!;
    type(name, "Purchase approval"); click(button(host, "Save"));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/decisions?decision=approval"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions", expect.objectContaining({ name: "Purchase approval", xml: expect.stringContaining("decisionTable") }));
    expect(api.notifications.notify).toHaveBeenCalledOnce(); expect(api.notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Decision saved" });
    expect(host.textContent).not.toContain("Decision saved");
  });
  it.each(["description", "sensitivity"])("guards a new decision's %s-only edit before its name is entered", async field => {
    const api = stubApi(), navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} isNew />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    if (field === "description") type(Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Description")!, "Only unsaved metadata");
    else click(host.querySelector<HTMLInputElement>('.decision-metadata input[type="checkbox"]')!);
    const probe = new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?new=1", toPath: "/workflows", intent: "probe" } }); window.dispatchEvent(probe); expect(probe.defaultPrevented).toBe(true);
    api.dialogs.confirm.mockResolvedValue(false); click(host.querySelector<HTMLAnchorElement>('nav[aria-label="Breadcrumb"] a[href="/decisions"]')!);
    await vi.waitFor(() => expect(api.dialogs.confirm).toHaveBeenCalled()); expect(navigate).not.toHaveBeenCalled();
    if (field === "description") expect(Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Description")!.value).toBe("Only unsaved metadata");
    else expect(host.querySelector<HTMLInputElement>('.decision-metadata input[type="checkbox"]')!.checked).toBe(true);
  });
  it("preserves unsaved table work after a conflicting save and supports explicit reload", async () => {
    const api = stubApi(), { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    editGridCell(host, "Rule 1, input amount", "<= 1000");
    expect(gridCell(host, "Rule 1, input amount").textContent).toBe("<= 1000");
    const failure = Object.assign(new Error("The draft changed."), { status: 409 }); api.backend.http.putJson.mockRejectedValue(failure as never);
    click(button(host, "Save"));
    await vi.waitFor(() => expect(host.textContent).toContain("Your unsaved work is still here"));
    expect(gridCell(host, "Rule 1, input amount").textContent).toBe("<= 1000");
    click(button(host, "Reload server draft"));
    await vi.waitFor(() => expect(gridCell(host, "Rule 1, input amount").textContent).toBe("-"));
  });
  it("does not render protected source or enable writes for a read-only principal", async () => {
    const api = stubApi(["read"]); api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [] } : { ...asset, xml: null } as never);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Decision source is protected"));
    expect(button(host, "Save").disabled).toBe(true); expect(button(host, "Publish").disabled).toBe(true); expect(decisionMenuItem(host, "Export DMN").disabled).toBe(true);
    expect(host.querySelector(".decision-table")).toBeNull();
  });
  it("allows Test+Read without Source to run cases while keeping source and protected values unavailable", async () => {
    const api = stubApi(["read", "test"]), saved = { id: "case1", name: "Boundary", revision: "c1", inputs: null, expectedStatus: "Success", expectedResult: null, isSensitive: true };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [] } : path.endsWith("/cases") ? { items: [saved] } : { ...asset, xml: null });
    api.backend.http.postJson.mockResolvedValue({ id: "run1", candidateId: "draft-r1", candidateHash: "hash1", passed: true, hasDetailedPayload: false, comparisons: [{ caseId: "case1", name: "Boundary", passed: true, status: "Success", expected: null, actual: null, matchedRules: [], selectedRules: [], trace: [] }], completedAt: asset.createdAt, evaluatedAt: asset.createdAt } as never);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Protected inputs are unavailable"));
    expect(button(host, "Add case").disabled).toBe(true); expect(caseMenuItem(host, "Boundary", "Edit").disabled).toBe(true); expect(caseMenuItem(host, "Boundary", "Delete").disabled).toBe(true); expect(button(host, "Run saved cases").disabled).toBe(false);
    click(button(host, "Run saved cases")); await vi.waitFor(() => expect(host.textContent).toContain("All cases passed"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/test", expect.objectContaining({ candidateRevision: "r1", candidateHash: "hash1", cases: [{ id: "case1", revision: "c1" }] }));
    expect(host.textContent).toContain("Detailed values were not retained or are unavailable with your permissions");
    expect(host.querySelector(".decision-table")).toBeNull(); expect(host.querySelector(".decision-value-field")).toBeNull(); expect(host.textContent).not.toContain("Expected:"); expect(host.textContent).not.toContain("Actual:");
  });
  it("edits a saved case from its compact menu and restores focus to the row trigger", async () => {
    const api = stubApi(), saved = { id: "case1", name: "Boundary", revision: "c1", inputs: { amount: { kind: "number" as const, value: "20" }, customerCategory: { kind: "string" as const, value: "Child" } }, expectedStatus: "Success", expectedResult: { kind: "string" as const, value: "Manual review" } };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: [saved] } : { items: [] });
    api.backend.http.putJson.mockResolvedValue(saved as never);
    const { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Boundary"));
    const trigger = host.querySelector<HTMLButtonElement>('button[aria-label="Actions for Boundary"]')!;
    expect(trigger).toBeTruthy();
    expect(host.querySelector('button[aria-label="Edit Boundary"]')).toBeNull();
    expect(host.querySelector('button[aria-label="Delete Boundary"]')).toBeNull();
    click(caseMenuItem(host, "Boundary", "Edit"));
    await vi.waitFor(() => expect(host.querySelector("form[aria-label='Edit decision case']")).not.toBeNull());
    expect(host.querySelector('[role="dialog"][aria-modal="true"]')).not.toBeNull();
    expect(document.activeElement).toBe(host.querySelector('input[data-dialog-initial-focus]'));
    click(button(host, "Cancel"));
    expect(document.activeElement).toBe(trigger);

    click(caseMenuItem(host, "Boundary", "Edit"));
    await vi.waitFor(() => expect(host.querySelector("form[aria-label='Edit decision case']")).not.toBeNull());
    type(host.querySelector<HTMLInputElement>("form input")!, "Updated boundary");
    click(button(host, "Save case"));
    await vi.waitFor(() => expect(host.querySelector("form")).toBeNull());
    expect(api.backend.http.putJson).toHaveBeenCalledWith("/tenant/decisions/approval/cases/case1", expect.objectContaining({ expectedRevision: "c1", name: "Updated boundary" }));
    expect(document.activeElement).toBe(trigger);
  });
  it("keeps case edits in a modal and blocks conflicting actions until cancellation", async () => {
    const api = stubApi();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: [] } : path.endsWith("/versions") ? { items: [] } : asset);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    click(button(host, "Tests"));
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet"));
    click(button(host, "Add case"));
    const name = host.querySelector<HTMLInputElement>('form[aria-label="New decision case"] input')!;
    type(name, "Draft retained across views");
    expect(host.querySelector('[role="dialog"][aria-modal="true"]')).not.toBeNull();
    expect(host.textContent).not.toContain("Unsaved case changes");
    expect(button(host, "Add case").disabled).toBe(true);
    expect(button(host, "Run saved cases").disabled).toBe(true);
    name.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(host.querySelector('[role="dialog"]')).toBeNull());
    expect(button(host, "Add case")).toBe(document.activeElement);
    click(button(host, "Rules")); click(button(host, "Tests"));
    expect(host.querySelector('form[aria-label="New decision case"]')).toBeNull();
  });
  it("keeps each case input on its own row and saves exact scalar values", async () => {
    let xml = editTable(newDecisionXml(), { kind: "add-clause", output: false });
    const addedInput = parseTable(xml).inputs[1];
    xml = editTable(xml, { kind: "clause", id: addedInput.id, output: false, name: "eligible", type: "boolean" });
    const api = stubApi(), number = "1234567890123456789012345678901234";
    let savedCase: Record<string, unknown> | null = null;
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: savedCase ? [savedCase] : [] } : { items: [] });
    api.backend.http.postJson.mockImplementation(async (_path, body) => {
      savedCase = { ...(body as Record<string, unknown>), id: "case-compact", revision: "case-r1" };
      return savedCase as never;
    });
    const { host } = mount(<CasePanel api={api} asset={{ ...asset, xml }} xml={xml} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet"));
    click(button(host, "Add case"));

    const rows = host.querySelectorAll<HTMLElement>(".decision-case-form-fields > fieldset:first-of-type .decision-case-value-grid > .decision-value-field");
    expect(rows).toHaveLength(2);
    expect(Array.from(rows, row => row.querySelector(".decision-value-name")?.textContent)).toEqual(["amount", "eligible"]);
    expect(host.querySelector('[aria-label="Case input amount value kind"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="Case input amount"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="Case input eligible value kind"]')).not.toBeNull();

    const amountKind = host.querySelector<HTMLSelectElement>('[aria-label="Case input amount value kind"]')!;
    select(amountKind, "null");
    expect(host.querySelector<HTMLInputElement>('[aria-label="Case input amount"]')?.disabled).toBe(true);
    select(amountKind, "number");
    type(host.querySelector<HTMLInputElement>('[aria-label="Case input amount"]')!, number);
    const eligibleKind = host.querySelector<HTMLSelectElement>('[aria-label="Case input eligible value kind"]')!;
    select(eligibleKind, "null");
    expect(host.querySelector<HTMLInputElement>('[aria-label="Case input eligible"]')?.disabled).toBe(true);
    select(eligibleKind, "boolean");
    select(host.querySelector<HTMLSelectElement>('[aria-label="Case input eligible"]')!, "true");

    type(host.querySelector<HTMLInputElement>('form[aria-label="New decision case"] input[data-dialog-initial-focus]')!, "Compact case");
    click(button(host, "Save case"));
    await vi.waitFor(() => expect(host.querySelector('[role="dialog"]')).toBeNull());
    expect(savedCase).toMatchObject({ inputs: {
      amount: { kind: "number", value: number },
      eligible: { kind: "boolean", value: "true" }
    } });
    expect(host.textContent).toContain("Compact case");
  });
  it("keeps case values and exposes save errors inside the dialog until a retry succeeds", async () => {
    const api = stubApi(), saved = { id: "case-new", name: "Exact boundary", revision: "case-r1", inputs: { amount: { kind: "number" as const, value: "20" } }, expectedStatus: "Success", expectedResult: null };
    let hasSaved = false;
    api.backend.http.postJson.mockRejectedValueOnce(new Error("Case save unavailable")).mockImplementationOnce(async () => { hasSaved = true; return saved as never; });
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: hasSaved ? [saved] : [] } : { items: [] });
    const { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet"));
    click(button(host, "Add case"));
    const name = host.querySelector<HTMLInputElement>('form[aria-label="New decision case"] input[data-dialog-initial-focus]')!;
    type(name, "Exact boundary"); click(button(host, "Save case"));
    await vi.waitFor(() => expect(host.querySelector('[role="dialog"] [role="alert"]')?.textContent).toBe("Case save unavailable"));
    expect(name.value).toBe("Exact boundary");
    expect(host.querySelector('[role="dialog"]')).not.toBeNull();
    click(button(host, "Save case"));
    await vi.waitFor(() => expect(host.querySelector('[role="dialog"]')).toBeNull());
    expect(api.backend.http.postJson).toHaveBeenCalledTimes(2);
    expect(host.textContent).toContain("Exact boundary");
  });
  it("prevents closing or duplicate submission while a case save is pending", async () => {
    const api = stubApi(); let complete!: (result: unknown) => void;
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: [] } : { items: [] });
    api.backend.http.postJson.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    const { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet"));
    const add = button(host, "Add case"); click(add);
    type(host.querySelector<HTMLInputElement>("input[data-dialog-initial-focus]")!, "Pending save");
    click(button(host, "Save case"));
    await vi.waitFor(() => expect(complete).toBeTypeOf("function"));
    const cancel = button(host, "Cancel");
    expect(cancel.disabled).toBe(true);
    expect(host.querySelector<HTMLInputElement>("input[data-dialog-initial-focus]")?.matches(":disabled")).toBe(true);
    host.querySelector<HTMLInputElement>("input[data-dialog-initial-focus]")!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true }));
    click(button(host, "Save case"));
    expect(host.querySelector('[role="dialog"]')).not.toBeNull();
    expect(api.backend.http.postJson).toHaveBeenCalledTimes(1);
    complete({ id: "pending-case", name: "Pending save", revision: "case-r1" });
    await vi.waitFor(() => expect(host.querySelector('[role="dialog"]')).toBeNull());
    expect(document.activeElement).toBe(add);
  });
  it("groups repeated test diagnostics and exposes named cell review actions", async () => {
    const api = stubApi(), table = parseTable(asset.xml!);
    const saved = { id: "case1", name: "Boundary", revision: "c1", inputs: { amount: { kind: "number" as const, value: "20" } }, expectedStatus: "Success", expectedResult: null };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: [saved] } : path.endsWith("/versions") ? { items: [] } : { items: [] });
    const failure = Object.assign(new Error("Request failed with 400"), { payload: { message: "The exact candidate cannot be evaluated.", diagnostics: [
      { code: "UnsupportedExpression", message: "Decision content does not satisfy the supported profile.", ruleId: table.rules[0].id, clauseId: table.inputs[0].id },
      { code: "UnsupportedExpression", message: "Decision content does not satisfy the supported profile.", ruleId: table.rules[0].id, clauseId: table.outputs[0].id }
    ] } });
    api.backend.http.postJson.mockRejectedValue(failure);
    const onReviewRules = vi.fn(), { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} onReviewRules={onReviewRules} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Boundary"));
    click(button(host, "Run saved cases"));
    await vi.waitFor(() => expect(host.textContent).toContain("The exact candidate cannot be evaluated."));
    expect(host.querySelectorAll(".decision-diagnostic-group")).toHaveLength(1);
    expect(host.querySelectorAll(".decision-diagnostic-message")).toHaveLength(1);
    const review = button(host, "Rule 1 · Column amount");
    click(review);
    expect(onReviewRules).toHaveBeenCalledWith([expect.objectContaining({ clauseId: table.inputs[0].id })]);
    expect(host.textContent).toContain("Column result");
  });
  it("confirms case deletion from the overflow menu and keeps cancellation safe", async () => {
    const api = stubApi(), saved = { id: "case1", name: "Boundary", revision: "c1", inputs: { amount: { kind: "number" as const, value: "20" } }, expectedStatus: "Success", expectedResult: null };
    let deleted = false;
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: deleted ? [] : [saved] } : { items: [] });
    api.backend.http.deleteJson.mockImplementation(async () => { deleted = true; return undefined as never; });
    api.dialogs.confirm.mockResolvedValue(false);
    const { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Boundary"));
    click(caseMenuItem(host, "Boundary", "Delete"));
    await vi.waitFor(() => expect(api.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: "Delete case", message: "Delete “Boundary”?", confirmLabel: "Delete", tone: "danger" })));
    expect(api.backend.http.deleteJson).not.toHaveBeenCalled();
    expect(host.textContent).toContain("Boundary");

    api.dialogs.confirm.mockResolvedValue(true);
    let deleteAction!: HTMLButtonElement;
    await vi.waitFor(() => { deleteAction = caseMenuItem(host, "Boundary", "Delete"); expect(deleteAction.disabled).toBe(false); });
    click(deleteAction);
    await vi.waitFor(() => expect(api.backend.http.deleteJson).toHaveBeenCalledWith(expect.stringMatching(/\/cases\/case1\?expectedRevision=c1&operationId=/)));
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet"));
  });
  it("ignores a stale response after the backend identity changes", async () => {
    const old = stubApi(), next = stubApi(); let complete!: (result: unknown) => void;
    old.backend.http.getJson.mockImplementation(() => new Promise(resolve => { complete = resolve; }) as never);
    next.backend.http.getJson.mockResolvedValue({ items: [{ ...asset, name: "Tenant B policy" }] } as never);
    const { host, update } = mount(<DecisionsPage api={old} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(complete).toBeTypeOf("function")); update(<DecisionsPage api={next} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Tenant B policy")); complete({ items: [{ ...asset, name: "Tenant A private policy" }] });
    await new Promise(resolve => setTimeout(resolve, 0)); expect(host.textContent).not.toContain("Tenant A private policy");
  });
  it("sends 34-digit case inputs and outputs as exact tagged text", async () => {
    const api = stubApi(), xml = newDecisionXml().replace('label="result" typeRef="string"', 'label="result" typeRef="number"');
    api.backend.http.getJson.mockResolvedValue({ items: [] } as never); api.backend.http.postJson.mockResolvedValue({} as never);
    const { host } = mount(<CasePanel api={api} asset={{ ...asset, xml }} xml={xml} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet")); click(button(host, "Add case"));
    const form = host.querySelector("form")!, name = form.querySelector<HTMLInputElement>("input")!, number = "1234567890123456789012345678901234";
    type(name, "Exact boundary"); type(form.querySelector<HTMLInputElement>('[aria-label="Case input amount"]')!, number); type(form.querySelector<HTMLInputElement>('[aria-label="Expected result"]')!, number);
    click(button(host, "Save case"));
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalled());
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/cases", expect.objectContaining({ inputs: { amount: { kind: "number", value: number } }, expectedResult: { kind: "number", value: number } }));
  });
  it.each(["NoMatch", "HitPolicyViolation", "InvalidInput"])("saves and runs a %s case with the matching null representation", async status => {
    const api = stubApi(), expectedResult = status === "NoMatch" ? { kind: "null", value: null } : null;
    let saved: Record<string, unknown> | undefined;
    api.backend.http.getJson.mockImplementation(async () => ({ items: saved ? [saved] : [] }));
    api.backend.http.postJson.mockImplementation(async (path, body) => {
      if (path.endsWith("/cases")) { saved = { ...body, id: "outcome-case", revision: "case-r1" }; return saved; }
      return { id: "outcome-run", candidateId: "draft-r1", candidateHash: asset.sourceHash, passed: true, hasDetailedPayload: true, comparisons: [{ caseId: "outcome-case", name: status, status, passed: true, expected: expectedResult, actual: expectedResult }], evaluatedAt: asset.createdAt, completedAt: asset.createdAt };
    });
    const { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet")); click(button(host, "Add case"));
    const form = host.querySelector("form")!; type(form.querySelector<HTMLInputElement>("input")!, status);
    const outcomes = Array.from(form.querySelectorAll("select")).find(element => element.labels?.[0]?.textContent?.startsWith("Expected outcome"))!;
    expect(Array.from(outcomes.options).map(option => option.value)).toEqual(["Success", "NoMatch", "HitPolicyViolation", "InvalidInput"]); select(outcomes, status);
    expect(form.querySelector('[aria-label="Expected result"]')).toBeNull(); click(button(host, "Save case"));
    await vi.waitFor(() => expect(host.querySelector("form")).toBeNull());
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/cases", expect.objectContaining({ expectedStatus: status, expectedResult }));
    click(button(host, "Run saved cases")); await vi.waitFor(() => expect(host.textContent).toContain("All cases passed"));
    expect(api.backend.http.postJson).toHaveBeenLastCalledWith("/tenant/decisions/approval/test", expect.objectContaining({ cases: [{ id: "outcome-case", revision: "case-r1" }] }));
    if (status === "NoMatch") expect(host.textContent).toContain("Actual: null");
  });
  it("keeps published tables read only and clones through revision-checked API", async () => {
    const api = stubApi(), published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [published] } : path.endsWith("/cases") ? { items: [] } : path.endsWith("/versions/v1") ? published : asset);
    api.backend.http.postJson.mockResolvedValue(asset as never); const navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} assetId={asset.id} versionId="v1" />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    expect(host.querySelector("h1#decision-title")?.textContent).toBe(asset.name);
    expect(host.querySelector('[aria-current="page"]')?.textContent).toContain(asset.name);
    expect(host.querySelector(".decision-draft-status")?.textContent).toBe("Version 1 · Read only");
    expect(host.querySelector('[role="grid"]')?.getAttribute("aria-readonly")).toBe("true");
    expect(host.querySelector('button[aria-label="Publish"]')).toBeNull();
    click(button(host, "Clone to draft"));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/decisions?decision=approval"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/clone-to-draft", expect.objectContaining({ expectedRevision: "r1", versionId: "v1" }));
    expect(api.notifications.notify).toHaveBeenCalledOnce(); expect(api.notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Version copied to the draft" });
    expect(host.textContent).not.toContain("Version copied to the draft");
  });
  it("keeps validation evidence inline and confirms publication through shared notifications", async () => {
    const api = stubApi(), navigate = vi.fn();
    api.backend.http.postJson.mockImplementation(async path => path.endsWith("/validate") ? { success: true, diagnostics: [] } : { id: "v1", number: 1 });
    const { host } = mount(<DecisionsPage api={api} navigate={navigate} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    decisionAction(host, "Validate");
    await vi.waitFor(() => expect(host.textContent).toContain("The saved draft is valid for the supported DMN profile."));
    expect(api.notifications.notify).not.toHaveBeenCalled();
    decisionAction(host, "Publish");
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/decisions?decision=approval&version=v1"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/publish", expect.objectContaining({ expectedRevision: asset.revision, operationId: expect.any(String) }));
    expect(api.notifications.notify).toHaveBeenCalledOnce(); expect(api.notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Version 1 published" });
    expect(host.textContent).not.toContain("Version 1 published");
  });
  it("keeps failed saves inline and confirms only the successful retry", async () => {
    const api = stubApi();
    api.backend.http.putJson.mockRejectedValueOnce(new Error("Save unavailable")).mockResolvedValueOnce({ ...asset, description: "Updated", revision: "r2" } as never);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    const description = Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Description")!;
    type(description, "Updated"); click(button(host, "Save"));
    await vi.waitFor(() => expect(host.textContent).toContain("Save unavailable"));
    expect(description.value).toBe("Updated"); expect(api.notifications.notify).not.toHaveBeenCalled();
    click(button(host, "Save"));
    await vi.waitFor(() => { expect(api.notifications.notify).toHaveBeenCalledOnce(); expect(api.notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Decision saved" }); });
    expect(host.textContent).not.toContain("Save unavailable"); expect(host.textContent).not.toContain("Decision saved");
  });
  it("does not confirm a cancelled clone", async () => {
    const api = stubApi(), published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [published] } : path.endsWith("/cases") ? { items: [] } : path.endsWith("/versions/v1") ? published : asset);
    api.dialogs.confirm.mockResolvedValue(false);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} versionId="v1" />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    click(button(host, "Clone to draft")); await vi.waitFor(() => expect(api.dialogs.confirm).toHaveBeenCalled());
    expect(api.backend.http.postJson).not.toHaveBeenCalled(); expect(api.notifications.notify).not.toHaveBeenCalled();
  });
  it.each(["save", "publish", "clone"])("suppresses a retired %s completion when the notification controller changes scope", async operation => {
    const api = stubApi(), controller = createNotificationController(), navigate = vi.fn();
    controller.setScope("backend-a:user-a"); Object.assign(api, { notifications: controller.api });
    const published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [published] } : path.endsWith("/cases") ? { items: [] } : path.endsWith("/versions/v1") ? published : asset);
    let complete!: (result: unknown) => void;
    const pending = new Promise(resolve => { complete = resolve; });
    const request = operation === "save" ? api.backend.http.putJson : api.backend.http.postJson;
    request.mockImplementation(() => pending);
    const { host, update } = mount(<DecisionsPage api={api} navigate={navigate} assetId={asset.id} versionId={operation === "clone" ? "v1" : undefined} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    if (operation === "save") type(Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.labels?.[0]?.textContent === "Description")!, "Updated");
    if (operation === "publish") decisionAction(host, "Publish");
    else click(button(host, operation === "save" ? "Save" : "Clone to draft"));
    await vi.waitFor(() => expect(request).toHaveBeenCalledOnce());
    update(<button>Current backend</button>); controller.setScope("backend-b:user-a");
    complete(operation === "publish" ? published : { ...asset, revision: "r2" });
    await pending; await Promise.resolve(); await Promise.resolve();
    expect(controller.getEntries()).toEqual([]); expect(navigate).not.toHaveBeenCalled();
  });
  it("offers latest-at-publication and exact immutable versions to workflow authors", async () => {
    const api = stubApi(), onChange = vi.fn(), published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => ({ items: path.endsWith("/versions") ? [published] : [asset] } as never));
    const { host, update } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="" disabled={false} onChange={onChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Approval")); select(host.querySelector<HTMLSelectElement>('[aria-label="Business decision"]')!, asset.id);
    expect(onChange).toHaveBeenLastCalledWith("approval@latest");
    update(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="approval@latest" disabled={false} onChange={onChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Version 1")); select(host.querySelector<HTMLSelectElement>('[aria-label="Business decision version"]')!, "v1"); expect(onChange).toHaveBeenLastCalledWith("approval@v1");
  });
  it("blocks new workflow bindings to an unpublished decision even when old versions remain", async () => {
    const api = stubApi(), onChange = vi.fn(), onValidationChange = vi.fn(), draft = { ...asset, publishedVersionId: null };
    const published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: asset.sourceHash, publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [published] } : path.endsWith("/decisions") ? { items: [draft] } : draft as never);
    const { host } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="approval@v1" disabled={false} onChange={onChange} onValidationChange={onValidationChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("This decision has no published version"));
    const versionPicker = host.querySelector<HTMLSelectElement>('[aria-label="Business decision version"]')!;
    expect(versionPicker.disabled).toBe(false);
    expect(Array.from(versionPicker.options).filter(option => option.value).every(option => option.disabled)).toBe(true);
    expect(onValidationChange).toHaveBeenLastCalledWith(true);
    expect(onChange).not.toHaveBeenCalled();
  });
  it("lets a workflow author start a new decision in another workspace tab without leaving the workflow", async () => {
    const api = stubApi(), onChange = vi.fn(), requests: StudioWorkspaceTabRequest[] = [], opened = vi.spyOn(window, "open").mockReturnValue(null);
    const handle = (event: Event) => { event.preventDefault(); requests.push((event as CustomEvent<StudioWorkspaceTabRequest>).detail); };
    window.addEventListener(studioWorkspaceTabRequestedEvent, handle); disposals.push(() => { window.removeEventListener(studioWorkspaceTabRequestedEvent, handle); opened.mockRestore(); });
    const { host } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="" disabled={false} onChange={onChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Approval"));
    click(button(host, "New decision"));
    expect(requests).toEqual([{ path: expect.stringMatching(/^\/decisions\?new=1&handoff=[^&]+$/), activate: true }]);
    expect(opened).not.toHaveBeenCalled(); expect(onChange).not.toHaveBeenCalled();
  });
  it("falls back to a browser tab for a new decision on hosts without workspace tabs", async () => {
    const api = stubApi(), opened = vi.spyOn(window, "open").mockReturnValue(null); disposals.push(() => opened.mockRestore());
    const { host } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="" disabled={false} onChange={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Approval"));
    click(button(host, "New decision"));
    expect(opened).toHaveBeenCalledWith("/decisions?new=1", "_blank", "noopener");
  });
  it("disables the new decision shortcut while the inspector is read-only", async () => {
    const api = stubApi(), opened = vi.spyOn(window, "open").mockReturnValue(null); disposals.push(() => opened.mockRestore());
    const { host } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="" disabled onChange={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Approval"));
    const shortcut = button(host, "New decision"); expect(shortcut.disabled).toBe(true);
    click(shortcut); expect(opened).not.toHaveBeenCalled();
  });
  it("hides the new decision shortcut from authors who cannot manage decisions", async () => {
    const api = stubApi(["read"]);
    const { host } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="" disabled={false} onChange={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Approval"));
    expect(() => button(host, "New decision")).toThrow();
  });
  it("renders actual matched and selected rules for exact published evaluation", async () => {
    const xml = editTable(asset.xml!, { kind: "add-rule" }), table = parseTable(xml);
    const api = stubApi(); api.backend.http.postJson.mockResolvedValue({ status: "Success", decisionId: "approval", contentHash: "exact-content", result: { kind: "string", value: "Automatic" }, diagnostics: [], trace: [{ ruleId: table.rules[0].id, matched: true, selected: true, cells: [{ inputId: table.inputs[0].id, truth: "True" }] }, { ruleId: table.rules[1].id, matched: true, selected: false, cells: [{ inputId: table.inputs[0].id, truth: "True" }] }] } as never);
    const { host } = mount(<EvaluationPanel api={api} id={asset.id} versionId="v1" xml={xml} />);
    type(host.querySelector<HTMLInputElement>('[aria-label="Evaluate input amount"]')!, "9007199254740993"); click(button(host, "Evaluate decision"));
    await vi.waitFor(() => expect(host.textContent).toContain("Automatic"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/evaluate", { versionId: "v1", inputs: { amount: { kind: "number", value: "9007199254740993" } } });
    const rows = host.querySelectorAll("tbody tr"); expect(rows[0].querySelector('th')?.textContent).toContain("Rule 1"); expect(rows[1].querySelector('th')?.textContent).toContain("Rule 2"); expect(rows[0].querySelectorAll('td')[0].textContent).toBe("Yes"); expect(rows[1].querySelectorAll('td')[1].textContent).toBe("No"); expect(rows[0].textContent).toContain("amount: True"); expect(host.textContent).toContain("exact-content"); expect(host.querySelector('details[open]')).toBeNull();
  });
  it("identifies historical results from another published candidate without relabeling its rules", async () => {
    const api = stubApi(), old = { id: "old-run", candidateId: "v1", candidateHash: "old-content", passed: true, hasDetailedPayload: false, comparisons: [], completedAt: asset.createdAt, evaluatedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/runs") ? { items: [old] } : path.endsWith("/runs/old-run") ? old : { items: [] });
    const { host } = mount(<CasePanel api={api} asset={asset} candidate={{ id: "v2", assetId: asset.id, number: 2, name: asset.name, xml: asset.xml, sourceHash: "new-content", publishedAt: asset.createdAt }} xml={asset.xml!} dirty={false} />);
    click(button(host, "Load test history")); await vi.waitFor(() => expect(host.textContent).toContain("Inspect run old-run")); click(button(host, "Inspect run old-run"));
    await vi.waitFor(() => expect(host.textContent).toContain("These results belong to a different saved candidate.")); expect(host.textContent).toContain("old-content");
  });
  it("uses Decisions problem messages and tolerates older responses without diagnostics", () => {
    const error = Object.assign(new Error("Request failed with 400."), { payload: {
      code: "unsupported", message: "The exact candidate cannot be evaluated.", diagnostics: [
        { code: "UnsupportedExpression", message: "Review this condition.", ruleId: "rule_1", clauseId: "amount" },
        { code: 12, message: false, ruleId: "private" }
      ]
    } });
    expect(failureMessage(error)).toBe("The exact candidate cannot be evaluated.");
    expect(decisionFailure(error).diagnostics).toEqual([{ code: "UnsupportedExpression", message: "Review this condition.", ruleId: "rule_1", clauseId: "amount" }]);
    const legacy = Object.assign(new Error("Request failed with 400."), { payload: { code: "unsupported", message: "The saved candidate is unsupported." } });
    expect(failureMessage(legacy)).toBe("The saved candidate is unsupported.");
    expect(decisionFailure(legacy).diagnostics).toEqual([]);
    expect(failureMessage(new Error("Network unavailable"))).toBe("Network unavailable");
  });
  it("shows expression examples only for unsupported expressions when table source is available", () => {
    const table = parseTable(asset.xml!), unsupported = [{ code: "UnsupportedExpression", message: "This condition is not supported by the selected profile." }];
    const { host: supportedSource } = mount(<DecisionDiagnostics diagnostics={unsupported} table={table} label="Decision diagnostics" />);
    expect(supportedSource.querySelectorAll(".decision-expression-guidance")).toHaveLength(1);
    expect(supportedSource.textContent).toContain("< 21"); expect(supportedSource.textContent).toContain('"male"');

    const { host: generic } = mount(<DecisionDiagnostics diagnostics={[{ code: "InvalidRange", message: "The range is invalid." }]} table={table} label="Decision diagnostics" />);
    expect(generic.querySelector(".decision-expression-guidance")).toBeNull();
    const { host: unavailableSource } = mount(<DecisionDiagnostics diagnostics={unsupported} table={null} label="Decision diagnostics" />);
    expect(unavailableSource.querySelector(".decision-expression-guidance")).toBeNull();
  });
  it("reviews saved-case diagnostics, clears stale results, and allows correction and retry", async () => {
    const api = stubApi(), table = parseTable(asset.xml!);
    const savedCase = { id: "case-1", name: "Under age boy", revision: "case-r1", inputs: { amount: { kind: "number", value: "20" } }, expectedStatus: "Success", expectedResult: { kind: "string", value: "Approved" } };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: [savedCase] } : path.endsWith("/versions") || path.endsWith("/runs") ? { items: [] } : asset);
    const passingRun = { id: "run-1", candidateId: asset.revision, candidateHash: asset.sourceHash, passed: true, hasDetailedPayload: false, comparisons: [], evaluatedAt: asset.createdAt, completedAt: asset.createdAt };
    const compileFailure = Object.assign(new Error("Request failed with 400."), { payload: { code: "unsupported", message: "The exact candidate cannot be evaluated.", diagnostics: [{
      code: "UnsupportedExpression", message: "This condition is not supported by the selected profile.", decisionId: table.id, ruleId: table.rules[0].id, clauseId: table.inputs[0].id
    }] } });
    api.backend.http.postJson.mockResolvedValueOnce(passingRun as never).mockRejectedValueOnce(compileFailure).mockResolvedValueOnce({ ...passingRun, id: "run-2", candidateId: "r2", candidateHash: "hash2" } as never);
    const correctedXml = editTable(asset.xml!, { kind: "cell", ruleId: table.rules[0].id, output: false, column: 0, text: "< 21" });
    api.backend.http.putJson.mockResolvedValue({ ...asset, xml: correctedXml, revision: "r2", sourceHash: "hash2", updatedAt: "2026-11-06T10:00:00Z" } as never);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    click(host.querySelector<HTMLElement>('[role="tab"][data-tab-id="tests"]')!);
    await vi.waitFor(() => expect(button(host, "Run saved cases").disabled).toBe(false));
    click(button(host, "Run saved cases")); await vi.waitFor(() => expect(host.textContent).toContain("All cases passed"));
    click(button(host, "Run saved cases"));
    await vi.waitFor(() => expect(host.textContent).toContain("The exact candidate cannot be evaluated."));
    expect(host.textContent).toContain("Saved cases could not run");
    expect(host.textContent).not.toContain("All cases passed");
    expect(host.textContent).toContain("This condition is not supported by the selected profile.");
    expect(host.querySelectorAll(".decision-expression-guidance")).toHaveLength(1);
    expect(host.textContent).toContain('"male"');
    expect(host.textContent).toContain("Rule 1"); expect(host.textContent).toContain("Column amount"); expect(host.textContent).not.toContain(table.rules[0].id);
    click(button(host, "Review rules"));
    const cell = gridCell(host, "Rule 1, input amount");
    await vi.waitFor(() => expect(document.activeElement).toBe(cell));
    expect(host.textContent).toContain("UnsupportedExpression");
    editGridCell(host, "Rule 1, input amount", "< 21");
    click(button(host, "Save"));
    await vi.waitFor(() => expect(api.backend.http.putJson).toHaveBeenCalledWith("/tenant/decisions/approval", expect.objectContaining({ xml: correctedXml, expectedRevision: "r1" })));
    click(host.querySelector<HTMLElement>('[role="tab"][data-tab-id="tests"]')!);
    click(button(host, "Run saved cases")); await vi.waitFor(() => expect(host.textContent).toContain("All cases passed"));
    expect(api.backend.http.postJson).toHaveBeenCalledTimes(3);
    expect(api.backend.http.postJson).toHaveBeenLastCalledWith("/tenant/decisions/approval/test", expect.objectContaining({ candidateRevision: "r2", candidateHash: "hash2" }));
  });
  it("blocks dirty navigation probes without showing a dialog and confirms an actual leave", async () => {
    const api = stubApi(), navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} isNew />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    editGridCell(host, "Rule 1, input amount", "<= 1000");
    const probe = new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?new=1", toPath: "/workflows", intent: "probe" } }); window.dispatchEvent(probe);
    expect(probe.defaultPrevented).toBe(true); expect(api.dialogs.confirm).not.toHaveBeenCalled();
    window.dispatchEvent(new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?new=1", toPath: "/workflows", intent: "navigate" } }));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/workflows")); expect(api.dialogs.confirm).toHaveBeenCalledTimes(1);
  });
  it("keeps later case input focus when a delayed frame runs", async () => {
    const frames: FrameRequestCallback[] = [];
    const frame = vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => { frames.push(callback); return frames.length; });
    try {
      const api = stubApi(), { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
      await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet"));
      click(button(host, "Add case"));
      const form = host.querySelector<HTMLFormElement>("form")!, name = form.querySelector<HTMLInputElement>("input")!;
      type(name, "Exact boundary");
      const amount = form.querySelector<HTMLInputElement>('[aria-label="Case input amount"]')!;
      amount.focus(); type(amount, "10000.01");
      frames.splice(0).forEach(callback => callback(performance.now()));
      expect(document.activeElement).toBe(amount);
      type(document.activeElement as HTMLInputElement, "9007199254740993");
      api.backend.http.postJson.mockResolvedValue({ id: "saved", revision: "case-r1" } as never);
      click(button(host, "Save case"));
      await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalled());
      expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/cases", expect.objectContaining({
        name: "Exact boundary", inputs: { amount: { kind: "number", value: "9007199254740993" } }
      }));
    } finally { frame.mockRestore(); }
  });
  it("preserves unsaved case fields until the author saves or cancels before editing another case", async () => {
    const api = stubApi(), cases = ["First", "Second"].map((name, index) => ({ id: `case${index}`, name, revision: `c${index}`, inputs: { amount: { kind: "number", value: `${index}` } }, expectedStatus: "Success", expectedResult: { kind: "string", value: name } }));
    api.backend.http.getJson.mockResolvedValue({ items: cases });
    const { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
    await vi.waitFor(() => expect(host.querySelector('button[aria-label="Actions for First"]')).not.toBeNull()); click(caseMenuItem(host, "First", "Edit"));
    const form = host.querySelector("form")!, name = form.querySelector<HTMLInputElement>("input")!, amount = form.querySelector<HTMLInputElement>('[aria-label="Case input amount"]')!;
    type(name, "Unsaved boundary"); type(amount, "9007199254740993");
    expect(button(host, "Add case").disabled).toBe(true);
    expect(caseMenuItem(host, "First", "Edit").disabled).toBe(true); click(host.querySelector<HTMLButtonElement>('button[aria-label="Actions for First"]')!);
    expect(caseMenuItem(host, "Second", "Edit").disabled).toBe(true);
    expect(name.value).toBe("Unsaved boundary"); expect(amount.value).toBe("9007199254740993");
    expect(host.textContent).not.toContain("Save or cancel this case before adding or editing another case."); expect(api.backend.http.postJson).not.toHaveBeenCalled();
    click(button(host, "Cancel")); expect(button(host, "Add case").disabled).toBe(false);
    expect(caseMenuItem(host, "Second", "Edit").disabled).toBe(false); click(caseMenuItem(host, "Second", "Edit"));
    expect(host.querySelector("form")?.querySelector<HTMLInputElement>("input")?.value).toBe("Second");
  });
  it("cancels case editing with Escape and restores its opener focus", async () => {
    const api = stubApi(), { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet")); const add = button(host, "Add case"); click(add);
    await vi.waitFor(() => expect(host.querySelector("form")).not.toBeNull());
    host.querySelector("form")!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    await vi.waitFor(() => expect(host.querySelector("form")).toBeNull()); await vi.waitFor(() => expect(document.activeElement).toBe(add)); expect(api.backend.http.postJson).not.toHaveBeenCalled();
  });
  it("guards unsaved case edits against navigation and publication without treating them as table changes", async () => {
    const api = stubApi(), navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet")); click(button(host, "Add case"));
    type(host.querySelector<HTMLFormElement>("form")!.querySelector<HTMLInputElement>("input")!, "Unsaved boundary");
    await vi.waitFor(() => expect(button(host, "Publish").disabled).toBe(true)); expect(button(host, "Save").disabled).toBe(true);
    const probe = new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?decision=approval", toPath: "/workflows", intent: "probe" } }); window.dispatchEvent(probe); expect(probe.defaultPrevented).toBe(true);
    api.dialogs.confirm.mockResolvedValue(false);
    window.dispatchEvent(new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?decision=approval", toPath: "/workflows", intent: "navigate" } }));
    await vi.waitFor(() => expect(api.dialogs.confirm).toHaveBeenCalled()); expect(navigate).not.toHaveBeenCalled(); expect(host.querySelector<HTMLFormElement>("form")!.querySelector<HTMLInputElement>("input")!.value).toBe("Unsaved boundary");
    click(button(host, "Cancel")); await vi.waitFor(() => expect(button(host, "Publish").disabled).toBe(false));
    const released = new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?decision=approval", toPath: "/workflows", intent: "probe" } }); window.dispatchEvent(released); expect(released.defaultPrevented).toBe(false);
  });
  it("keeps a dirty workspace tab open on close intent without navigating to another screen", async () => {
    const api = stubApi(), navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} isNew />);
    await vi.waitFor(() => expect(host.querySelector('[role="grid"]')).not.toBeNull());
    editGridCell(host, "Rule 1, input amount", "<= 1000");
    const close = new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?new=1", toPath: "/workflows", intent: "close" } }); window.dispatchEvent(close);
    expect(close.defaultPrevented).toBe(true); await new Promise(resolve => setTimeout(resolve, 0)); expect(navigate).not.toHaveBeenCalled(); expect(api.dialogs.confirm).not.toHaveBeenCalled();
  });
});
