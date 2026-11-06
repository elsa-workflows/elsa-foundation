import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { newDecisionXml } from "../decisionXml";
import type { DecisionAsset } from "../decisionsApi";
export const asset: DecisionAsset = { id: "approval", name: "Approval", description: "Purchase approval", revision: "r1", sourceHash: "hash1", xml: newDecisionXml(), isSensitive: false, createdAt: "2026-11-05T10:00:00Z", updatedAt: "2026-11-05T10:00:00Z" };
export function stubApi(grants = ["read", "manage", "publish", "evaluate", "test", "history", "source", "sensitive-values"]) {
  const getJson = vi.fn(async (path: string): Promise<unknown> => path.endsWith("/versions") || path.endsWith("/cases") || path.endsWith("/runs") ? { items: [] } : path.endsWith("/decisions") ? { items: [asset] } : asset);
  const registry = () => { const values: unknown[] = []; return { add: (value: unknown) => values.push(value), list: () => values }; };
  const api = {
    backend: { baseUrl: "https://server.example/tenant", http: { getJson, postJson: vi.fn(), putJson: vi.fn(), deleteJson: vi.fn(), requestJson: vi.fn(), requestResponse: vi.fn() } },
    backendSession: { capabilities: { capabilities: [{ id: "elsa.api.decisions", contractVersion: "1", links: [{ rel: "decisions", href: "decisions" }] }] }, identity: { status: "authenticated", subject: "user", tenantId: "tenant", roles: [], permissions: grants.map(grant => `decisions:${grant}`) } },
    runtime: { identity: { subject: "user", tenantId: "tenant" } }, featureAreas: registry(), propertyEditors: registry(),
    dialogs: { confirm: vi.fn(async () => true), prompt: vi.fn(async () => "Imported approval"), alert: vi.fn() }
  };
  return api as unknown as ElsaStudioModuleApi & { backend: typeof api.backend; dialogs: typeof api.dialogs };
}
export function render(content: React.ReactNode) {
  const host = document.createElement("div"); document.body.append(host); const root = createRoot(host); flushSync(() => root.render(content));
  return { host, update: (next: React.ReactNode) => flushSync(() => root.render(next)), dispose: () => { flushSync(() => root.unmount()); host.remove(); } };
}
export function button(host: HTMLElement, text: string) { const result = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(item => item.textContent?.trim() === text); if (!result) throw new Error(`Button not found: ${text}`); return result; }
export function click(element: HTMLElement) { flushSync(() => element.click()); }
export function type(element: HTMLInputElement, value: string) { flushSync(() => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(element, value); element.dispatchEvent(new Event("input", { bubbles: true })); }); }
export function select(element: HTMLSelectElement, value: string) { flushSync(() => { element.value = value; element.dispatchEvent(new Event("change", { bubbles: true })); }); }
