import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { defaultPolicy, permissions, type Access, type Job, type Schedule } from "../types";

// Isolated component state injection only; this is not a Jobs authority or acceptance host.
export function fixture() {
  const identity: Access = { tenantId: "tenant-a", subjectId: "alice", permissions: Object.values(permissions) };
  const get = vi.fn(async (_path: string): Promise<unknown> => ({ items: [], next: null }));
  const post = vi.fn(async (_path: string, _body: unknown): Promise<unknown> => ({ status: "applied" }));
  const put = vi.fn(async (_path: string, _body: unknown): Promise<unknown> => ({ status: "applied" }));
  const prompt = vi.fn(async (): Promise<string | null> => "Operator reason"); const confirm = vi.fn(async () => true); const notify = vi.fn(); const add = vi.fn();
  const api = { backend: { baseUrl: "https://owned.invalid", http: { getJson: vi.fn(async (path: string) => path === "/jobs/access" ? structuredClone(identity) : get(path)), postJson: post, putJson: put } },
    backendSession: { capabilities: { capabilities: [{ id: "elsa.api.jobs", contractVersion: "1", links: ["jobs", "queues", "schedules", "holds", "capacity", "access", "operations"].map(rel => ({ rel, href: rel === "jobs" ? "jobs" : "jobs/" + rel })) }] } },
    dialogs: { prompt, confirm, alert: vi.fn(async () => {}) }, notifications: { notify }, featureAreas: { add }
  } as unknown as ElsaStudioModuleApi;
  return { api, identity, get, post, put, prompt, confirm, notify, add };
}
export const job: Job = { jobId: "report-one", handler: { jobType: "reports", version: "1" }, queue: "reports", state: "terminal", revision: 3,
  acceptedAt: "2026-11-06T12:00:00Z", eligibleAt: "2026-11-06T12:00:00Z", cancellationRequested: false,
  outcome: "succeeded", resultId: "result-one", completedAt: "2026-11-06T12:01:00Z", consumptionState: "owed",
  association: { modelKind: "elsa.bpmn", definitionId: "order-fulfillment", definitionVersion: "version-one", executionId: "bpmn-root", mode: "awaited" } };
export const schedule: Schedule = { scheduleId: "hourly", revision: 3, enabled: true, nextOccurrence: "2026-11-06T13:00:00Z", firstOccurrence: "2026-11-06T13:00:00Z",
  createdAt: "2026-11-06T12:00:00Z", updatedAt: "2026-11-06T12:00:00Z", handler: { jobType: "reports", version: "1" }, queue: "reports",
  inputSchema: { alias: "report-input", version: 1 }, resultSchema: { alias: "report-result", version: 1 }, policy: structuredClone(defaultPolicy),
  recurrence: { kind: "interval", expression: "PT1H" }, missedPolicy: "coalesceOne" };
export async function act(action: () => unknown) {
  let result: unknown; flushSync(() => { result = action(); }); await result;
  await new Promise(resolve => setTimeout(resolve, 0)); flushSync(() => {});
}
export function mount() {
  const container = document.createElement("div"); document.body.append(container); const root: Root = createRoot(container);
  return { container, render: (node: React.ReactNode) => act(() => root.render(node)), dispose: async () => { await act(() => root.unmount()); container.remove(); } };
}
export async function click(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(node => node.textContent === text);
  if (!button) throw new Error(`Button missing: ${text}`); await act(() => button.click());
}
export function control(container: HTMLElement, label: string) {
  const field = [...container.querySelectorAll("label")].find(node => node.querySelector("strong")?.textContent === label);
  const input = field?.querySelector("input,textarea,select"); if (!input) throw new Error(`Field missing: ${label}`);
  return input as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
}
export async function input(element: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement, value: string) {
  await act(() => {
    const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(element, value);
    element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? "change" : "input", { bubbles: true }));
  });
}
export async function submit(container: HTMLElement) {
  await act(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
}
