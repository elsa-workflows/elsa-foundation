import React, { type ReactNode } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { getDefinition, listDefinitions } from "../../api/workflowDesign";
import { SubscriptionEditor, SubscriptionsSection } from "../Subscriptions";
import type { TriggerClient } from "../api";
import type { ActorView, SubscriptionView, TargetSelector, TriggerView } from "../types";

vi.mock("../../api/workflowDesign", () => ({ getDefinition: vi.fn(), listDefinitions: vi.fn() }));
let roots: Root[] = [];
afterEach(() => { for (const root of roots) flushSync(() => root.unmount()); roots = []; document.body.innerHTML = ""; vi.resetAllMocks(); });
function mount(node: ReactNode) { const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container); roots.push(root); flushSync(() => root.render(node)); return { container, render: (next: ReactNode) => flushSync(() => root.render(next)) }; }
function api(baseUrl = "https://backend.example/") { return { backend: { baseUrl }, runtime: {} } as unknown as ElsaStudioModuleApi; }
function fill(element: HTMLInputElement | HTMLSelectElement, value: string) {
  Object.getOwnPropertyDescriptor(element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype, "value")!.set!.call(element, value);
  flushSync(() => element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? "change" : "input", { bubbles: true })));
}
const actor = {} as ActorView;
const trigger: TriggerView = { id: "trigger", revision: 1, name: "End of day", typeKey: "event", schemaVersion: "1", configuration: {}, origin: { kind: "Studio", configurationReadOnly: false }, desiredState: "Paused", effectiveState: "Paused", subscriptionCount: 0, eligibleSubscriptionCount: 0, findings: [], actions: [{ action: "AddSubscription", allowed: true }] };
const definition = { id: "opaque", name: "Reconcile Accounts", createdAt: "2026-01-01T00:00:00Z", lastModifiedAt: "2026-01-01T00:00:00Z", versionCount: 0 };
const details = (name: string) => ({ definition: { ...definition, name } } as Awaited<ReturnType<typeof getDefinition>>);
function client() {
  return { targets: vi.fn().mockResolvedValue({ items: [] }),
    mappingContext: vi.fn(async (_id: string, _revision: number, target: TargetSelector) => ({ target: { ...target, state: "Unpublished", findings: [] }, sourceSchema: { fields: [] }, supportedExpressions: [], findings: [] })),
    preview: vi.fn().mockResolvedValue({ previewId: "review", expiresAt: "2099-01-01T00:00:00Z", observedRevision: 1, commandFingerprint: "digest", canApply: true, findings: [], impact: [] }) } as unknown as TriggerClient;
}
async function choose(container: HTMLElement) {
  const search = container.querySelector<HTMLInputElement>('[role="combobox"][aria-label="Workflow"]');
  expect(search, "The trigger direction offers authorized workflow name search").not.toBeNull();
  fill(search!, "Reconcile");
  await vi.waitFor(() => expect(container.querySelector('[role="option"]')?.textContent).toContain("Reconcile Accounts"));
  flushSync(() => (container.querySelector('[role="option"]') as HTMLElement).click());
}
it("selects a never-published workflow by name and reviews a disabled association using Runtime context", async () => {
  vi.mocked(listDefinitions).mockResolvedValue({ definitions: [definition], page: 1, pageSize: 20, isPaged: true });
  vi.mocked(getDefinition).mockResolvedValue(details("Reconcile Accounts"));
  const transport = client(), context = api(); const { container } = mount(<SubscriptionEditor api={context} client={transport} actor={actor} trigger={trigger} onCancel={vi.fn()} onSaved={vi.fn()} />);
  await choose(container);
  expect(listDefinitions).toHaveBeenCalledWith(context.backend, expect.objectContaining({ search: "Reconcile", state: "all" }));
  await vi.waitFor(() => expect(transport.mappingContext).toHaveBeenCalledWith("trigger", 1, { definitionId: "opaque", slotName: "default" }, [], expect.any(AbortSignal)));
  const review = () => [...container.querySelectorAll("button")].find(button => button.textContent === "Review subscription")!;
  await vi.waitFor(() => expect(review().disabled).toBe(false)); flushSync(() => review().click());
  await vi.waitFor(() => expect(transport.preview).toHaveBeenCalledWith("trigger", 1, { kind: "AddSubscription", target: { definitionId: "opaque", slotName: "default" }, enabled: false, mappings: [], invocationRequest: "None" }, expect.any(AbortSignal)));
});
it("keeps published Runtime target selection usable when Design name search is denied", async () => {
  vi.mocked(listDefinitions).mockRejectedValue(new Error("Design read denied")); vi.mocked(getDefinition).mockRejectedValue(new Error("Design read denied"));
  const transport = client(); vi.mocked(transport.targets).mockResolvedValue({ items: [{ definitionId: "runtime-only", slotName: "blue", displayName: "runtime-only", state: "Available", findings: [] }] });
  const { container } = mount(<SubscriptionEditor api={api()} client={transport} actor={actor} trigger={trigger} onCancel={vi.fn()} onSaved={vi.fn()} />);
  const search = container.querySelector<HTMLInputElement>('[role="combobox"][aria-label="Workflow"]'); expect(search).not.toBeNull(); fill(search!, "Reconcile");
  await vi.waitFor(() => expect(container.textContent).toContain("Design read denied"));
  await vi.waitFor(() => expect(container.querySelector('select[aria-label="Workflow and publication slot"] option[value*=runtime-only]')).not.toBeNull());
  fill(container.querySelector<HTMLSelectElement>('select[aria-label="Workflow and publication slot"]')!, JSON.stringify({ definitionId: "runtime-only", slotName: "blue" }));
  await vi.waitFor(() => expect(transport.mappingContext).toHaveBeenCalledWith("trigger", 1, { definitionId: "runtime-only", slotName: "blue" }, [], expect.any(AbortSignal)));
});
const subscription: SubscriptionView = { id: "subscription", revision: 1, triggerId: "trigger", enabled: false, origin: { kind: "Studio", configurationReadOnly: false }, target: { definitionId: "opaque", slotName: "default" }, mappings: [], invocationGrant: { state: "Absent" }, compatibility: "ValidationPending", findings: [], actions: [] };
it("labels subscription rows with authorized names and retains copyable exact identity", async () => {
  vi.mocked(getDefinition).mockResolvedValue(details("Reconcile Accounts"));
  const transport = { subscriptions: vi.fn().mockResolvedValue({ items: [subscription] }) } as unknown as TriggerClient;
  const { container } = mount(<SubscriptionsSection api={api()} client={transport} actor={actor} trigger={trigger} />);
  await vi.waitFor(() => expect(container.querySelector('th[scope="row"]')?.textContent).toContain("Reconcile Accounts"));
  expect(container.querySelector('[aria-label="Workflow definition ID: opaque"]')).not.toBeNull();
});
it("never queries optional metadata for a redacted subscription target", async () => {
  const transport = { subscriptions: vi.fn().mockResolvedValue({ items: [{ ...subscription, target: { definitionId: "", slotName: "" }, findings: [{ code: "TargetDetailsRestricted", message: "Restricted" }] }] }) } as unknown as TriggerClient;
  const { container } = mount(<SubscriptionsSection api={api()} client={transport} actor={actor} trigger={trigger} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Restricted workflow")); expect(getDefinition).not.toHaveBeenCalled();
});
it("immediately fences an old name when the selected backend changes", async () => {
  let finish!: (value: Awaited<ReturnType<typeof getDefinition>>) => void;
  vi.mocked(getDefinition).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValueOnce(details("Current backend name"));
  const transport = { subscriptions: vi.fn().mockResolvedValue({ items: [subscription] }) } as unknown as TriggerClient;
  const { container, render } = mount(<SubscriptionsSection api={api("https://old.example/")} client={transport} actor={actor} trigger={trigger} />);
  await vi.waitFor(() => expect(getDefinition).toHaveBeenCalledTimes(1)); render(<SubscriptionsSection api={api("https://current.example/")} client={transport} actor={actor} trigger={trigger} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Current backend name")); finish(details("Old backend name")); await Promise.resolve(); expect(container.textContent).not.toContain("Old backend name");
});
