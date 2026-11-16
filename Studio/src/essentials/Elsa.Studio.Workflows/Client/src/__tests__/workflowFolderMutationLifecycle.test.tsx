import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { RenameWorkflowFolderDialog, MoveWorkflowFolderDialog } from "../workflow-editor/WorkflowFolderMutationDialogs";
import { listWorkflowFolders, moveWorkflowFolder, renameWorkflowFolder } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
vi.mock("../api/workflowDesign", () => ({ listWorkflowFolders: vi.fn(), moveWorkflowFolder: vi.fn(), renameWorkflowFolder: vi.fn(), getWorkflowFolder: vi.fn() }));
let root: Root, container: HTMLDivElement;
const folder: WorkflowFolder = { id: "first", name: "First", parentId: null, normalizedName: "FIRST", revision: "1", createdAt: "", lastModifiedAt: "" };
const context = { baseUrl: "https://first.example", http: {} } as StudioEndpointContext;
const onSaved = vi.fn(), onClose = vi.fn();
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(listWorkflowFolders).mockResolvedValue({ items: [], nextContinuationToken: null });
  container = document.createElement("div"); document.body.appendChild(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
function render(kind: "rename" | "move", mode: "visible" | "hidden" = "visible", target = folder, endpoint = context) {
  flushSync(() => root.render(<React.Activity mode={mode}>{kind === "rename"
    ? <RenameWorkflowFolderDialog context={endpoint} folder={target} onRenamed={onSaved} onClose={onClose} />
    : <MoveWorkflowFolderDialog context={endpoint} folder={target} onMoved={onSaved} onClose={onClose} />}</React.Activity>));
}
function submit() { flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }))); }
it.each(["rename", "move"] as const)("reconciles an accepted %s after hiding and showing without reviving toast or focus", async kind => {
  let finish!: () => void;
  const work = new Promise<void>(resolve => { finish = resolve; });
  const mutation = kind === "rename" ? vi.mocked(renameWorkflowFolder) : vi.mocked(moveWorkflowFolder);
  mutation.mockReturnValue(work);
  render(kind); submit();
  expect(mutation).toHaveBeenCalledTimes(1);
  render(kind, "hidden"); render(kind);
  expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true);
  finish(); await work;
  await vi.waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
  expect(onClose).toHaveBeenCalledWith(false);
  const canNotify = onSaved.mock.calls[0][kind === "rename" ? 0 : 2] as () => boolean;
  expect(canNotify()).toBe(false);
  expect(mutation).toHaveBeenCalledTimes(1);
});
it.each(["rename", "move"] as const)("rejects a %s completion belonging to another backend", async kind => {
  let finish!: () => void;
  const work = new Promise<void>(resolve => { finish = resolve; });
  const mutation = kind === "rename" ? vi.mocked(renameWorkflowFolder) : vi.mocked(moveWorkflowFolder);
  mutation.mockReturnValue(work);
  render(kind); submit();
  render(kind, "visible", folder, { ...context, baseUrl: "https://second.example" });
  finish(); await work; await Promise.resolve();
  expect(onSaved).not.toHaveBeenCalled(); expect(onClose).not.toHaveBeenCalled();
});
it.each(["rename", "move"] as const)("keeps a current %s failure inline and completes a successful retry", async kind => {
  const mutation = kind === "rename" ? vi.mocked(renameWorkflowFolder) : vi.mocked(moveWorkflowFolder);
  mutation.mockRejectedValueOnce(new Error("Mutation unavailable")).mockResolvedValue(undefined);
  render(kind); submit();
  await vi.waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent).toContain("Mutation unavailable"));
  expect(onSaved).not.toHaveBeenCalled();
  submit();
  await vi.waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
  expect(onClose).toHaveBeenCalledTimes(1);
});
