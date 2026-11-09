// @vitest-environment jsdom
import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioActivityPropertyEditorContribution, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { register } from "../module";
import { ActionsEditor, AssignmentEditor } from "../PropertyEditors";
import { BasicTaskForm } from "../TaskForm";
import { FormsPage } from "../FormsPage";
import { TasksPage, TaskDetail } from "../TasksPage";
import { read, type TaskActor, type TaskItem } from "../taskApi";
async function act(action: () => unknown) {
  let result: unknown;
  flushSync(() => { result = action(); });
  await result;
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => {});
}
let container: HTMLDivElement; let root: Root;
beforeEach(() => { container = document.createElement("div"); document.body.append(container); root = createRoot(container); });
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });
const identity: TaskActor = { userId: "alice", canRead: true, canAct: true, canManage: false, canManageForms: false, canReadForms: false };
const task = (): TaskItem => ({ id: "task", title: "Purchase", assigneeId: "alice", actions: ["Approve", "Reject"], optionalFormActions: [], prefill: {}, status: "Open", revision: 1, history: [{ operation: "created", actorId: "workflow", at: "2026-11-02T12:00:00Z" }] });
function api(getJson = vi.fn().mockResolvedValue(task()), postJson = vi.fn()) { return { backend: { http: { getJson, postJson } }, backendSession: { capabilities: { capabilities: [{ id: "elsa.api.user-tasks", contractVersion: "1", links: [{ rel: "identity", href: "runtime/user-task-identity" }, { rel: "tasks", href: "runtime/user-tasks" }, { rel: "task", href: "runtime/user-tasks/{id}" }, ...["claim", "release", "complete", "reassign"].map(rel => ({ rel, href: `runtime/user-tasks/{id}/${rel}` })), { rel: "forms", href: "runtime/user-task-forms" }, { rel: "targets", href: "runtime/user-task-targets" }] }] } }, userTaskForms: { list: () => [{ id: "basic", provider: "elsa.basic-forms/1", component: BasicTaskForm }] } } as unknown as ElsaStudioModuleApi; }
async function click(text: string) { const button = [...container.querySelectorAll("button")].find(item => item.textContent === text); expect(button).toBeTruthy(); await act(async () => button!.click()); }
async function input(element: HTMLInputElement | HTMLTextAreaElement, value: string) { await act(async () => { const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype; Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(element, value); element.dispatchEvent(new Event("input", { bubbles: true })); }); }

describe("task form rendering and decision delivery", () => {
  it("treats opaque prototype keys as absent and preserves a submitted __proto__ property", async () => {
    const onChange = vi.fn();
    await act(async () => root.render(<BasicTaskForm form={{ fields: [{ key: "constructor", label: "Constructor", kind: "multiline" }, { key: "__proto__", label: "Opaque", kind: "text" }] }} value={{}} onChange={onChange} readOnly={false} errors={{}} />));
    const text = container.querySelector("textarea")!;
    expect(text.value).toBe(""); expect(text.getAttribute("aria-invalid")).toBe("false"); expect(container.textContent).not.toContain("native code");
    await input(container.querySelector("input")!, "opaque value");
    const value = onChange.mock.calls.at(-1)![0];
    expect(Object.hasOwn(value, "__proto__")).toBe(true); expect(JSON.parse(JSON.stringify(value))["__proto__"]).toBe("opaque value");
    expect(Object.getPrototypeOf(value)).toBe(Object.prototype);
  });
  it("clears optional number/date/choice fields by omitting them", async () => {
    for (const kind of ["number", "date", "choice"]) {
      const onChange = vi.fn();
      await act(async () => root.render(<BasicTaskForm form={{ fields: [{ key: "Value", label: "Value", kind, choices: ["A"] }] }} value={{ Value: kind === "number" ? 10 : kind === "date" ? "2026-11-02" : "A" }} onChange={onChange} readOnly={false} />));
      const control = container.querySelector("input,select") as HTMLInputElement | HTMLSelectElement;
      if (control.tagName === "SELECT") await act(async () => { control.value = ""; control.dispatchEvent(new Event("change", { bubbles: true })); });
      else await input(control as HTMLInputElement, "");
      expect(Object.hasOwn(onChange.mock.calls.at(-1)![0], "Value")).toBe(false);
    }
  });
  it("retries an ambiguous response with the exact original ID and payload even after a retry refusal", async () => {
    const get = vi.fn().mockResolvedValue(task());
    const post = vi.fn().mockRejectedValueOnce(new StudioHttpError(0, "Connection lost"))
      .mockRejectedValueOnce(new StudioHttpError(403, "Permission changed"))
      .mockResolvedValue({ ...task(), status: "CompletionPending", revision: 2 });
    await act(async () => root.render(<TaskDetail api={api(get, post)} id="task" identity={identity} changed={vi.fn()} />));
    await click("Approve"); await click("Retry Approve"); await click("Refresh task"); await click("Retry Approve");
    expect(post).toHaveBeenCalledTimes(3);
    expect(post.mock.calls[0][1]).toEqual(post.mock.calls[1][1]); expect(post.mock.calls[0][1]).toEqual(post.mock.calls[2][1]);
    expect(post.mock.calls[0][1]).not.toHaveProperty("actorId"); expect(container.textContent).toContain("response is recorded");
  });
  it("keeps a newer terminal refresh when an older open-task response arrives later", async () => {
    let release!: (value: TaskItem) => void;
    const old = new Promise<TaskItem>(resolve => { release = resolve; });
    const get = vi.fn().mockReturnValueOnce(old).mockResolvedValue({ ...task(), status: "Completed", revision: 3 });
    const context = api(get);
    await act(async () => root.render(<TaskDetail api={context} id="task" identity={identity} changed={vi.fn()} />));
    await click("Retry"); await act(async () => release(task()));
    expect(container.textContent).toContain("Completed"); expect([...container.querySelectorAll("button")].some(item => item.textContent === "Approve")).toBe(false);
  });
  it("removes the pending-delivery notice when refresh confirms completion", async () => {
    const accepted = { ...task(), status: "CompletionPending", revision: 2, decision: { response: { action: "Approve", data: {} } } };
    const get = vi.fn().mockResolvedValueOnce(task()).mockResolvedValue({ ...accepted, status: "Completed", revision: 3 });
    const post = vi.fn().mockResolvedValue(accepted);
    await act(async () => root.render(<TaskDetail api={api(get, post)} id="task" identity={identity} changed={vi.fn()} />));
    await click("Approve"); expect(container.textContent).toContain("The workflow will continue when delivery completes.");
    await click("Refresh task");
    expect(container.textContent).toContain("Completed");
    expect(container.textContent).not.toContain("The workflow will continue when delivery completes.");
  });
  it("renders server errors against the exact field key and refuses a missing integration", async () => {
    const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "AccountID", label: "Account", kind: "text", required: true, readOnly: false }] } };
    const post = vi.fn().mockRejectedValue(new StudioHttpError(400, "Correct the form", null, { errors: { AccountID: "Account is required" } }));
    await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(withForm), post)} id="task" identity={identity} changed={vi.fn()} />));
    await click("Approve"); expect(container.textContent).toContain("Account is required"); expect(container.querySelector("input")!.getAttribute("aria-invalid")).toBe("true");
    await act(async () => root.render(<TaskDetail key="missing" api={api(vi.fn().mockResolvedValue({ ...withForm, form: { ...withForm.form, provider: "custom/1" } }))} id="task" identity={identity} changed={vi.fn()} />));
    expect(container.textContent).toContain("Form integration unavailable");
    const approve = [...container.querySelectorAll("button")].find(item => item.textContent === "Approve"); expect(approve!.disabled).toBe(true);
  });
});

it("takes forms-only users directly to their permitted form workspace without reading tasks", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canRead: false, canManageForms: true, canReadForms: true } : path.includes("forms") ? { items: [] } : Promise.reject(new StudioHttpError(403, "Task read denied")));
  await act(async () => root.render(<TasksPage api={api(get)} />));
  await act(async () => {});
  expect(container.textContent).toContain("Reusable task forms");
  await vi.waitFor(async () => { await act(async () => {}); expect(container.textContent).toContain("No reusable forms yet"); });
  await click("Create form version"); expect(container.textContent).toContain("Publish a basic form version");
  expect(get.mock.calls.some(([path]) => path.startsWith("/runtime/user-tasks?"))).toBe(false);
});

it("makes an unanswered boolean explicit and submits a selected No as false", async () => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Confirmed", label: "Confirmed", kind: "boolean", required: true, readOnly: false }] } };
  const post = vi.fn().mockResolvedValue({ ...withForm, status: "CompletionPending", revision: 2 });
  await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(withForm), post)} id="task" identity={identity} changed={vi.fn()} />));
  const select = container.querySelector("select")!; expect(select.value).toBe("");
  await act(async () => { select.value = "false"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  await click("Approve"); expect(post.mock.calls[0][1].data).toEqual({ Confirmed: false });
});

it("replaces detail selection when the same task page receives another route ID", async () => {
  let finishFirst!: (value: TaskItem) => void; let finishSecond!: (value: TaskItem) => void;
  const first = new Promise<TaskItem>(resolve => { finishFirst = resolve; });
  const second = new Promise<TaskItem>(resolve => { finishSecond = resolve; });
  const get = vi.fn(async (path: string) => {
    if (path.includes("identity")) return identity;
    if (path.includes("?")) return { items: [] };
    return path.endsWith("one") ? first : second;
  });
  const context = api(get);
  await act(async () => root.render(<TasksPage api={context} initialId="one" />));
  await vi.waitFor(() => expect(get.mock.calls.some(([path]) => path.endsWith("one"))).toBe(true));
  await act(async () => finishFirst({ ...task(), id: "one", title: "First purchase" }));
  await vi.waitFor(async () => { await act(async () => {}); expect(container.textContent).toContain("First purchase"); });
  await act(async () => root.render(<TasksPage api={context} initialId="two" />));
  await vi.waitFor(() => expect(get.mock.calls.some(([path]) => path.endsWith("two"))).toBe(true));
  await act(async () => finishSecond({ ...task(), id: "two", title: "Second purchase" }));
  await vi.waitFor(async () => { await act(async () => {}); expect(container.textContent).toContain("Second purchase"); });
  expect(container.textContent).not.toContain("First purchase");
});

it("claims and releases group work using returned revisions", async () => {
  const group = { ...task(), assigneeId: undefined, groupId: "finance" };
  const post = vi.fn().mockResolvedValueOnce({ ...group, claimantId: "alice", status: "Claimed", revision: 2 }).mockResolvedValueOnce({ ...group, revision: 3 });
  await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(group), post)} id="task" identity={identity} changed={vi.fn()} />));
  await click("Claim task"); await click("Release task");
  expect(post.mock.calls[0][0]).toContain("/claim"); expect(post.mock.calls[0][1].revision).toBe(1);
  expect(post.mock.calls[1][0]).toContain("/release"); expect(post.mock.calls[1][1].revision).toBe(2);
  expect(container.textContent).toContain("Claim task");
});

it("pages and filters the inbox without losing the opaque continuation", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? identity : { items: [{ ...task(), title: path.includes("after=") ? "Next purchase" : "First purchase" }], next: path.includes("after=") ? null : "opaque+/=" });
  await act(async () => root.render(<TasksPage api={api(get)} />));
  await click("Next page"); expect(container.textContent).toContain("Next purchase");
  expect(get.mock.calls.some(([path]) => path.includes("after=opaque%2B%2F%3D"))).toBe(true);
  const select = container.querySelector("select")!;
  await act(async () => { select.value = "queue"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  expect(get.mock.calls.at(-1)![0]).toContain("view=queue"); expect(get.mock.calls.at(-1)![0]).not.toContain("after=");
});

it("publishes a reusable basic form through the structured field editor", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : { items: [] });
  const post = vi.fn().mockResolvedValue({});
  await act(async () => root.render(<FormsPage api={api(get, post)} />));
  await click("Create form version");
  const field = (label: string) => [...container.querySelectorAll("label")].find(item => item.textContent?.startsWith(label))!.querySelector("input")!;
  await input(field("Form ID"), "purchase"); await input(field("Name"), "Purchase response");
  await click("Add field"); await input(field("Key"), "AccountID"); await input(field("Label"), "Account");
  await click("Publish form version");
  expect(post.mock.calls[0][1]).toMatchObject({ id: "purchase", version: "1", name: "Purchase response", fields: [{ key: "AccountID", kind: "text" }] });
  expect(container.textContent).toContain("immutable form version is available");
});

it("uses advertised operation relations and encodes opaque task identities", async () => {
  const get = vi.fn().mockResolvedValue(task()); const context = api(get);
  const capability = context.backendSession!.capabilities!.capabilities[0];
  capability.links.find(item => item.rel === "task")!.href = "custom/human-work/{id}";
  await read(context, "opaque/id"); expect(get).toHaveBeenCalledWith("/custom/human-work/opaque%2Fid");
});

it("clears validation only for response fields that the user corrects", async () => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [
    { key: "Note", label: "Note", kind: "multiline", required: true, readOnly: false },
    { key: "Amount", label: "Amount", kind: "number", required: true, readOnly: false }
  ] } };
  const post = vi.fn().mockRejectedValue(new StudioHttpError(400, "Correct the response", null, { errors: { Note: "Enter a note", Amount: "Enter an amount" } }));
  await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(withForm), post)} id="task" identity={identity} changed={vi.fn()} />));
  await click("Approve");
  expect(container.textContent).toContain("Enter a note"); expect(container.textContent).toContain("Enter an amount");
  await input(container.querySelector("textarea")!, "Reviewed");
  expect(container.textContent).not.toContain("Enter a note"); expect(container.textContent).toContain("Enter an amount");
  expect(container.textContent).not.toContain("Correct the response");
});

it("keeps an empty inbox distinct from denied access and hides unavailable paging", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? identity : { items: [] });
  await act(async () => root.render(<TasksPage api={api(get)} />));
  expect(container.querySelector("aside")).toBeNull();
  expect(container.textContent).toContain("No tasks here");
  expect(container.textContent).toContain("0 tasks shown");
  expect([...container.querySelectorAll("button")].some(button => button.textContent === "Next page")).toBe(false);
  const denied = vi.fn().mockResolvedValue({ ...identity, canRead: false });
  await act(async () => root.render(<TasksPage api={api(denied)} />));
  expect(container.textContent).toContain("Task access unavailable");
  expect(container.textContent).not.toContain("No tasks here");
  expect(denied).toHaveBeenCalledTimes(1);
});

it("preserves opaque paging cursors and returns to the first page when searching", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? identity : { items: [task()], next: "opaque/+token" });
  await act(async () => root.render(<TasksPage api={api(get)} />));
  await click("Next page");
  expect(get.mock.calls.some(([path]) => new URL(path, "http://owned").searchParams.get("after") === "opaque/+token")).toBe(true);
  await input(container.querySelector('input[type="search"]')!, "Purchase");
  const path = get.mock.calls.filter(([path]) => path.includes("?")).at(-1)![0];
  expect(new URL(path, "http://owned").searchParams.get("search")).toBe("Purchase");
  expect(new URL(path, "http://owned").searchParams.has("after")).toBe(false);
});

it("does not present the previous inbox after a different inbox fails to load", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? identity : path.includes("view=queue") ? Promise.reject(new Error("Queue unavailable")) : { items: [task()] });
  await act(async () => root.render(<TasksPage api={api(get)} />));
  expect(container.textContent).toContain("Purchase");
  const select = container.querySelector("select")!;
  await act(async () => { select.value = "queue"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  expect(container.textContent).toContain("Tasks unavailable");
  expect(container.textContent).not.toContain("Purchase");
  expect(container.textContent).not.toContain("tasks shown");
});


it("resolves an unaccepted ambiguous response when a newer revision proves its CAS cannot commit", async () => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  const get = vi.fn().mockResolvedValueOnce(withForm).mockResolvedValue({ ...withForm, revision: 2 });
  const post = vi.fn().mockRejectedValueOnce(new StudioHttpError(0, "Connection lost")).mockResolvedValue({ ...withForm, status: "CompletionPending", revision: 3 });
  await act(async () => root.render(<TaskDetail api={api(get, post)} id="task" identity={identity} changed={vi.fn()} />));
  await input(container.querySelector("input")!, "Keep my response"); await click("Approve"); await click("Refresh task");
  expect(container.querySelector("input")!.disabled).toBe(false); expect(container.querySelector("input")!.value).toBe("Keep my response");
  await click("Approve"); expect(post.mock.calls[1][1].revision).toBe(2); expect(post.mock.calls[1][1].requestId).not.toBe(post.mock.calls[0][1].requestId);
});

it("edits the literal JSON string supplied by catalog defaults without losing configured actions", async () => {
  const change = vi.fn(); const validation = vi.fn();
  await act(async () => root.render(<ActionsEditor value={'["Approve","Reject"]'} onChange={change} onValidationChange={validation} descriptor={{ name: "Test", typeName: "String" }} context={{ activity: {}, expressionDescriptors: [] }} />));
  expect([...container.querySelectorAll("input")].map(x => x.value)).toEqual(["Approve", "Reject"]); expect(validation).toHaveBeenLastCalledWith(false);
  await input(container.querySelector("input")!, "Accept"); expect(change).toHaveBeenLastCalledWith(["Accept", "Reject"]);
});

it("serializes target paging and retries a failed page before advancing", async () => {
  let release!: (value: unknown) => void; const pending = new Promise(resolve => { release = resolve; });
  const get = vi.fn().mockResolvedValueOnce({ users: [{ id: "first", name: "First" }], groups: [], moreUsers: true })
    .mockReturnValueOnce(pending).mockRejectedValueOnce(new Error("Temporary outage")).mockResolvedValue({ users: [{ id: "last", name: "Last" }], groups: [], moreUsers: false });
  await act(async () => root.render(<AssignmentEditor api={api(get)} group={false} value={null} onChange={vi.fn()} descriptor={{ name: "Test", typeName: "String" }} context={{ activity: {}, expressionDescriptors: [] }} />));
  await click("More choices"); await click("More choices"); expect(get).toHaveBeenCalledTimes(2);
  await act(async () => release({ users: [{ id: "middle", name: "Middle" }], groups: [], moreUsers: true }));
  await click("More choices"); await click("More choices");
  expect(get.mock.calls.map(([path]) => new URL(path, "http://owned").searchParams.get("skip"))).toEqual(["0", "100", "200", "200"]);
  expect([...container.querySelectorAll("option")].map(x => x.value)).toEqual(["", "first", "middle", "last"]);
});

it("renders an explicit external integration and submits its nested case-sensitive response", async () => {
  const custom = { ...task(), form: { id: "external", version: "immutable-1", name: "External", provider: "acme.forms/1", fields: [] } };
  const post = vi.fn().mockResolvedValue({ ...custom, status: "CompletionPending", revision: 2 });
  const context = api(vi.fn().mockResolvedValue(custom), post);
  Object.assign(context, { userTaskForms: { list: () => [{ id: "acme.renderer", provider: "acme.forms/1", component: ({ value, onChange, readOnly }) => <label>External amount<input disabled={readOnly} value={String((value.Approval as { AmountUSD?: number } | undefined)?.AmountUSD ?? "")} onChange={event => onChange({ Approval: { AmountUSD: Number(event.target.value), CaseSensitiveKey: "kept" } })} /></label> }] } as NonNullable<ElsaStudioModuleApi["userTaskForms"]> });
  await act(async () => root.render(<TaskDetail api={context} id="task" identity={identity} changed={vi.fn()} />));
  await input(container.querySelector("input")!, "42"); await click("Approve");
  expect(post.mock.calls[0][1].data).toEqual({ Approval: { AmountUSD: 42, CaseSensitiveKey: "kept" } });
});

it("selects task assignment and form contributions before the generic scalar editor", async () => {
  const registered: StudioActivityPropertyEditorContribution[] = [];
  const context = { ...api(vi.fn(async (path: string) => path.includes("targets") ? { users: [], groups: [] } : path.includes("forms") ? { items: [] } : task())), featureAreas: { add: vi.fn() }, propertyEditors: { add: (item: StudioActivityPropertyEditorContribution) => registered.push(item) }, userTaskForms: { add: vi.fn() } } as unknown as ElsaStudioModuleApi;
  register(context);
  const fallback: StudioActivityPropertyEditorContribution = { id: "generic.scalar", order: 100, supports: () => true, component: () => <input aria-label="Generic scalar" /> };
  for (const hint of ["user-task-user", "user-task-group", "user-task-form"]) {
    const descriptor = { name: "Test", typeName: "String", uiHint: hint }; const propertyContext = { activity: {}, expressionDescriptors: [] };
    const editor = [...registered, fallback].sort((a, b) => (a.order ?? 500) - (b.order ?? 500)).find(item => item.supports(descriptor, propertyContext))!;
    expect(editor.id).not.toBe("generic.scalar");
    const Component = editor.component;
    await act(async () => root.render(<Component descriptor={descriptor} context={propertyContext} value={null} onChange={vi.fn()} />));
    expect(container.querySelector("select")).toBeTruthy();
  }
});
