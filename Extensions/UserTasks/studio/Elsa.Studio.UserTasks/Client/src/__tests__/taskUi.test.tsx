// @vitest-environment jsdom
import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioActivityPropertyEditorContribution, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { register } from "../module";
import { ActionsEditor, AssignmentEditor, FormEditor } from "../PropertyEditors";
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

it("recovers an immutable-version collision without discarding the authored fields", async () => {
  let latest = 1;
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : { items: [{ form: { id: "purchase", version: String(latest), name: "Purchase", provider: "elsa.basic-forms/1", fields: [] }, createdAt: "2026-11-02T10:00:00Z" }] });
  const post = vi.fn().mockImplementationOnce(async () => { latest = 2; throw new StudioHttpError(409, "Version exists"); }).mockResolvedValue({});
  await act(async () => root.render(<FormsPage api={api(get, post)} />));
  await click("Create next version");
  const field = (label: string) => [...container.querySelectorAll("label")].find(item => item.textContent?.startsWith(label))!.querySelector("input")!;
  await input(field("Name"), "Edited purchase"); await click("Publish form version");
  await click("Use next available version");
  expect(field("Version").value).toBe("3"); expect(field("Name").value).toBe("Edited purchase");
  await click("Publish form version"); expect(post.mock.calls[1][1]).toMatchObject({ name: "Edited purchase", version: "3" });
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
    let form = { id: "purchase", version: "1", name: "Purchase", provider: "elsa.basic-forms/1", fields: [] };
    const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : { items: [{ form, createdAt: "2026-11-02T10:00:00Z" }] });
    const post = vi.fn(async (_path: string, value: typeof form) => { form = value; return {}; });
    await act(async () => root.render(<FormsPage api={api(get, post)} />));
    await click("Create next version"); await click("Publish form version");
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
    let form = { id: "purchase", version: "1", name: "Purchase", provider: "elsa.basic-forms/1", fields: [] };
    const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : { items: [{ form, createdAt: "2026-11-02T10:00:00Z" }] });
    const post = vi.fn(async (_path: string, value: typeof form) => { await pending; form = value; return {}; });
    await act(async () => root.render(<FormsPage api={api(get, post)} />));
    await click("Create next version"); await click("Publish form version");
    const inbox = container.querySelector<HTMLAnchorElement>("a")!; inbox.focus();
    await act(async () => release()); await vi.waitFor(() => expect(frames.length).toBeGreaterThan(0));
    frames.splice(0).forEach(callback => callback(0)); expect(document.activeElement).toBe(inbox);
  } finally { release(); frame.mockRestore(); }
});

it.each([false, true])("prevents competing local navigation while resolving the next version (preview=%s)", async preview => {
  const form = { id: "purchase", version: "1", name: "Purchase", provider: "elsa.basic-forms/1", fields: [] };
  const page = { items: [{ form, createdAt: "2026-11-02T10:00:00Z" }] };
  let release!: (value: typeof page) => void; const pending = new Promise<typeof page>(resolve => { release = resolve; }); let waiting = false;
  const get = vi.fn(async (path: string) => path.includes("identity") ? { ...identity, canManageForms: true, canReadForms: true } : waiting ? pending : page);
  await act(async () => root.render(<FormsPage api={api(get)} />));
  if (preview) await click("Purchase");
  waiting = true; await click("Create next version");
  try {
    const competing = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === (preview ? "Back to forms" : "Create form version"))!;
    expect(competing.disabled).toBe(true);
    if (!preview) expect(container.querySelector<HTMLButtonElement>("tbody button.user-task-link")!.disabled).toBe(true);
  } finally { await act(async () => release(page)); }
});
