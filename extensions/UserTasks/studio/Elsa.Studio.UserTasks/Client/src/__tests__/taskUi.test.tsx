// @vitest-environment jsdom
import React, { Activity } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, requestStudioNavigation, type StudioActivityPropertyEditorContribution, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { register } from "../module";
import { ActionsEditor, AssignmentEditor, FormEditor } from "../PropertyEditors";
import { BasicTaskForm } from "../TaskForm";
import { FormsPage } from "../FormsPage";
import { FormLifecyclePage } from "../FormsPageLifecycle";
import { TasksPage } from "../TasksPage";
import { TaskDetail, TaskDetailPage } from "../TaskDetailPage";
import { read, type TaskActor, type TaskForm, type TaskItem } from "../taskApi";
import { readTaskBrowseLocation, replaceTaskBrowseLocation, taskCollectionPath, taskDetailPath } from "../taskNavigation";
async function act(action: () => unknown) {
  let result: unknown;
  flushSync(() => { result = action(); });
  await result;
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => {});
}
const notifications = { notify: vi.fn((_input: { level: string; title: string; message?: string }) => ({ id: "test", update() {}, dismiss() {} })), track: <T,>(work: Promise<T>) => work };
let container: HTMLDivElement; let root: Root;
beforeEach(() => { notifications.notify.mockClear(); window.history.replaceState(null, "", "/"); container = document.createElement("div"); document.body.append(container); root = createRoot(container); });
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });
const identity: TaskActor = { userId: "alice", canRead: true, canAct: true, canManage: false, canManageForms: false, canReadForms: false };
const task = (): TaskItem => ({ id: "task", title: "Purchase", assigneeId: "alice", actions: ["Approve", "Reject"], optionalFormActions: [], prefill: {}, status: "Open", revision: 1, history: [{ operation: "created", actorId: "workflow", at: "2026-11-02T12:00:00Z" }] });
const taskLinks = [{ rel: "identity", href: "runtime/user-task-identity" }, { rel: "tasks", href: "runtime/user-tasks" }, { rel: "task", href: "runtime/user-tasks/{id}" }, ...["claim", "release", "complete", "reassign"].map(rel => ({ rel, href: `runtime/user-tasks/{id}/${rel}` })), { rel: "forms", href: "runtime/user-task-forms" }, { rel: "form-definitions", href: "runtime/user-task-forms/definitions" }, { rel: "form-definition", href: "runtime/user-task-forms/{id}/definition" }, { rel: "form-history", href: "runtime/user-task-forms/{id}/versions" }, { rel: "save-form-draft", href: "runtime/user-task-forms/{id}/draft" }, { rel: "discard-form-draft", href: "runtime/user-task-forms/{id}/discard" }, { rel: "publish-form", href: "runtime/user-task-forms/{id}/publish" }, { rel: "targets", href: "runtime/user-task-targets" }];
const legacyTaskLinks = taskLinks.filter(({ rel }) => rel === "identity" || rel === "forms");
function api(getJson = vi.fn().mockResolvedValue(task()), postJson = vi.fn(), links = taskLinks) { return { notifications, backend: { http: { getJson, postJson } }, backendSession: { capabilities: { capabilities: [{ id: "elsa.api.user-tasks", contractVersion: "1", links }] } }, userTaskForms: { list: () => [{ id: "basic", provider: "elsa.basic-forms/1", component: BasicTaskForm }] } } as unknown as ElsaStudioModuleApi; }
async function click(text: string) {
  let button: HTMLButtonElement | undefined;
  await vi.waitFor(() => { button = [...container.querySelectorAll("button")].find(item => item.textContent === text); expect(button).toBeTruthy(); });
  await act(async () => button!.click());
}
async function refreshTask() {
  const button = container.querySelector<HTMLButtonElement>('button[aria-label="Refresh task"]')!;
  expect(button).toBeTruthy();
  await act(async () => button.click());
}
async function clickTaskAction(text: string) {
  const trigger = container.querySelector<HTMLButtonElement>('button[aria-label="Task actions"]');
  expect(trigger).toBeTruthy();
  await act(async () => trigger!.click());
  let item: HTMLButtonElement | undefined;
  await vi.waitFor(() => { item = [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')].find(button => button.textContent === text); expect(item).toBeTruthy(); });
  await act(async () => item!.click());
}
async function startNewForm() {
  await click("New form");
  const dialog = document.querySelector<HTMLElement>('[role="dialog"]')!;
  const name = dialog.querySelector<HTMLInputElement>("input")!;
  const create = dialog.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  expect(create.disabled).toBe(true);
  await input(name, "Named form");
  await input(dialog.querySelector<HTMLTextAreaElement>("textarea")!, "Form purpose");
  await act(async () => create.click());
}
async function editFirstForm() {
  const button = container.querySelector<HTMLButtonElement>("tbody button.user-task-link");
  expect(button).toBeTruthy();
  await act(async () => button!.click());
}
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
  it("keeps unanswered Boolean checkboxes distinct from false and supports clearing an answer", async () => {
    const onChange = vi.fn();
    const form = { fields: [{ key: "accepted", label: "Accept terms", kind: "boolean", presentation: { display: "checkbox" as const, trueLabel: "Enabled", falseLabel: "Disabled", unansweredLabel: "Not provided" }, required: false, readOnly: false }] };
    const Harness = () => { const [value, setValue] = React.useState<Record<string, unknown>>({}); return <BasicTaskForm form={form} value={value} onChange={next => { onChange(next); setValue(next); }} readOnly={false} />; };
    await act(async () => root.render(<Harness />));
    const checkbox = container.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    expect(container.querySelector('[role="status"]')?.textContent).toBe("Not provided");
    await act(async () => checkbox.click());
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ accepted: true });
    expect(container.querySelector('[role="status"]')?.textContent).toBe("Enabled");
    await act(async () => checkbox.click());
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ accepted: false });
    expect(container.querySelector('[role="status"]')?.textContent).toBe("Disabled");
    await click("Clear answer");
    expect(Object.hasOwn(onChange.mock.calls.at(-1)![0], "accepted")).toBe(false);
    expect(container.querySelector('[role="status"]')?.textContent).toBe("Not provided");

    await act(async () => root.render(<BasicTaskForm form={form} value={{}} onChange={onChange} readOnly />));
    expect(container.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBe(true);
    expect(container.querySelector(".user-task-field-clear")).toBeNull();
  });
  it("uses native radio groups with unique names and preserves Boolean and choice values", async () => {
    const onChange = vi.fn();
    const secondChange = vi.fn();
    const form = { fields: [
      { key: "approved", label: "Approve?", kind: "boolean", presentation: { display: "radio" as const, trueLabel: "Approve", falseLabel: "Reject" }, required: true, readOnly: false },
      { key: "channel", label: "Channel", kind: "choice", choices: ["Email", "Phone"], presentation: { display: "radio" as const }, required: false, readOnly: false }
    ] };
    const Harness = ({ callback }: { callback: typeof onChange }) => { const [value, setValue] = React.useState<Record<string, unknown>>({}); return <BasicTaskForm form={form} value={value} onChange={next => { callback(next); setValue(next); }} readOnly={false} />; };
    await act(async () => root.render(<><Harness callback={onChange} /><Harness callback={secondChange} /></>));
    const names = [...container.querySelectorAll<HTMLInputElement>('input[type="radio"]')].map(input => input.name);
    expect(new Set(names).size).toBe(4);
    const groups = container.querySelectorAll("fieldset");
    expect(groups).toHaveLength(4);
    await act(async () => (groups[0].querySelectorAll("input")[1] as HTMLInputElement).click());
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ approved: false });
    await click("Clear answer");
    expect(Object.hasOwn(onChange.mock.calls.at(-1)![0], "approved")).toBe(false);
    await act(async () => (groups[1].querySelectorAll("input")[1] as HTMLInputElement).click());
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ channel: "Phone" });
  });
  it("preserves dropdown presentation for existing Boolean and choice definitions", async () => {
    const onChange = vi.fn();
    await act(async () => root.render(<BasicTaskForm form={{ fields: [
      { key: "approved", label: "Approve?", kind: "boolean", required: false, readOnly: false },
      { key: "channel", label: "Channel", kind: "choice", choices: ["Email"], required: false, readOnly: false }
    ] }} value={{}} onChange={onChange} readOnly={false} />));
    const selects = container.querySelectorAll("select");
    expect(selects).toHaveLength(2);
    expect(selects[0].options[0].textContent).toBe("Choose…");
    await act(async () => { selects[0].value = "false"; selects[0].dispatchEvent(new Event("change", { bubbles: true })); });
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ approved: false });
  });
  it("renders configured labels for unanswered, true, and false dropdown values", async () => {
    const onChange = vi.fn();
    await act(async () => root.render(<BasicTaskForm form={{ fields: [
      { key: "approved", label: "Approve?", kind: "boolean", presentation: { display: "dropdown", trueLabel: "Allow", falseLabel: "Deny", unansweredLabel: "Decide later" }, required: false, readOnly: false }
    ] }} value={{}} onChange={onChange} readOnly={false} />));
    const select = container.querySelector("select")!;
    expect([...select.options].map(option => option.textContent)).toEqual(["Decide later", "Allow", "Deny"]);
    await act(async () => { select.value = "true"; select.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ approved: true });
    await act(async () => { select.value = "false"; select.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(onChange.mock.calls.at(-1)![0]).toEqual({ approved: false });
  });
  it("retries an ambiguous response with the exact original ID and payload even after a retry refusal", async () => {
    const get = vi.fn().mockResolvedValue(task());
    const post = vi.fn().mockRejectedValueOnce(new StudioHttpError(0, "Connection lost"))
      .mockRejectedValueOnce(new StudioHttpError(403, "Permission changed"))
      .mockResolvedValue({ ...task(), status: "CompletionPending", revision: 2 });
    await act(async () => root.render(<TaskDetail api={api(get, post)} id="task" identity={identity} />));
    await click("Approve"); await click("Retry Approve"); await refreshTask(); await click("Retry Approve");
    expect(post).toHaveBeenCalledTimes(3);
    expect(post.mock.calls[0][1]).toEqual(post.mock.calls[1][1]); expect(post.mock.calls[0][1]).toEqual(post.mock.calls[2][1]);
    expect(post.mock.calls[0][1]).not.toHaveProperty("actorId"); expect(container.textContent).toContain("response is recorded");
  });
  it("keeps a newer terminal refresh when an older open-task response arrives later", async () => {
    let release!: (value: TaskItem) => void;
    const old = new Promise<TaskItem>(resolve => { release = resolve; });
    const get = vi.fn().mockReturnValueOnce(old).mockResolvedValue({ ...task(), status: "Completed", revision: 3 });
    const context = api(get);
    await act(async () => root.render(<TaskDetail api={context} id="task" identity={identity} />));
    await click("Retry"); await act(async () => release(task()));
    expect(container.textContent).toContain("Completed"); expect([...container.querySelectorAll("button")].some(item => item.textContent === "Approve")).toBe(false);
  });
  it("removes the pending-delivery notice when refresh confirms completion", async () => {
    const accepted = { ...task(), status: "CompletionPending", revision: 2, decision: { response: { action: "Approve", data: {} } } };
    const get = vi.fn().mockResolvedValueOnce(task()).mockResolvedValue({ ...accepted, status: "Completed", revision: 3 });
    const post = vi.fn().mockResolvedValue(accepted);
    await act(async () => root.render(<TaskDetail api={api(get, post)} id="task" identity={identity} />));
    await click("Approve"); expect(container.textContent).toContain("The workflow will continue when delivery completes.");
    await refreshTask();
    expect(container.textContent).toContain("Completed");
    expect(container.textContent).not.toContain("The workflow will continue when delivery completes.");
  });
  it("renders server errors against the exact field key and refuses a missing integration", async () => {
    const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "AccountID", label: "Account", kind: "text", required: true, readOnly: false }] } };
    const post = vi.fn().mockRejectedValue(new StudioHttpError(400, "Correct the form", null, { errors: { AccountID: "Account is required" } }));
    await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(withForm), post)} id="task" identity={identity} />));
    await click("Approve"); expect(container.textContent).toContain("Account is required"); expect(container.querySelector("input")!.getAttribute("aria-invalid")).toBe("true");
    await act(async () => root.render(<TaskDetail key="missing" api={api(vi.fn().mockResolvedValue({ ...withForm, form: { ...withForm.form, provider: "custom/1" } }))} id="task" identity={identity} />));
    expect(container.textContent).toContain("Form integration unavailable");
    const approve = [...container.querySelectorAll("button")].find(item => item.textContent === "Approve"); expect(approve!.disabled).toBe(true);
  });
});

it("takes forms-only users directly to their permitted form workspace without reading tasks", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canRead: false, canManageForms: true, canReadForms: true } : path.includes("forms") ? { items: [] } : Promise.reject(new StudioHttpError(403, "Task read denied")));
  await act(async () => root.render(<TasksPage api={api(get)} />));
  await act(async () => {});
  expect(container.textContent).toContain("Task forms");
  await vi.waitFor(async () => { await act(async () => {}); expect(container.textContent).toContain("No reusable forms yet"); });
  await startNewForm(); expect(container.textContent).toContain("Named form");
  expect(get.mock.calls.some(([path]) => path.startsWith("/runtime/user-tasks?"))).toBe(false);
});

it("loads published forms through the legacy contract when draft relations are not advertised", async () => {
  const published = { tenantId: "tenant", form: { id: "contact", version: "1", name: "Contact form", provider: "elsa.basic-forms/1", fields: [] }, createdAt: "2026-11-20T00:00:00Z", createdBy: "alice" };
  const get = vi.fn(async (path: string) => {
    if (path.endsWith("user-task-identity")) return { ...identity, canReadForms: true };
    if (path.includes("user-task-forms?")) return { items: [published], next: null };
    throw new Error(`Unexpected request: ${path}`);
  });
  await act(async () => root.render(<FormsPage api={api(get, vi.fn(), legacyTaskLinks)} />));
  await vi.waitFor(() => expect(container.textContent).toContain("Contact form"));
  expect(get.mock.calls.map(([path]) => path).sort()).toEqual(["/runtime/user-task-forms?limit=100", "/runtime/user-task-identity"].sort());
  expect(container.querySelector('button[aria-label="Open form contact"]')).toBeTruthy();
  await click("Contact form");
  expect(container.textContent).toContain("Published version 1");
});

it.each(["missing", "wrong version", "dormant", "missing form relations"] as const)("keeps Forms unavailable for a %s User Tasks capability", async state => {
  const get = vi.fn();
  const context = api(get, vi.fn(), state === "missing form relations" ? [] : legacyTaskLinks);
  const capability = context.backendSession!.capabilities!.capabilities[0];
  if (state === "missing") capability.id = "elsa.api.other";
  if (state === "wrong version") capability.contractVersion = "2";
  if (state === "dormant") capability.status = "dormant";
  await act(async () => root.render(<FormsPage api={context} />));
  expect(container.textContent).toContain("Forms unavailable");
  expect(get).not.toHaveBeenCalled();
});

it("discovers legacy form relations through the SDK when no session capability snapshot exists", async () => {
  const published = { tenantId: "tenant", form: { id: "contact", version: "1", name: "Contact form", provider: "elsa.basic-forms/1", fields: [] }, createdAt: "2026-11-20T00:00:00Z", createdBy: "alice" };
  const get = vi.fn(async (path: string) => {
    if (path === "/capabilities") return { capabilities: [{ id: "elsa.api.user-tasks", contractVersion: "1", links: legacyTaskLinks }] };
    if (path.endsWith("user-task-identity")) return { ...identity, canReadForms: true };
    if (path.includes("user-task-forms?")) return { items: [published], next: null };
    throw new Error(`Unexpected request: ${path}`);
  });
  const context = { ...api(get, vi.fn(), legacyTaskLinks), backendSession: undefined } as unknown as ElsaStudioModuleApi;
  await act(async () => root.render(<FormsPage api={context} />));
  await vi.waitFor(() => expect(container.textContent).toContain("Contact form"));
  expect(get.mock.calls.map(([path]) => path).sort()).toEqual(["/capabilities", "/runtime/user-task-forms?limit=100", "/runtime/user-task-identity"].sort());
});

it("does not fall back to legacy forms when a draft endpoint denies access", async () => {
  const get = vi.fn(async (path: string) => path.endsWith("user-task-identity") ? identity : Promise.reject(new StudioHttpError(403, "Form definitions denied")));
  await act(async () => root.render(<FormsPage api={api(get)} />));
  await vi.waitFor(() => expect(container.textContent).toContain("Form definitions denied"));
  expect(get.mock.calls.some(([path]) => path.includes("user-task-forms?"))).toBe(false);
});

it("keeps recipient preview responses out of the published form definition", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true } : { items: [] });
  const post = vi.fn().mockImplementation(async (_path, request: { form: TaskForm }) => ({ tenantId: "tenant", form: request.form, createdAt: "2026-11-20T00:00:00Z", createdBy: "alice" }));
  await act(async () => root.render(<FormLifecyclePage api={api(get, post)} />));
  await startNewForm();
  const field = (label: string) => [...container.querySelectorAll("label")].find(item => item.textContent?.startsWith(label))!.control as HTMLInputElement;
  await input(field("Form ID"), "purchase"); await input(field("Name"), "Purchase response");
  await click("Text input"); await click("Preview");
  await input(container.querySelector<HTMLInputElement>(".user-task-designer__recipient-preview input")!, "preview-only response");
  await click("Designer"); await click("Publish");
  expect(post).toHaveBeenCalledTimes(1);
  expect(post.mock.calls[0][1]).toMatchObject({ revision: 0, form: {
    id: "purchase",
    name: "Purchase response",
    fields: [{ key: "text1", label: "Text field 1", kind: "text", required: false, readOnly: false }]
  } });
  expect(JSON.stringify(post.mock.calls[0][1])).not.toContain("preview-only response");
});

it("makes an unanswered boolean explicit and submits a selected No as false", async () => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Confirmed", label: "Confirmed", kind: "boolean", required: true, readOnly: false }] } };
  const post = vi.fn().mockResolvedValue({ ...withForm, status: "CompletionPending", revision: 2 });
  await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(withForm), post)} id="task" identity={identity} />));
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
  await act(async () => root.render(<TaskDetailPage api={context} id="one" navigate={vi.fn()} />));
  await vi.waitFor(() => expect(get.mock.calls.some(([path]) => path.endsWith("one"))).toBe(true));
  await act(async () => finishFirst({ ...task(), id: "one", title: "First purchase" }));
  await vi.waitFor(async () => { await act(async () => {}); expect(container.textContent).toContain("First purchase"); });
  await act(async () => root.render(<TaskDetailPage api={context} id="two" navigate={vi.fn()} />));
  await vi.waitFor(() => expect(get.mock.calls.some(([path]) => path.endsWith("two"))).toBe(true));
  await act(async () => finishSecond({ ...task(), id: "two", title: "Second purchase" }));
  await vi.waitFor(async () => { await act(async () => {}); expect(container.textContent).toContain("Second purchase"); });
  expect(container.textContent).not.toContain("First purchase");
});

it("claims and releases group work using returned revisions", async () => {
  const group = { ...task(), assigneeId: undefined, groupId: "finance" };
  const post = vi.fn().mockResolvedValueOnce({ ...group, claimantId: "alice", status: "Claimed", revision: 2 }).mockResolvedValueOnce({ ...group, revision: 3 });
  await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(group), post)} id="task" identity={identity} />));
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
  await click("Group queue");
  expect(get.mock.calls.at(-1)![0]).toContain("view=queue"); expect(get.mock.calls.at(-1)![0]).not.toContain("after=");
});

it("publishes a reusable basic form through the structured field editor", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : { items: [] });
  const post = vi.fn().mockImplementation(async (_path: string, request: { form: TaskForm }) => ({ tenantId: "tenant", form: request.form, createdAt: "2026-11-20T00:00:00Z", createdBy: "alice" }));
  await act(async () => root.render(<FormsPage api={api(get, post)} />));
  await startNewForm();
  const field = (label: string) => [...container.querySelectorAll("label")].find(item => item.textContent?.startsWith(label))!.control as HTMLInputElement;
  await input(field("Form ID"), "purchase"); await input(field("Name"), "Purchase response");
  await click("Text input"); await input(field("Key"), "AccountID"); await input(field("Label"), "Account");
  await click("Publish");
  expect(post.mock.calls[0][1]).toMatchObject({ revision: 0, form: { id: "purchase", version: "1", name: "Purchase response", fields: [{ key: "AccountID", kind: "text" }] } });
  expect(notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Form published", message: "The new immutable version is available to new tasks." });
  expect(container.textContent).not.toContain("immutable form version is available");
});

it.each(["hidden", "visible"] as const)("reconciles an accepted form publication while the retained editor is %s", async completion => {
  let finish!: () => void; let published = false;
  const delayed = new Promise<void>(resolve => { finish = resolve; });
  const form = { id: "purchase", version: "1", name: "Purchase response", provider: "elsa.basic-forms/1", fields: [] };
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : { items: published ? [{ tenantId: "tenant", id: form.id, latestPublished: { tenantId: "tenant", form, createdAt: "2026-11-20T00:00:00Z", createdBy: "alice" }, draft: null, revision: 1 }] : [] });
  const post = vi.fn(async (_path: string, request: { form: typeof form }) => { await delayed; published = true; return { tenantId: "tenant", form: request.form, createdAt: "2026-11-20T00:00:00Z", createdBy: "alice" }; });
  const context = api(get, post);
  const render = (mode: "hidden" | "visible") => act(() => root.render(<React.Activity mode={mode}><FormsPage api={context} /></React.Activity>));
  await render("visible"); await vi.waitFor(() => expect([...container.querySelectorAll("button")].map(button => button.textContent)).toContain("New form")); await startNewForm();
  const field = (label: string) => [...container.querySelectorAll("label")].find(item => item.textContent?.startsWith(label))!.control as HTMLInputElement;
  await input(field("Form ID"), form.id); await input(field("Name"), form.name); await click("Text input"); await click("Publish");
  expect(post).toHaveBeenCalledTimes(1); expect(published).toBe(false);
  await render("hidden"); if (completion === "visible") await render("visible");
  expect(container.querySelector(".user-task-form-editor")).not.toBeNull();
  await act(() => finish()); await render("visible");
  await vi.waitFor(() => expect(container.textContent).toContain("Published · version 1"));
  expect(container.querySelector(".user-task-form-editor")).not.toBeNull();
  await click("Cancel"); expect(container.querySelector("table")?.textContent).toContain(form.name);
  expect(notifications.notify).not.toHaveBeenCalled(); expect(post).toHaveBeenCalledTimes(1);
});

it("reconciles accepted completion after a retained-tab refresh read the old task", async () => {
  let finish!: () => void; let accepted = false;
  const delayed = new Promise<void>(resolve => { finish = resolve; });
  const result = { ...task(), status: "CompletionPending", revision: 2, decision: { response: { action: "Approve", data: {} } } };
  const get = vi.fn(async () => accepted ? result : task());
  const post = vi.fn(async () => { await delayed; accepted = true; return result; });
  const context = api(get, post);
  const render = (mode: "visible" | "hidden") => act(() => root.render(<React.Activity mode={mode}><TaskDetail api={context} id="task" identity={identity} /></React.Activity>));
  await render("visible"); await click("Approve");
  expect(post).toHaveBeenCalledTimes(1); expect(accepted).toBe(false);
  await render("hidden"); await render("visible");
  expect(get.mock.calls.length).toBeGreaterThanOrEqual(2); expect(accepted).toBe(false);
  await act(() => finish());
  await vi.waitFor(() => expect(container.textContent).toContain("Your response is recorded."));
  expect(notifications.notify).not.toHaveBeenCalled();
  expect(container.textContent).not.toContain("Retry Approve");
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
  await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(withForm), post)} id="task" identity={identity} />));
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
  await click("Group queue");
  expect(container.textContent).toContain("Tasks unavailable");
  expect(container.textContent).not.toContain("Purchase");
  expect(container.textContent).not.toContain("tasks shown");
});


it("resolves an unaccepted ambiguous response when a newer revision proves its CAS cannot commit", async () => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  const get = vi.fn().mockResolvedValueOnce(withForm).mockResolvedValue({ ...withForm, revision: 2 });
  const post = vi.fn().mockRejectedValueOnce(new StudioHttpError(0, "Connection lost")).mockResolvedValue({ ...withForm, status: "CompletionPending", revision: 3 });
  await act(async () => root.render(<TaskDetail api={api(get, post)} id="task" identity={identity} />));
  await input(container.querySelector("input")!, "Keep my response"); await click("Approve"); await refreshTask();
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

describe("assignee and group assignment rule", () => {
  const targetsPage = () => vi.fn().mockResolvedValue({ users: [{ id: "alice", name: "Alice" }], groups: [{ id: "buyers", name: "Buyers" }], moreUsers: false, moreGroups: false });
  async function renderAssignment(group: boolean, inputValues: Record<string, unknown> | undefined) {
    const validation = vi.fn();
    await act(async () => root.render(<AssignmentEditor api={api(targetsPage())} group={group} value={null} onChange={vi.fn()} onValidationChange={validation}
      descriptor={{ name: group ? "GroupId" : "AssigneeId", typeName: "String" }} context={{ activity: {}, expressionDescriptors: [], inputValues }} />));
    return validation;
  }
  const alerts = () => [...container.querySelectorAll('[role="alert"]')].map(item => item.textContent);

  it("accepts exactly one of an assignee or a group", async () => {
    const validation = await renderAssignment(false, { AssigneeId: "alice", GroupId: "" });
    expect(validation).toHaveBeenLastCalledWith(false);
    expect(alerts()).toEqual([]);
    expect(container.querySelector("select")!.getAttribute("aria-invalid")).toBe("false");
  });

  it("asks to clear one side when both an assignee and a group are set", async () => {
    const validation = await renderAssignment(false, { AssigneeId: "alice", GroupId: "buyers" });
    expect(validation).toHaveBeenLastCalledWith(true);
    expect(alerts()).toEqual(["Choose either an assignee or a group; clear the other assignment."]);
    expect(container.querySelector("select")!.getAttribute("aria-invalid")).toBe("true");
  });

  it("asks for an assignee or a group when neither is set, treating whitespace as unset", async () => {
    const validation = await renderAssignment(false, { AssigneeId: "  ", GroupId: "" });
    expect(validation).toHaveBeenLastCalledWith(true);
    expect(alerts()).toEqual(["Select an assignee or a group for this task."]);
  });

  it("reports the rule from the group row without repeating the message", async () => {
    const validation = await renderAssignment(true, { AssigneeId: "", GroupId: "" });
    expect(validation).toHaveBeenLastCalledWith(true);
    expect(alerts()).toEqual([]);
  });

  it("does not judge the rule while either side is an expression rather than a literal", async () => {
    // Expression-valued inputs are absent from inputValues; their runtime value is unknown at design time.
    const validation = await renderAssignment(false, { AssigneeId: "" });
    expect(validation).toHaveBeenLastCalledWith(false);
    expect(alerts()).toEqual([]);
    await act(async () => root.unmount()); root = createRoot(container);
    expect(await renderAssignment(false, undefined)).toHaveBeenLastCalledWith(false);
  });
});

it("renders an explicit external integration and submits its nested case-sensitive response", async () => {
  const custom = { ...task(), form: { id: "external", version: "immutable-1", name: "External", provider: "acme.forms/1", fields: [] } };
  const post = vi.fn().mockResolvedValue({ ...custom, status: "CompletionPending", revision: 2 });
  const context = api(vi.fn().mockResolvedValue(custom), post);
  Object.assign(context, { userTaskForms: { list: () => [{ id: "acme.renderer", provider: "acme.forms/1", component: ({ value, onChange, readOnly }) => <label>External amount<input disabled={readOnly} value={String((value.Approval as { AmountUSD?: number } | undefined)?.AmountUSD ?? "")} onChange={event => onChange({ Approval: { AmountUSD: Number(event.target.value), CaseSensitiveKey: "kept" } })} /></label> }] } as NonNullable<ElsaStudioModuleApi["userTaskForms"]> });
  await act(async () => root.render(<TaskDetail api={context} id="task" identity={identity} />));
  await input(container.querySelector("input")!, "42"); await click("Approve");
  expect(post.mock.calls[0][1].data).toEqual({ Approval: { AmountUSD: 42, CaseSensitiveKey: "kept" } });
});

it("selects task assignment and form contributions before the generic scalar editor", async () => {
  const registered: StudioActivityPropertyEditorContribution[] = [];
  const featureArea = vi.fn();
  const context = { ...api(vi.fn(async (path: string) => path.includes("targets") ? { users: [], groups: [] } : path.includes("forms") ? { items: [] } : task())), featureAreas: { add: featureArea }, propertyEditors: { add: (item: StudioActivityPropertyEditorContribution) => registered.push(item) }, userTaskForms: { add: vi.fn() } } as unknown as ElsaStudioModuleApi;
  register(context);
  const nav = (featureArea.mock.calls[0][0] as { nav: { items: Array<{ title: string; placement?: string }> } }).nav;
  expect(nav.items.map(item => item.title)).toEqual(["Inbox", "Task forms"]);
  expect(nav.items.map(item => item.placement)).toEqual(["operate", "operate"]);
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

it.each(["form", "task"])("does not toast a delayed %s mutation after leaving its page", async kind => {
  let finish!: (result: unknown) => void;
  const post = vi.fn(() => new Promise(resolve => { finish = resolve; }));
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true } : path.includes("forms") ? { items: [] } : task());
  const moduleApi = api(get, post);
  if (kind === "form") {
    await act(async () => root.render(<FormsPage api={moduleApi} />));
    await startNewForm();
    const field = (label: string) => [...container.querySelectorAll("label")].find(item => item.textContent?.startsWith(label))!.control as HTMLInputElement;
    await input(field("Form ID"), "purchase"); await input(field("Name"), "Purchase response");
    await click("Text input"); await click("Publish");
  } else {
    await act(async () => root.render(<TaskDetail api={moduleApi} id="task" identity={identity} />));
    await click("Approve");
  }
  expect(post).toHaveBeenCalledTimes(1);
  await act(async () => root.render(<p>Another page</p>));
  await act(async () => finish({ ...task(), status: "Completed", revision: 2 }));
  expect(notifications.notify).not.toHaveBeenCalled();
});

it("pins the latest published form across all pages in one atomic draft change", async () => {
  const version = (id: string, v: string, at: string) => ({ form: { id, version: v, name: "Purchase" }, createdAt: at });
  const get = vi.fn(async (path: string) => path.includes("after=") ? { items: [version("purchase", "2", "2026-11-03T10:00:00Z")] } : { items: [version("purchase", "1", "2026-11-02T10:00:00Z")], next: "later" });
  const setLiteralInputs = vi.fn(); const onChange = vi.fn();
  await act(async () => root.render(<FormEditor api={api(get)} descriptor={{ name: "FormId", typeName: "String", uiHint: "user-task-form" }} value={null} context={{ activity: {}, expressionDescriptors: [], setLiteralInputs }} onChange={onChange} />));
  await vi.waitFor(() => expect(container.querySelector<HTMLSelectElement>("select")!.disabled).toBe(false));
  await act(async () => { const select = container.querySelector("select")!; select.value = "purchase"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  expect(get).toHaveBeenCalledTimes(2); expect(setLiteralInputs).toHaveBeenCalledWith({ FormId: "purchase", FormVersion: "2" }); expect(onChange).not.toHaveBeenCalled();
});

it("keeps pinned selections through a form-load failure and lets authors retry", async () => {
  const get = vi.fn().mockRejectedValueOnce(new Error("Read failed")).mockResolvedValue({ items: [{ form: { id: "purchase", version: "1", name: "Purchase" }, createdAt: "2026-11-02T10:00:00Z" }] });
  const onChange = vi.fn(); const validation = vi.fn();
  await act(async () => root.render(<FormEditor api={api(get)} descriptor={{ name: "FormId", typeName: "String", uiHint: "user-task-form" }} value="purchase" context={{ activity: {}, expressionDescriptors: [] }} onChange={onChange} onValidationChange={validation} />));
  expect(container.querySelector<HTMLSelectElement>("select")!.value).toBe("purchase");
  await click("Retry loading forms");
  await vi.waitFor(() => expect(container.querySelector<HTMLSelectElement>("select")!.disabled).toBe(false));
  expect(container.textContent).not.toContain("Read failed"); expect(onChange).not.toHaveBeenCalled();
});

it("uses the older host's literal FormId and preserves expression-based form version authoring", async () => {
  const get = vi.fn().mockResolvedValue({ items: [{ form: { id: "purchase", version: "2", name: "Purchase" }, createdAt: "2026-11-02T10:00:00Z" }] });
  const context = api(get); const onChange = vi.fn();
  const descriptor = { name: "FormVersion", typeName: "String", uiHint: "user-task-form-version" };
  await act(async () => root.render(<FormEditor api={context} descriptor={descriptor} value="2" context={{ activity: { FormId: { expression: { type: "Literal", value: "purchase" } } }, expressionDescriptors: [] }} onChange={onChange} />));
  await vi.waitFor(() => expect(container.querySelector<HTMLSelectElement>("select")!.disabled).toBe(false));
  expect(container.querySelector("select")!.value).toBe("2"); expect(container.textContent).not.toContain("unavailable");
  await act(async () => root.render(<FormEditor api={context} descriptor={descriptor} value="2" context={{ activity: { FormId: { expression: { type: "JavaScript", value: "getForm()" } } }, expressionDescriptors: [] }} onChange={onChange} />));
  expect(container.querySelector("select")).toBeNull(); expect(container.querySelector("input")!.value).toBe("2");
  expect(container.textContent).toContain("form ID is an expression"); expect(container.textContent).not.toContain("unavailable");
});

it("keeps local edits visible when a shared draft save detects a stale revision", async () => {
  const form = { id: "purchase", version: "1", name: "Purchase", provider: "elsa.basic-forms/1", fields: [] };
  const published = { tenantId: "tenant", form, createdAt: "2026-11-02T10:00:00Z", createdBy: "alice" };
  const definition = { tenantId: "tenant", id: form.id, latestPublished: published, draft: null, revision: 0 };
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : path.includes("/purchase/definition") ? definition : { items: [definition] });
  const post = vi.fn().mockRejectedValue(new StudioHttpError(409, "This form changed elsewhere"));
  await act(async () => root.render(<FormsPage api={api(get, post)} />));
  await editFirstForm();
  const name = [...container.querySelectorAll("label")].find(item => item.textContent?.startsWith("Name"))!.control as HTMLInputElement;
  await input(name, "Edited purchase"); await click("Save");
  expect(container.textContent).toContain("This form changed elsewhere");
  expect(name.value).toBe("Edited purchase");
  expect(post.mock.calls[0][1]).toMatchObject({ revision: 0, form: { id: "purchase", name: "Edited purchase" } });
});

it("distinguishes published forms that share a display name", async () => {
  const get = vi.fn().mockResolvedValue({ items: ["purchase", "expense"].map(id => ({ form: { id, version: "1", name: "Approval" }, createdAt: "2026-11-02T10:00:00Z" })) });
  await act(async () => root.render(<FormEditor api={api(get)} descriptor={{ name: "FormId", typeName: "String", uiHint: "user-task-form" }} value={null} context={{ activity: {}, expressionDescriptors: [] }} onChange={vi.fn()} />));
  await vi.waitFor(() => expect(container.querySelector<HTMLSelectElement>("select")!.disabled).toBe(false));
  expect([...container.querySelectorAll("option")].map(item => item.textContent)).toEqual(["No form", "Approval (purchase)", "Approval (expense)"]);
});

it("does not steal a form row's keyboard focus when publication restores the list", async () => {
  const frames: FrameRequestCallback[] = [];
  const frame = vi.spyOn(globalThis, "requestAnimationFrame").mockImplementation(callback => { frames.push(callback); return frames.length; });
  try {
    const form = { id: "purchase", version: "1", name: "Purchase", provider: "elsa.basic-forms/1", fields: [] };
    const published = { tenantId: "tenant", form, createdAt: "2026-11-02T10:00:00Z", createdBy: "alice" };
    const definition = { tenantId: "tenant", id: form.id, latestPublished: published, draft: null, revision: 0 };
    const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : path.includes("/purchase/definition") ? definition : { items: [definition] });
    const post = vi.fn(async (_path: string, request: { form: typeof form }) => ({ tenantId: "tenant", form: request.form, createdAt: "2026-11-20T00:00:00Z", createdBy: "alice" }));
    await act(async () => root.render(<FormLifecyclePage api={api(get, post)} />));
    await editFirstForm(); await click("Publish");
    await click("Cancel");
    const row = container.querySelector<HTMLButtonElement>("tbody button.user-task-link")!;
    expect(row).toBeTruthy(); expect(frames.length).toBeGreaterThan(0);
    row.focus(); frames.splice(0).forEach(callback => callback(0));
    expect(document.activeElement).toBe(row);
  } finally { frame.mockRestore(); }
});

it("keeps focus moved outside the editor while publication is pending", async () => {
  const frames: FrameRequestCallback[] = [];
  const frame = vi.spyOn(globalThis, "requestAnimationFrame").mockImplementation(callback => { frames.push(callback); return frames.length; });
  let release!: () => void; const pending = new Promise<void>(resolve => { release = resolve; });
  try {
    const form = { id: "purchase", version: "1", name: "Purchase", provider: "elsa.basic-forms/1", fields: [] };
    const published = { tenantId: "tenant", form, createdAt: "2026-11-02T10:00:00Z", createdBy: "alice" };
    const definition = { tenantId: "tenant", id: form.id, latestPublished: published, draft: null, revision: 0 };
    const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : path.includes("/purchase/definition") ? definition : { items: [definition] });
    const post = vi.fn(async (_path: string, request: { form: typeof form }) => { await pending; return { tenantId: "tenant", form: request.form, createdAt: "2026-11-20T00:00:00Z", createdBy: "alice" }; });
    await act(async () => root.render(<FormLifecyclePage api={api(get, post)} />));
    await editFirstForm(); await click("Publish");
    const inbox = container.querySelector<HTMLAnchorElement>("a")!; inbox.focus();
    await act(async () => release());
    await vi.waitFor(() => expect(container.textContent).toContain("Published · version 1"));
    expect(document.activeElement).toBe(inbox); expect(frames).toHaveLength(0);
  } finally { release(); frame.mockRestore(); }
});

it("opens published content without writing a draft and enters edit mode directly", async () => {
  const form = { id: "purchase", version: "1", name: "Purchase", provider: "elsa.basic-forms/1", fields: [] };
  const published = { tenantId: "tenant", form, createdAt: "2026-11-02T10:00:00Z", createdBy: "alice" };
  const definition = { tenantId: "tenant", id: form.id, latestPublished: published, draft: null, revision: 0 };
  let release!: (value: typeof definition) => void;
  const pending = new Promise<typeof definition>(resolve => { release = resolve; }); let loadingDefinition = false;
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : path.includes("/purchase/definition") && loadingDefinition ? pending : path.includes("/purchase/definition") ? definition : { items: [definition] });
  const post = vi.fn();
  await act(async () => root.render(<FormLifecyclePage api={api(get, post)} />));
  loadingDefinition = true; await editFirstForm();
  expect(post).not.toHaveBeenCalled(); expect(container.textContent).toContain("Loading form…");
  await act(async () => release(definition));
  const formId = [...container.querySelectorAll("label")].find(label => label.textContent?.startsWith("Form ID"))!.control as HTMLInputElement;
  expect(formId.value).toBe("purchase");
  expect(post).not.toHaveBeenCalled();
});


function registeredRoutes(context: ElsaStudioModuleApi) {
  const areas: Parameters<ElsaStudioModuleApi["featureAreas"]["add"]>[0][] = [];
  register(Object.assign(context, {
    featureAreas: { add: (area: typeof areas[number]) => areas.push(area) },
    propertyEditors: { add: vi.fn() },
    userTaskForms: { ...context.userTaskForms, add: vi.fn() }
  }));
  return areas[0].routes!;
}

it("opens the task detail route as a standalone page without loading the inbox", async () => {
  window.history.replaceState(null, "", "/user-tasks/task");
  const get = vi.fn(async (path: string) => path.includes("identity") ? identity : path.includes("?") ? { items: [task()] } : task());
  const Route = registeredRoutes(api(get)).find(route => route.path === "/user-tasks/:id")!.component;
  await act(async () => root.render(<Route navigate={vi.fn()} />));
  await vi.waitFor(() => expect(container.textContent).toContain("Purchase"));
  expect(container.querySelector('input[type="search"]')).toBeNull();
  expect(container.querySelector('table[aria-label="User tasks"]')).toBeNull();
  expect(get.mock.calls.some(([path]) => path.startsWith("/runtime/user-tasks?"))).toBe(false);
  expect(container.textContent).toContain("User tasks");
});

it("navigates from the collection to a task instead of mounting its response editor beside the list", async () => {
  const get = vi.fn(async (path: string) => path.includes("identity") ? identity : path.includes("?") ? { items: [task()] } : task());
  const navigate = vi.fn();
  const Route = registeredRoutes(api(get)).find(route => route.path === "/user-tasks")!.component;
  await act(async () => root.render(<Route navigate={navigate} />));
  await act(async () => container.querySelector<HTMLAnchorElement>('[data-task-id="task"]')!.click());
  expect(navigate).toHaveBeenCalledWith(expect.stringContaining("/user-tasks/task"));
  expect(container.querySelector("aside")).toBeNull();
  expect(get.mock.calls.some(([path]) => path.endsWith("/user-tasks/task"))).toBe(false);
});

it("protects an unsent task response from normal Studio navigation and silent guard probes", async () => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(withForm))} id="task" identity={identity} />));
  await input(container.querySelector("input")!, "Keep my response");
  const before = container.textContent;
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks/task", "probe")).toBe(false);
  expect(container.textContent).toBe(before);
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks")).toBe(false);
});

it("blocks task completion when a custom field control is unavailable", async () => {
  const withUnavailableField = { ...task(), form: { id: "custom", version: "1", name: "Custom", provider: "elsa.basic-forms/1", fields: [
    { key: "rating", label: "Rating", kind: "number", required: true, readOnly: false, controlId: "example.rating/1", configuration: { maximum: 5 } }
  ] } };
  const get = vi.fn().mockResolvedValue(withUnavailableField);
  const post = vi.fn();
  await act(async () => root.render(<TaskDetail api={api(get, post)} id="task" identity={identity} />));

  const approve = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Approve")!;
  expect(container.textContent).toContain("field controls are unavailable");
  expect(approve.disabled).toBe(true);
  await act(async () => approve.click());
  expect(post).not.toHaveBeenCalled();
});


it("retains inbox search and opaque paging context through a standalone task and its return link", async () => {
  const contextQuery = new URLSearchParams({ view: "queue", search: "Purchase", after: "opaque+/=" });
  window.history.replaceState({ retained: "host history" }, "", `/user-tasks?${contextQuery}`);
  const get = vi.fn(async (path: string) => path.includes("identity") ? identity : path.includes("?") ? { items: [{ ...task(), id: "opaque/id" }] } : { ...task(), id: "opaque/id" });
  const routes = registeredRoutes(api(get)); const navigate = vi.fn();
  const List = routes.find(route => route.path === "/user-tasks")!.component;
  const Detail = routes.find(route => route.path === "/user-tasks/:id")!.component;
  await act(async () => root.render(<List navigate={navigate} />));
  expect(container.querySelector<HTMLInputElement>('input[type="search"]')!.value).toBe("Purchase");
  await act(async () => container.querySelector<HTMLAnchorElement>("[data-task-id]")!.click());
  const detailPath = navigate.mock.calls.at(-1)![0] as string;
  expect(new URL(detailPath, location.origin).pathname).toBe("/user-tasks/opaque%2Fid");
  expect(new URL(detailPath, location.origin).searchParams.get("after")).toBe("opaque+/=");
  expect(window.history.state).toEqual({ retained: "host history" });
  window.history.replaceState(null, "", detailPath);
  await act(async () => root.render(<Detail navigate={navigate} />));
  await vi.waitFor(() => expect(container.textContent).toContain("Purchase"));
  await click("User tasks");
  const back = new URL(navigate.mock.calls.at(-1)![0], location.origin);
  expect(back.pathname).toBe("/user-tasks"); expect(back.searchParams.get("view")).toBe("queue");
  expect(back.searchParams.get("search")).toBe("Purchase"); expect(back.searchParams.get("after")).toBe("opaque+/="); expect(back.searchParams.get("focusTask")).toBe("opaque/id");
});

it("identifies the task resource independently of its originating inbox", () => {
  const route = registeredRoutes(api()).find(route => route.path === "/user-tasks/:id")!;
  expect(route.workspace!.resourceKey!(new URL("https://studio/user-tasks/opaque%2Fid?view=queue"))).toBe("user-task:opaque/id");
  expect(route.workspace!.resourceKey!(new URL("https://studio/user-tasks/opaque%2Fid?view=history"))).toBe("user-task:opaque/id");
});

it("shows a denied task detail without reading either tasks or the inbox", async () => {
  const get = vi.fn().mockResolvedValue({ ...identity, canRead: false, canReadForms: true, canManageForms: true });
  await act(async () => root.render(<TaskDetailPage api={api(get)} id="task" navigate={vi.fn()} />));
  expect(container.textContent).toContain("Task access unavailable"); expect(container.querySelector("table")).toBeNull();
  expect(container.textContent).not.toContain("Task forms"); expect(get).toHaveBeenCalledTimes(1);
});

it("keeps a response when discard is cancelled and releases the guard only after confirmed discard", async () => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  const confirm = vi.fn().mockResolvedValueOnce(false).mockResolvedValueOnce(true); const closed = vi.fn();
  const context = Object.assign(api(vi.fn().mockResolvedValue(withForm)), { dialogs: { confirm } }) as unknown as ElsaStudioModuleApi;
  await act(async () => root.render(<TaskDetail api={context} id="task" identity={identity} onClose={closed} />));
  await input(container.querySelector("input")!, "Unsent notes");
  await click("User tasks"); expect(closed).not.toHaveBeenCalled(); expect(container.querySelector("input")!.value).toBe("Unsent notes");
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks/task", "probe")).toBe(false);
  await click("User tasks"); expect(closed).toHaveBeenCalledTimes(1);
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks/task", "probe")).toBe(true);
});

it("blocks closing and unload while an ambiguous task response is unconfirmed", async () => {
  const get = vi.fn().mockResolvedValueOnce(task()).mockResolvedValue({ ...task(), revision: 2 });
  const post = vi.fn().mockRejectedValue(new StudioHttpError(0, "Connection lost"));
  await act(async () => root.render(<TaskDetail api={api(get, post)} id="task" identity={identity} />));
  await click("Approve");
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks", "close")).toBe(false);
  expect(window.dispatchEvent(new Event("beforeunload", { cancelable: true }))).toBe(false);
  await refreshTask();
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks/task", "probe")).toBe(true);
});


it("does not navigate after a discard confirmation outlives the task view", async () => {
  let finish!: (value: boolean) => void;
  const confirm = vi.fn().mockReturnValue(new Promise<boolean>(resolve => { finish = resolve; })); const closed = vi.fn();
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  const context = Object.assign(api(vi.fn().mockResolvedValue(withForm)), { dialogs: { confirm } }) as unknown as ElsaStudioModuleApi;
  await act(async () => root.render(<TaskDetail api={context} id="task" identity={identity} onClose={closed} />));
  await input(container.querySelector("input")!, "Unsent notes"); await click("User tasks");
  await act(async () => root.render(<p>Another view</p>)); await act(async () => finish(true));
  expect(closed).not.toHaveBeenCalled();
});


it("restarts an expired inbox cursor after returning from a task without dropping its filter", async () => {
  window.history.replaceState(null, "", "/user-tasks?view=queue&search=Purchase&after=expired");
  const get = vi.fn(async (path: string) => path.includes("identity") ? identity : path.includes("after=") ? Promise.reject(new StudioHttpError(410, "The inbox cursor expired")) : { items: [task()] });
  await act(async () => root.render(<TasksPage api={api(get)} />));
  expect(container.textContent).toContain("The inbox cursor expired"); await click("First page");
  expect(container.textContent).toContain("Purchase"); expect(container.textContent).not.toContain("Tasks unavailable");
  expect(new URL(location.href).searchParams.get("view")).toBe("queue"); expect(new URL(location.href).searchParams.get("search")).toBe("Purchase"); expect(new URL(location.href).searchParams.has("after")).toBe(false);
});


it("preserves the selected backend while qualifying task and collection browse queries", () => {
  window.history.replaceState(null, "", "/user-tasks?backend=production&view=queue&search=Purchase&after=opaque%2B%2F%3D");
  const browse = readTaskBrowseLocation(location.search);
  for (const path of [taskDetailPath("opaque/id", browse), taskCollectionPath(browse, "opaque/id")]) {
    const url = new URL(path, location.origin);
    expect(url.searchParams.get("backend")).toBe("production"); expect(url.searchParams.get("view")).toBe("queue");
    expect(url.searchParams.get("search")).toBe("Purchase"); expect(url.searchParams.get("after")).toBe("opaque+/=");
    expect(path.split("?")).toHaveLength(2);
  }
  replaceTaskBrowseLocation({ ...browse, cursor: null });
  expect(new URL(location.href).searchParams.get("backend")).toBe("production");
});

it.each([false, true])("retains a workspace task response and retry identity across Activity hide/show (ambiguous=%s)", async ambiguous => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  let identityReads = 0; let finishIdentity!: (who: TaskActor) => void;
  const revalidation = new Promise<TaskActor>(resolve => { finishIdentity = resolve; });
  const get = vi.fn(async (path: string) => { if (path.includes("identity")) { identityReads++; return identityReads === 1 ? identity : revalidation; } return withForm; });
  const post = vi.fn().mockRejectedValue(new StudioHttpError(0, "Connection lost")); const context = api(get, post); const navigate = vi.fn();
  const screen = (mode: "visible" | "hidden") => <Activity mode={mode}><TaskDetailPage api={context} id="task" navigate={navigate} /></Activity>;
  await act(async () => root.render(screen("visible")));
  await vi.waitFor(() => expect(container.querySelector("input")).not.toBeNull());
  await input(container.querySelector("input")!, "Retain this exact response");
  if (ambiguous) await click("Approve");
  await act(async () => root.render(screen("hidden"))); await act(async () => root.render(screen("visible")));
  await vi.waitFor(() => expect(identityReads).toBeGreaterThan(1));
  try { expect(container.querySelector("input")?.value).toBe("Retain this exact response"); }
  finally { await act(async () => finishIdentity(identity)); }
  expect(container.querySelector("input")!.value).toBe("Retain this exact response");
  if (ambiguous) { await click("Retry Approve"); expect(post).toHaveBeenCalledTimes(2); expect(post.mock.calls[1][1]).toEqual(post.mock.calls[0][1]); }
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks/task", "probe")).toBe(false);
});

it("allows leaving after an accepted response while runtime delivery is still pending", async () => {
  const closed = vi.fn(); const receipt = { ...task(), status: "CompletionPending", revision: 2, decision: { response: { action: "Approve", data: {} } } };
  await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(task()), vi.fn().mockResolvedValue(receipt))} id="task" identity={identity} onClose={closed} />));
  await click("Approve"); expect(container.textContent).toContain("Your response is recorded");
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks/task", "probe")).toBe(true);
  const back = [...container.querySelectorAll("button")].find(button => button.textContent === "User tasks")!;
  expect(back.disabled).toBe(false); await click("User tasks"); expect(closed).toHaveBeenCalledTimes(1);
});

it("releases an ambiguous attempt when refresh confirms a durable pending decision", async () => {
  const receipt = { ...task(), status: "CompletionPending", revision: 2, decision: { response: { action: "Approve", data: {} } } };
  const get = vi.fn().mockResolvedValueOnce(task()).mockResolvedValue(receipt);
  await act(async () => root.render(<TaskDetail api={api(get, vi.fn().mockRejectedValue(new StudioHttpError(0, "Connection lost")))} id="task" identity={identity} />));
  await click("Approve"); expect(requestStudioNavigation("/user-tasks/task", "/user-tasks/task", "probe")).toBe(false);
  await refreshTask(); expect(container.textContent).toContain("Your response is recorded");
  expect(requestStudioNavigation("/user-tasks/task", "/user-tasks/task", "probe")).toBe(true);
});

it("resets a task response when its backend connection scope changes", async () => {
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  const first = api(vi.fn(async (path: string) => path.includes("identity") ? identity : withForm));
  const second = api(vi.fn(async (path: string) => path.includes("identity") ? identity : { ...withForm, prefill: { Note: "Other backend" } }));
  await act(async () => root.render(<TaskDetailPage api={first} id="task" navigate={vi.fn()} />));
  await vi.waitFor(() => expect(container.querySelector("input")).not.toBeNull()); await input(container.querySelector("input")!, "First backend response");
  await act(async () => root.render(<TaskDetailPage api={second} id="task" navigate={vi.fn()} />));
  await vi.waitFor(() => expect(container.querySelector("input")?.value).toBe("Other backend"));
});


it("keeps an unsent workspace response through transient access revalidation failure", async () => {
  let identityReads = 0;
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  const context = api(vi.fn(async (path: string) => { if (path.includes("identity")) { if (++identityReads > 1) throw new Error("Access service temporarily unavailable"); return identity; } return withForm; }));
  const screen = (mode: "visible" | "hidden") => <Activity mode={mode}><TaskDetailPage api={context} id="task" navigate={vi.fn()} /></Activity>;
  await act(async () => root.render(screen("visible"))); await vi.waitFor(() => expect(container.querySelector("input")).not.toBeNull());
  await input(container.querySelector("input")!, "Keep during outage");
  await act(async () => root.render(screen("hidden"))); await act(async () => root.render(screen("visible")));
  await vi.waitFor(() => expect(container.textContent).toContain("Could not refresh task access"));
  expect(container.querySelector("input")!.value).toBe("Keep during outage");
});

it("withdraws the response view when tab-return access revalidation denies reading tasks", async () => {
  let identityReads = 0;
  const withForm = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] } };
  const context = api(vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canRead: ++identityReads === 1 } : withForm));
  const screen = (mode: "visible" | "hidden") => <Activity mode={mode}><TaskDetailPage api={context} id="task" navigate={vi.fn()} /></Activity>;
  await act(async () => root.render(screen("visible"))); await vi.waitFor(() => expect(container.querySelector("input")).not.toBeNull());
  await input(container.querySelector("input")!, "Private notes");
  await act(async () => root.render(screen("hidden"))); await act(async () => root.render(screen("visible")));
  await vi.waitFor(() => expect(container.textContent).toContain("Task access unavailable")); expect(container.querySelector("input")).toBeNull();
});

describe("inbox and form polish (#4068)", () => {
  const manager: TaskActor = { ...identity, canManage: true };
  const readsDirectory = (get: ReturnType<typeof vi.fn>) => get.mock.calls.some(([path]) => String(path).includes("targets"));
  it("names assignees from the task response for a reader who cannot read the directory, and labels each cell for the stacked narrow layout (#4198)", async () => {
    const items = [{ ...task(), id: "a", title: "Mine", assigneeName: "Alice" }, { ...task(), id: "b", title: "Bob's", assigneeId: "bob", assigneeName: "Bob Builder" },
      { ...task(), id: "c", title: "Queue", assigneeId: undefined, groupId: "finance", groupName: "Finance team" }, { ...task(), id: "d", title: "Claimed", assigneeId: undefined, groupId: "finance", groupName: "Finance team", claimantId: "dana", claimantName: "Dana" },
      { ...task(), id: "e", title: "Stranger", assigneeId: "carol", assigneeName: null }];
    const get = vi.fn(async (path: string) => path.includes("identity") ? identity : { items });
    await act(async () => root.render(<TasksPage api={api(get)} />));
    await vi.waitFor(() => expect(container.textContent).toContain("Bob Builder"));
    const assignments = [...container.querySelectorAll("td[data-label='Assignment']")].map(cell => cell.textContent);
    expect(assignments).toEqual(["You", "Bob Builder", "Finance team", "Dana", "carol"]);
    expect([...container.querySelector("tbody tr")!.querySelectorAll("td")].map(cell => cell.dataset.label)).toEqual(["Task", "Status", "Assignment"]);
    expect(readsDirectory(get)).toBe(false);
  });
  it("keeps IDs readable without reading the directory, even for a manager", async () => {
    const get = vi.fn(async (path: string) => path.includes("identity") ? manager : { items: [{ ...task(), assigneeId: "bob" }] });
    await act(async () => root.render(<TasksPage api={api(get)} />));
    await vi.waitFor(() => expect(container.querySelector("td[data-label='Assignment']")?.textContent).toBe("bob"));
    expect(readsDirectory(get)).toBe(false);
  });
  it("offers Clear search once: beside results, or in the empty state but not both", async () => {
    window.history.replaceState(null, "", "/user-tasks?search=nothing");
    let items = [task()];
    const get = vi.fn(async (path: string) => path.includes("identity") ? identity : { items });
    await act(async () => root.render(<TasksPage api={api(get)} />));
    await vi.waitFor(() => expect(container.querySelector("tbody")).toBeTruthy());
    expect([...container.querySelectorAll("button")].filter(item => item.textContent === "Clear search")).toHaveLength(1);
    items = []; await click("Refresh");
    await vi.waitFor(() => expect(container.textContent).toContain("No matching tasks"));
    const clear = [...container.querySelectorAll("button")].filter(item => item.textContent === "Clear search");
    expect(clear).toHaveLength(1); expect(clear[0].closest(".studio-result-state")).toBeTruthy();
  });
  it("shows a finished response as a record with names in its history", async () => {
    const completed: TaskItem = { ...task(), status: "Completed", form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "multiline", required: true, readOnly: false, help: "" }] },
      decision: { response: { action: "Approve", data: { Note: "Done" } }, actorId: "alice" },
      history: [{ operation: "created", actorId: "workflow", at: "2026-11-02T12:00:00Z" }, { operation: "completed", action: "Approve", actorId: "alice", actorName: "Alice", at: "2026-11-02T12:01:00Z" }, { operation: "reassigned", actorId: "bob", actorName: "Bob Builder", at: "2026-11-02T12:02:00Z" }, { operation: "released", actorId: "erin", actorName: null, at: "2026-11-02T12:03:00Z" }] };
    const get = vi.fn().mockResolvedValue(completed);
    await act(async () => root.render(<TaskDetail api={api(get)} id="task" identity={identity} />));
    await vi.waitFor(() => expect(container.textContent).toContain("Bob Builder"));
    const note = container.querySelector("textarea")!;
    expect(note.disabled).toBe(true); expect(note.value).toBe("Done");
    expect(note.closest(".user-task-response")!.hasAttribute("data-final")).toBe(true);
    expect(container.querySelector(`label[for="${note.id}"]`)!.textContent).toBe("Note");
    expect(note.hasAttribute("aria-describedby")).toBe(false);
    expect(container.querySelector(".user-task-actions")).toBeNull();
    expect([...container.querySelectorAll(".user-task-history-list li")].map(item => item.textContent!.split(" · ").slice(0, -1).join(" · "))).toEqual(["created · workflow", "completed · Approve · You", "reassigned · Bob Builder", "released · erin"]);
    expect(readsDirectory(get)).toBe(false);
  });
  it("keeps the required marker while the response can still be filled in", async () => {
    const open: TaskItem = { ...task(), form: { id: "f", version: "1", name: "F", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "multiline", required: true, readOnly: false }] } };
    await act(async () => root.render(<TaskDetail api={api(vi.fn().mockResolvedValue(open))} id="task" identity={identity} />));
    await vi.waitFor(() => expect(container.querySelector("textarea")).toBeTruthy());
    expect(container.querySelector(`label[for="${container.querySelector("textarea")!.id}"]`)!.textContent).toBe("Note *");
    expect(container.querySelector(".user-task-response")!.hasAttribute("data-final")).toBe(false);
  });
  it("gives a missing task a way back from the breadcrumb", async () => {
    const onClose = vi.fn();
    await act(async () => root.render(<TaskDetail api={api(vi.fn().mockRejectedValue(new StudioHttpError(404, "Not found")))} id="gone" identity={identity} onClose={onClose} />));
    await vi.waitFor(() => expect(container.textContent).toContain("Task not found"));
    const body = container.querySelector("nav")!.querySelector("button")!;
    expect(body.textContent).toBe("User tasks");
    await act(async () => body.click()); await vi.waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });
  it("lists one row per form and publishes the next system-managed version", async () => {
    const formOf = (id: string, version: string) => ({ tenantId: "tenant", form: { id, version, name: id === "purchase" ? "Purchase" : "Expense", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "text" }] }, createdAt: "2026-11-02T10:00:00Z", createdBy: "alice" });
    const definitionOf = (id: string, version: string) => ({ tenantId: "tenant", id, latestPublished: formOf(id, version), draft: null, revision: 0 });
    let items = [definitionOf("purchase", "2"), definitionOf("expense", "1")];
    const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : path.includes("/purchase/definition") ? items[0] : path.includes("/expense/definition") ? items[1] : { items });
    const post = vi.fn(async (_path: string, value: { revision: number; form: { id: string; version: string; name: string; provider: string; fields: typeof items[number]["latestPublished"]["form"]["fields"] } }) => {
      const current = items.find(item => item.id === value.form.id)!;
      const published = { ...formOf(value.form.id, String(Number(current.latestPublished!.form.version) + 1)), form: { ...value.form, version: String(Number(current.latestPublished!.form.version) + 1) } };
      items = items.map(item => item.id === value.form.id ? { ...item, latestPublished: published, revision: value.revision + 1 } : item);
      return published;
    });
    const notify = vi.fn();
    const context = Object.assign(api(get, post), { notifications: { notify } });
    await act(async () => root.render(<FormsPage api={context} />));
    await vi.waitFor(() => expect(container.querySelector("tbody")).toBeTruthy());
    const rows = [...container.querySelectorAll("tbody tr")];
    expect(rows).toHaveLength(2); expect(rows[0].textContent).toContain("purchase"); expect(rows[0].textContent).toContain("Published · v2");
    expect(rows[1].textContent).toContain("expense"); expect([...container.querySelectorAll("tbody tr")].filter(row => row.textContent?.includes("purchase"))).toHaveLength(1);
    await act(async () => rows[0].querySelector<HTMLButtonElement>("button.user-task-link")!.click());
    await click("Publish");
    await click("Cancel");
    await vi.waitFor(() => expect([...container.querySelectorAll("tbody tr")].find(row => row.textContent?.includes("purchase"))?.textContent).toContain("Published · v3"));
    expect(container.querySelectorAll("tbody tr")).toHaveLength(2); expect(post.mock.calls[0][1]).toMatchObject({ revision: 0, form: { id: "purchase" } });
    expect(notify).toHaveBeenCalledWith({ level: "success", title: "Form published", message: "The new immutable version is available to new tasks." });
  });
});
