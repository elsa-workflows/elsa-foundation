import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowOrganizationButton } from "../workflow-editor/WorkflowOrganizationButton";

const pending = vi.hoisted(() => [] as Array<() => void>);
vi.mock("../api/capabilities", () => ({ capabilityIds: { workflowDesign: "design" }, hasCapabilityLink: vi.fn(async () => true) }));
vi.mock("../api/workflowOrganization", () => ({ getWorkflowOrganization: vi.fn(async () => ({ permissions: { canManageFolders: false } })) }));
// Only the delayed child-completion boundary is controlled here; real move HTTP success/error is exercised by directBpmnEditor tests.
vi.mock("../workflow-editor/WorkflowTagsDialog", () => ({ WorkflowTagsDialog: ({ onSaved, onClose }: { onSaved(): void; onClose(): void }) =>
  <div role="dialog"><button onClick={() => pending.push(() => { onSaved(); onClose(); })}>Start delayed save</button></div> }));
let root: Root, container: HTMLDivElement;
const saved = vi.fn();
const first = { baseUrl: "https://first.example", http: {} } as StudioEndpointContext;
beforeEach(() => { pending.length = 0; saved.mockReset(); container = document.createElement("div"); document.body.appendChild(container); root = createRoot(container); });
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
function render(context = first, definitionId = "first", mode: "visible" | "hidden" = "visible") {
  flushSync(() => root.render(<React.StrictMode><React.Activity mode={mode}><WorkflowOrganizationButton context={context} definitionId={definitionId} name={definitionId} onSaved={saved} /></React.Activity></React.StrictMode>));
}
async function start() {
  const open = await vi.waitFor(() => { const button = [...container.querySelectorAll("button")].find(node => node.textContent === "Edit tags"); expect(button).toBeDefined(); return button!; });
  flushSync(() => open.click());
  const begin = await vi.waitFor(() => { const button = [...container.querySelectorAll("button")].find(node => node.textContent === "Start delayed save"); expect(button).toBeDefined(); return button!; });
  flushSync(() => begin.click());
}
it.each(["backend", "definition", "retained visit"] as const)("ignores a retired organization completion after %s without closing its replacement", async change => {
  render(); await start(); const old = pending.shift()!;
  if (change === "backend") render({ ...first, baseUrl: "https://second.example" });
  else if (change === "definition") render(first, "second");
  else { render(first, "first", "hidden"); render(); }
  await start(); const current = pending.shift()!;
  flushSync(old);
  expect(saved).not.toHaveBeenCalled();
  expect(container.querySelector('[role="dialog"]')).not.toBeNull();
  flushSync(current);
  expect(saved).toHaveBeenCalledTimes(1);
  expect(container.querySelector('[role="dialog"]')).toBeNull();
});
