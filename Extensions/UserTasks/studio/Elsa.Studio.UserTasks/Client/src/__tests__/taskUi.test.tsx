// @vitest-environment jsdom
import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { BasicTaskForm } from "../TaskForm";
import { TaskDetail } from "../TasksPage";
import { type TaskActor, type TaskItem } from "../taskApi";
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
function api(getJson = vi.fn().mockResolvedValue(task()), postJson = vi.fn()) { return { backend: { http: { getJson, postJson } }, userTaskForms: { list: () => [{ id: "basic", provider: "elsa.basic-forms/1", component: BasicTaskForm }] } } as unknown as ElsaStudioModuleApi; }
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
