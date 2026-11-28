import React, { type ReactNode } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { getDefinition } from "../../api/workflowDesign";
import { DeliveryDetails } from "../Deliveries";
import { ChangeReview } from "../ChangeReview";
import type { TriggerClient } from "../api";
import type { DeliveryView, RecipientView } from "../types";

vi.mock("../../api/workflowDesign", () => ({ getDefinition: vi.fn() }));
let roots: Root[] = [];
afterEach(() => { for (const root of roots) flushSync(() => root.unmount()); roots = []; document.body.innerHTML = ""; vi.resetAllMocks(); });
function mount(node: ReactNode) { const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container); roots.push(root); flushSync(() => root.render(node)); return { container, render: (next: ReactNode) => flushSync(() => root.render(next)) }; }
function api(baseUrl = "https://backend.example/") { return { backend: { baseUrl }, runtime: {} } as unknown as ElsaStudioModuleApi; }
const recipient: RecipientView = { id: "recipient", revision: 1, target: { definitionId: "opaque-workflow", slotName: "default" }, targetDisplayName: "opaque-workflow", admission: "Accepted", dispatch: "Queued", findings: [], actions: [] };
function client(selected = recipient): TriggerClient { return { delivery: vi.fn().mockResolvedValue({ id: "delivery", revision: 1, triggerId: "trigger", triggerRevision: 1, admission: "Accepted", acceptedCount: 1, refusedCount: 0, recipients: [selected], findings: [] } satisfies DeliveryView) } as unknown as TriggerClient; }
const details = (name: string) => ({ definition: { id: "opaque-workflow", name } } as Awaited<ReturnType<typeof getDefinition>>);

it.each(["Accepted", "Refused"] as const)("resolves the actual authorized Design name for %s when the Runtime label is its opaque identity", async admission => {
  vi.mocked(getDefinition).mockResolvedValue(details("Reconcile Accounts")); const context = api();
  const { container } = mount(<DeliveryDetails api={context} client={client({ ...recipient, admission })} deliveryId="delivery" navigate={vi.fn()} />);
  await vi.waitFor(() => expect(container.querySelector("h3")?.textContent).toBe("Reconcile Accounts"));
  expect(getDefinition).toHaveBeenCalledWith(context.backend, "opaque-workflow", expect.any(AbortSignal));
  expect(container.querySelector('[aria-label="Workflow definition ID: opaque-workflow"]')).not.toBeNull();
});
it("retains execution evidence and the exact identity when Design is unavailable or denied", async () => {
  vi.mocked(getDefinition).mockRejectedValue(new Error("Design read is denied"));
  const { container } = mount(<DeliveryDetails api={api()} client={client()} deliveryId="delivery" navigate={vi.fn()} />);
  await vi.waitFor(() => expect(container.querySelector("h3")?.textContent).toBe("opaque-workflow"));
  await vi.waitFor(() => expect(getDefinition).toHaveBeenCalledTimes(1));
  expect(container.textContent).toContain("1 accepted"); expect(container.textContent).toContain("Queued");
  expect(container.querySelector('[aria-label="Workflow definition ID: opaque-workflow"]')).not.toBeNull();
});
it.each(["Accepted", "Refused"] as const)("does not query definition metadata when a %s delivery redacts its target", async admission => {
  const { container } = mount(<DeliveryDetails api={api()} client={client({ ...recipient, admission, target: undefined, targetDisplayName: undefined })} deliveryId="delivery" navigate={vi.fn()} />);
  await vi.waitFor(() => expect(container.querySelector("h3")?.textContent).toBe("recipient"));
  expect(getDefinition).not.toHaveBeenCalled(); expect(container.textContent).not.toContain("opaque-workflow");
});
it("ignores a delayed name from the previous backend context", async () => {
  let finish!: (value: Awaited<ReturnType<typeof getDefinition>>) => void;
  vi.mocked(getDefinition).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValueOnce(details("Current backend name"));
  const transport = client(); const { container, render } = mount(<DeliveryDetails api={api("https://old.example/")} client={transport} deliveryId="delivery" navigate={vi.fn()} />);
  await vi.waitFor(() => expect(getDefinition).toHaveBeenCalledTimes(1));
  render(<DeliveryDetails api={api("https://current.example/")} client={transport} deliveryId="delivery" navigate={vi.fn()} />);
  await vi.waitFor(() => expect(container.querySelector("h3")?.textContent).toBe("Current backend name"));
  finish(details("Old backend name")); await Promise.resolve();
  expect(container.querySelector("h3")?.textContent).toBe("Current backend name");
});

it.each(["resolved", "denied", "restricted"] as const)("uses optional authorized Design names in change impacts: %s", async state => {
  if (state === "resolved") vi.mocked(getDefinition).mockResolvedValue(details("Reconcile Accounts"));
  else vi.mocked(getDefinition).mockRejectedValue(new Error("Design unavailable"));
  const restricted = state === "restricted";
  const transport = { preview: vi.fn().mockResolvedValue({ canApply: true, findings: [], impact: [{ label: restricted ? "Restricted workflow" : "Target", description: restricted ? "Restricted details retained" : "opaque-workflow / default", target: restricted ? undefined : { definitionId: "opaque-workflow", slotName: "default" } }] }) } as unknown as TriggerClient;
  const context = api();
  const { container } = mount(<ChangeReview api={context} client={transport} trigger={{ id: "trigger", name: "End of day", revision: 1 }} command={{ kind: "PauseTrigger" }} onCancel={vi.fn()} onApplied={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain(state === "resolved" ? "Reconcile Accounts" : restricted ? "Restricted workflow" : "Target"));
  if (restricted) { expect(getDefinition).not.toHaveBeenCalled(); expect(container.textContent).not.toContain("opaque-workflow"); }
  else { await vi.waitFor(() => expect(getDefinition).toHaveBeenCalledWith(context.backend, "opaque-workflow", expect.any(AbortSignal))); expect(container.textContent).toContain("opaque-workflow / default"); }
});
