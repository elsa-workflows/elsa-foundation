import React, { Activity, useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { studioWorkspaceTabRequestedEvent, type StudioWorkspaceTabRequest } from "@elsa-workflows/studio-sdk";
import { DecisionsPage } from "../DecisionsPage";
import { DecisionVersionEditor } from "../DecisionVersionEditor";
import { beginNewDecisionHandoff, cancelNewDecisionHandoff, completeNewDecisionHandoff, takeNewDecisionHandoff } from "../newDecisionHandoff";
import { asset, button, click, render, select, stubApi, type } from "./helpers";

const disposals: Array<() => void> = [];
afterEach(() => { disposals.splice(0).forEach(dispose => dispose()); });
function mount(content: React.ReactNode) { const result = render(content); disposals.push(result.dispose); return result; }

const created = { ...asset, id: "created", name: "Picker-created policy", revision: "r9" };
const other = { ...asset, id: "other", name: "Other policy" };

/** Captures workspace-tab requests the way the host does, so the picker believes a tab opened. */
function workspaceTabs() {
  const requests: StudioWorkspaceTabRequest[] = [];
  const handle = (event: Event) => { event.preventDefault(); requests.push((event as CustomEvent<StudioWorkspaceTabRequest>).detail); };
  window.addEventListener(studioWorkspaceTabRequestedEvent, handle);
  disposals.push(() => window.removeEventListener(studioWorkspaceTabRequestedEvent, handle));
  return { requests, token: (index = requests.length - 1) => new URL(requests[index].path, window.location.origin).searchParams.get("handoff")! };
}

function pickerApi() {
  const api = Object.assign(stubApi(), { notifications: { notify: vi.fn() } }); let listed = [asset, other];
  api.backend.http.getJson.mockImplementation(async (path: string) => (path.endsWith("/decisions") ? { items: listed } : { items: [] }) as never);
  api.backend.http.postJson.mockImplementation(async () => { listed = [asset, other, created]; return created as never; });
  return api;
}

/** A workflow tab holding the picker, retained like a hidden workspace tab, beside the tab that edits the new decision. */
function Workspace({ api, initial = "approval@latest", handoff, workflowVisible, disabled = false, onValue }: { api: ReturnType<typeof pickerApi>; initial?: string; handoff?: string | null; workflowVisible: boolean; disabled?: boolean; onValue(value: string): void }) {
  const [value, setValue] = useState(initial);
  const change = (next: string) => { setValue(next); onValue(next); };
  return <>
    <Activity mode={workflowVisible ? "visible" : "hidden"}><div data-tab="workflow"><DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value={value} disabled={disabled} onChange={change} /></div></Activity>
    {handoff !== undefined && !workflowVisible ? <div data-tab="decision"><DecisionsPage api={api} navigate={vi.fn()} isNew handoff={handoff} /></div> : null}
  </>;
}

const picker = (host: HTMLElement) => host.querySelector<HTMLSelectElement>('[data-tab="workflow"] [aria-label="Business decision"]')!;
const decisionTab = (host: HTMLElement) => host.querySelector<HTMLElement>('[data-tab="decision"]')!;

async function saveNewDecision(host: HTMLElement) {
  await vi.waitFor(() => expect(decisionTab(host)?.querySelector('input[aria-label="Input 1 name"]')).not.toBeNull());
  const name = Array.from(decisionTab(host).querySelectorAll<HTMLInputElement>("input")).find(input => input.parentElement?.textContent === "Name")!;
  type(name, created.name); click(button(decisionTab(host), "Save decision"));
}

describe("New decision handoff to the Evaluate Decision picker", () => {
  it("selects the decision saved from the picker's New decision tab when the workflow tab returns", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    const { host, update } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision"));
    expect(tabs.requests).toHaveLength(1); expect(tabs.requests[0].path).toMatch(/^\/decisions\?new=1&handoff=[^&]+$/);
    update(<Workspace api={api} workflowVisible={false} handoff={tabs.token()} onValue={onValue} />);
    await saveNewDecision(host);
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalled());
    expect(onValue).not.toHaveBeenCalled();
    update(<Workspace api={api} workflowVisible handoff={tabs.token()} onValue={onValue} />);
    await vi.waitFor(() => expect(onValue).toHaveBeenCalledWith("created@latest"));
    expect(onValue).toHaveBeenCalledTimes(1);
    await vi.waitFor(() => expect(picker(host).value).toBe("created"));
    expect(picker(host).selectedOptions[0].textContent).toBe(created.name);
    expect(host.textContent).toContain("This decision has no published version");
    expect(api.notifications.notify).toHaveBeenCalledTimes(1);
    expect(api.notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Decision selected", message: `${created.name} is now used by this Evaluate Decision activity.` });
  });

  it("keeps a decision the author picked after starting New decision", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    const { host, update } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision")); const token = tabs.token();
    select(picker(host), "other"); expect(onValue).toHaveBeenLastCalledWith("other@latest");
    update(<Workspace api={api} workflowVisible={false} handoff={token} onValue={onValue} />);
    await saveNewDecision(host);
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalled());
    update(<Workspace api={api} workflowVisible handoff={token} onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="created"]')).not.toBeNull());
    expect(onValue).toHaveBeenCalledTimes(1); expect(picker(host).value).toBe("other");
  });

  it("keeps the flow when the author clicks New decision again before saving, so the host focuses the same tab", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    const { host } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision")); click(button(host, "New decision"));
    expect(tabs.requests).toHaveLength(2); expect(tabs.requests[1].path).toBe(tabs.requests[0].path);
    completeNewDecisionHandoff(tabs.token(0), { id: "created", name: created.name });
    await vi.waitFor(() => expect(onValue).toHaveBeenCalledWith("created@latest"));
    click(button(host, "New decision"));
    expect(tabs.token(2)).not.toBe(tabs.token(0));
  });

  it("re-arms a flow the author interrupted, so New decision again focuses the same editor tab", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    const { host } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision")); select(picker(host), "other");
    click(button(host, "New decision"));
    expect(tabs.requests[1].path).toBe(tabs.requests[0].path);
    completeNewDecisionHandoff(tabs.token(0), { id: "created", name: created.name });
    await vi.waitFor(() => expect(onValue).toHaveBeenLastCalledWith("created@latest"));
    expect(api.notifications.notify).toHaveBeenCalledTimes(1);
  });

  it("does not select for an interrupted flow, and starts a fresh one once its editor has saved", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    const { host } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision")); select(picker(host), "other");
    completeNewDecisionHandoff(tabs.token(0), { id: "created", name: created.name });
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(onValue).toHaveBeenCalledTimes(1); expect(picker(host).value).toBe("other"); expect(api.notifications.notify).not.toHaveBeenCalled();
    click(button(host, "New decision"));
    expect(tabs.token(1)).not.toBe(tabs.token(0));
  });

  it("keeps the author's pick even when they pick the original decision again", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    const { host } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision")); select(picker(host), "other"); select(picker(host), "approval");
    completeNewDecisionHandoff(tabs.token(), { id: "created", name: created.name });
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(onValue).toHaveBeenCalledTimes(2); expect(picker(host).value).toBe("approval");
  });

  it("lists the selected decision after a late save, so it stays offered when the author picks another", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn(); let saved = false;
    api.backend.http.getJson.mockImplementation(async (path: string) => ({ items: path.endsWith("/decisions") ? saved ? [asset, other, created] : [asset, other] : [] }) as never);
    const { host } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision"));
    saved = true; completeNewDecisionHandoff(tabs.token(), { id: "created", name: created.name });
    await vi.waitFor(() => expect(picker(host).value).toBe("created"));
    await vi.waitFor(() => expect(api.backend.http.getJson.mock.calls.filter(([path]) => String(path).endsWith("/decisions")).length).toBe(2));
    await new Promise(resolve => setTimeout(resolve, 20));
    select(picker(host), "other");
    expect(picker(host).querySelector('option[value="created"]')?.textContent).toBe(created.name);
  });

  it("ends the flow when the author changes the version before saving", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onChange = vi.fn(), published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async (path: string) => ({ items: path.endsWith("/versions") ? [published] : [asset, other] }) as never);
    // The value stays fixed, so only the cancellation (not the click-time comparison) can keep the pick.
    const { host } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="approval@latest" disabled={false} onChange={onChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Version 1"));
    click(button(host, "New decision"));
    select(host.querySelector<HTMLSelectElement>('[aria-label="Business decision version"]')!, "v1"); expect(onChange).toHaveBeenLastCalledWith("approval@v1");
    completeNewDecisionHandoff(tabs.token(), { id: "created", name: created.name });
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it("names the selected decision before the reloaded list includes it", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    const { host } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision"));
    completeNewDecisionHandoff(tabs.token(), { id: "created", name: created.name });
    await vi.waitFor(() => expect(picker(host).value).toBe("created"));
    expect(picker(host).querySelectorAll('option[value="created"]')).toHaveLength(1);
    expect(picker(host).selectedOptions[0].textContent).toBe(created.name);
    // Listed once, then gone again (deleted, access lost): it reads as unavailable, not by the old name.
    const listing = (items: unknown[]) => api.backend.http.getJson.mockImplementation(async (path: string) => (path.endsWith("/decisions") ? { items } : { items: [] }) as never);
    const reloads = () => api.backend.http.getJson.mock.calls.filter(([path]) => String(path).endsWith("/decisions")).length;
    const reload = async () => { const before = reloads(); window.dispatchEvent(new Event("focus")); await vi.waitFor(() => expect(reloads()).toBeGreaterThan(before)); await new Promise(resolve => setTimeout(resolve, 20)); };
    listing([asset, other, created]); await reload();
    listing([asset, other]); await reload();
    await vi.waitFor(() => expect(picker(host).selectedOptions[0].textContent).toBe("Unavailable decision (created)"));
  });

  it("selects the decision when the author returns before the save completes", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    let finish: (value: unknown) => void = () => undefined;
    api.backend.http.postJson.mockImplementation(() => new Promise(resolve => { finish = resolve; }) as never);
    // Both tabs stay mounted, as the host retains them; the editor tab is hidden while its save is in flight.
    function Tabs({ workflowVisible, handoff }: { workflowVisible: boolean; handoff: string | null }) {
      const [value, setValue] = useState("approval@latest");
      return <>
        <Activity mode={workflowVisible ? "visible" : "hidden"}><div data-tab="workflow"><DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value={value} disabled={false} onChange={next => { setValue(String(next)); onValue(String(next)); }} /></div></Activity>
        {handoff ? <Activity mode={workflowVisible ? "hidden" : "visible"}><div data-tab="decision"><DecisionsPage api={api} navigate={vi.fn()} isNew handoff={handoff} /></div></Activity> : null}
      </>;
    }
    const { host, update } = mount(<Tabs workflowVisible handoff={null} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision")); const token = tabs.token();
    update(<Tabs workflowVisible={false} handoff={token} />);
    await saveNewDecision(host);
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalled());
    update(<Tabs workflowVisible handoff={token} />);
    finish(created);
    await vi.waitFor(() => expect(onValue).toHaveBeenCalledWith("created@latest"));
  });

  it("keeps a value changed outside the picker after New decision", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onChange = vi.fn();
    const editor = (value: string) => <DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value={value} disabled={false} onChange={onChange} />;
    const { host, update } = mount(editor("approval@latest"));
    await vi.waitFor(() => expect(host.textContent).toContain("Other policy"));
    click(button(host, "New decision"));
    update(editor("other@latest")); // An undo or a draft reload, not this picker's selects.
    completeNewDecisionHandoff(tabs.token(), { id: "created", name: created.name });
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(onChange).not.toHaveBeenCalled();
  });

  it("leaves a read-only inspector unchanged", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), onValue = vi.fn();
    const { host, update } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision")); const token = tabs.token();
    update(<Workspace api={api} workflowVisible={false} handoff={token} onValue={onValue} />);
    await saveNewDecision(host);
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalled());
    update(<Workspace api={api} workflowVisible disabled handoff={token} onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="created"]')).not.toBeNull());
    expect(onValue).not.toHaveBeenCalled(); expect(picker(host).value).toBe("approval");
  });

  it("only moves the picker that started the flow", async () => {
    const api = pickerApi(), tabs = workspaceTabs(), started = vi.fn(), bystander = vi.fn();
    const editor = (onChange: (value: string) => void) => <DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="approval@latest" disabled={false} onChange={onChange} />;
    const first = mount(editor(started)), second = mount(editor(bystander));
    await vi.waitFor(() => expect(second.host.textContent).toContain("Other policy"));
    click(button(first.host, "New decision"));
    completeNewDecisionHandoff(tabs.token(), { id: "created", name: created.name });
    await vi.waitFor(() => expect(started).toHaveBeenCalledWith("created@latest"));
    expect(bystander).not.toHaveBeenCalled();
  });

  it("does not move the picker for a new decision started some other way", async () => {
    const api = pickerApi(), onValue = vi.fn();
    const { host, update } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    update(<Workspace api={api} workflowVisible={false} handoff={null} onValue={onValue} />);
    await saveNewDecision(host);
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalled());
    update(<Workspace api={api} workflowVisible handoff={null} onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="created"]')).not.toBeNull());
    expect(onValue).not.toHaveBeenCalled();
  });

  it("opens the browser-tab fallback without a handoff, which only refreshes the list", async () => {
    const api = pickerApi(), opened = vi.spyOn(window, "open").mockReturnValue(null), onValue = vi.fn(); disposals.push(() => opened.mockRestore());
    const { host } = mount(<Workspace api={api} workflowVisible onValue={onValue} />);
    await vi.waitFor(() => expect(picker(host).querySelector('option[value="other"]')).not.toBeNull());
    click(button(host, "New decision"));
    expect(opened).toHaveBeenCalledWith("/decisions?new=1", "_blank", "noopener");
    window.dispatchEvent(new Event("focus"));
    await vi.waitFor(() => expect(api.backend.http.getJson.mock.calls.filter(([path]) => String(path).endsWith("/decisions")).length).toBeGreaterThan(1));
    expect(onValue).not.toHaveBeenCalled();
  });
});

describe("New decision handoff store", () => {
  it("hands a completed result over once and ignores unknown, cancelled or repeated completions", () => {
    const token = beginNewDecisionHandoff();
    expect(takeNewDecisionHandoff(token)).toBeNull();
    completeNewDecisionHandoff(token, { id: "a", name: "A" }); completeNewDecisionHandoff(token, { id: "b", name: "B" });
    expect(takeNewDecisionHandoff(token)).toEqual({ id: "a", name: "A" }); expect(takeNewDecisionHandoff(token)).toBeNull();
    completeNewDecisionHandoff("unknown", { id: "c", name: "C" }); expect(takeNewDecisionHandoff("unknown")).toBeNull();
    const cancelled = beginNewDecisionHandoff(); cancelNewDecisionHandoff(cancelled);
    completeNewDecisionHandoff(cancelled, { id: "d", name: "D" }); expect(takeNewDecisionHandoff(cancelled)).toBeNull();
  });

  it("forgets the oldest unfinished handoffs past its cap", () => {
    const oldest = beginNewDecisionHandoff();
    for (let index = 0; index < 20; index += 1) beginNewDecisionHandoff();
    completeNewDecisionHandoff(oldest, { id: "a", name: "A" }); expect(takeNewDecisionHandoff(oldest)).toBeNull();
  });
});
