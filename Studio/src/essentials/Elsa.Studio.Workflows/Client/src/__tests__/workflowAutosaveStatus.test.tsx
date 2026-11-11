import React, { useEffect, useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import { autosaveDelayMs } from "../workflow-editor/constants";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";
import { useWorkflowDocument } from "../workflow-editor/workflowDocument";

// #4085: the debounced autosave shows "Autosaving..." while it waits. When that pending autosave is dropped before it
// saves (autosave turned off, paused for publication review, or held for review), the status must stop claiming a save.
const api = vi.hoisted(() => ({ save: vi.fn() }));
vi.mock("../api/workflowDesign", () => ({ updateDraft: api.save }));
const context = { baseUrl: "https://backend.example/autosave-status/" } as StudioEndpointContext;
const initial: WorkflowDraft = {
  id: "draft", definitionId: "definition", layout: [], validationErrors: [],
  state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] } }
};
let root: Root, container: HTMLDivElement;
let document: ReturnType<typeof useWorkflowDocument>;
let persistence: ReturnType<typeof useWorkflowPersistence>;
let status = "";
let setStatusFromTest: (value: string) => void;
const noop = () => {};

function Probe() {
  document = useWorkflowDocument();
  const [currentStatus, setStatus] = useState("");
  status = currentStatus; setStatusFromTest = setStatus;
  persistence = useWorkflowPersistence({ context, draft: document.state.draft, editDraft: document.editDraft, setStatus, setError: noop });
  const { markSaved } = persistence, { loadDraft } = document;
  useEffect(() => { markSaved(initial); loadDraft(initial); }, [markSaved, loadDraft]);
  return null;
}
async function act(run: () => unknown) {
  let result: unknown;
  flushSync(() => { result = run(); });
  await result;
  await vi.advanceTimersByTimeAsync(0);
}
async function edit(nodeId: string) {
  await act(() => document.editDraft(({ draft }) => ({ ...draft!, state: { rootActivity: { ...draft!.state.rootActivity!, nodeId } } })));
}

beforeEach(() => {
  vi.useFakeTimers(); vi.resetAllMocks(); status = "";
  api.save.mockImplementation(async (_context, draft) => draft);
  container = window.document.createElement("div"); window.document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Probe />));
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.useRealTimers(); });

describe("autosave status (#4085)", () => {
  it("clears Autosaving when autosave is turned off while the autosave is pending", async () => {
    await edit("edited");
    expect(status).toBe("Autosaving...");
    await act(() => persistence.setAutosaveEnabled(false));
    expect(status).toBe("");
    await act(() => vi.advanceTimersByTimeAsync(autosaveDelayMs));
    expect(api.save).not.toHaveBeenCalled();
    expect(status).toBe("");
  });

  it("clears Autosaving when a publication review pauses the pending autosave", async () => {
    await edit("edited");
    await act(() => persistence.setAutosavePaused(true));
    expect(status).toBe("");
  });

  it("clears Autosaving when a review hold drops the pending autosave", async () => {
    await edit("edited");
    await act(() => persistence.holdForReview(true));
    expect(status).toBe("");
  });

  it("clears Autosaving when a hold taken while the autosave waited in the save queue skips it", async () => {
    let finish!: (draft: WorkflowDraft) => void;
    api.save.mockImplementationOnce(() => new Promise<WorkflowDraft>(resolve => { finish = resolve; }));
    await edit("explicit");
    const snapshot = document.state.draft!;
    const explicit = persistence.saveDraft(snapshot, "Saved");
    await edit("autosaved");
    await act(() => vi.advanceTimersByTimeAsync(autosaveDelayMs));
    expect(status).toBe("Autosaving...");
    // The autosave timer fired and queued its save behind the explicit one; the hold is taken before it runs.
    await act(() => persistence.holdForReview(true));
    await act(async () => { finish(snapshot); await explicit; });
    expect(api.save).toHaveBeenCalledTimes(1);
    expect(status).toBe("");
  });

  it("keeps a status another action wrote while the autosave was pending", async () => {
    await edit("edited");
    await act(() => setStatusFromTest("Exported workflow as JSON."));
    await act(() => persistence.setAutosaveEnabled(false));
    expect(status).toBe("Exported workflow as JSON.");
  });

  it("still reports Autosaved when the pending autosave completes", async () => {
    await edit("edited");
    await act(() => vi.advanceTimersByTimeAsync(autosaveDelayMs));
    expect(api.save).toHaveBeenCalledTimes(1);
    expect(status).toBe("Autosaved");
  });
});
