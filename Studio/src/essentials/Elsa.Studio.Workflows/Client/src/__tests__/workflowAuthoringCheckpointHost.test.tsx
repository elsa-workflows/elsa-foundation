import React from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import { useWorkflowDocument } from "../workflow-editor/workflowDocument";
import { useWorkflowAuthoringCheckpoint } from "../workflow-editor/useWorkflowAuthoringCheckpoint";

const mounted: (() => void)[] = [];
afterEach(() => { for (const dispose of mounted.splice(0)) dispose(); });
const draft = (id = "draft"): WorkflowDraft => ({ id, definitionId: "definition", layout: [], validationErrors: [], state: {
  rootActivity: { nodeId: "root", activityVersionId: "version", inputs: [], outputs: [] }
} });
function fixture() {
  const element = document.createElement("div");
  document.body.appendChild(element);
  const root = createRoot(element);
  let api!: { document: ReturnType<typeof useWorkflowDocument>; authoring: ReturnType<typeof useWorkflowAuthoringCheckpoint> };
  const context = { baseUrl: "test://checkpoint" } as StudioEndpointContext;
  function Harness() {
    const document = useWorkflowDocument();
    const authoring = useWorkflowAuthoringCheckpoint({ context, definitionId: "definition", document, canEdit: () => true, lineage: () => ["line"] });
    api = { document, authoring };
    return null;
  }
  flushSync(() => root.render(<Harness />));
  flushSync(() => api.document.loadDraft(draft()));
  mounted.push(() => { flushSync(() => root.unmount()); element.remove(); });
  return { current: () => api };
}

describe("actual workflow resource checkpoint hook", () => {
  it("reads the committed document and line instead of a stale render snapshot", async () => {
    const view = fixture();
    const old = view.current().document.state.draft!;
    flushSync(() => view.current().document.editDraft(({ draft: current }) => ({ ...current!, layout: [{ nodeId: "root", x: 20, y: 30 }] })));
    const prepared = await view.current().authoring.saveCheckpoint.prepare({ snapshot: old, intent: "save", capture: "current", lineage: ["old"] });
    expect(prepared.draft.layout).toEqual([{ nodeId: "root", x: 20, y: 30 }]);
    expect(prepared.lineage).toEqual(["line"]);
  });

  it("revokes queued save authority synchronously on the actual session event", async () => {
    const view = fixture();
    const prepared = await view.current().authoring.saveCheckpoint.prepare({ snapshot: draft(), intent: "save", capture: "current", lineage: [] });
    window.dispatchEvent(new Event(authSessionEndedEvent));
    expect(prepared.assertCurrent).toThrow("authorization");
    await expect(view.current().authoring.saveCheckpoint.prepare({ snapshot: draft(), intent: "save", capture: "current", lineage: [] })).rejects.toThrow("authorization");
  });

  it("does not transfer a prepared save's authority across logout and login", async () => {
    const view = fixture();
    const prepared = await view.current().authoring.saveCheckpoint.prepare({ snapshot: draft(), intent: "save", capture: "current", lineage: [] });
    window.dispatchEvent(new Event(authSessionEndedEvent));
    window.dispatchEvent(new Event(authSessionStartedEvent));
    expect(prepared.assertCurrent).toThrow("authorization");
    const next = await view.current().authoring.saveCheckpoint.prepare({ snapshot: draft(), intent: "save", capture: "current", lineage: [] });
    expect(() => next.assertCurrent?.()).not.toThrow();
  });

  it("retains an old scope's pending controller and blocks a new scope's save", async () => {
    const view = fixture();
    const participant = { readWork: () => ({ pending: true, sequence: 1, acceptedSequence: 0 }), checkpoint: vi.fn() };
    view.current().authoring.getResource().checkpoint.register("native", participant).detach();
    flushSync(() => view.current().document.navigateToScope([{ ownerNodeId: "root", slotId: "nested", label: "Nested" }]));
    expect(view.current().authoring.saveCheckpoint.probe()).toBe(true);
    await expect(view.current().authoring.saveCheckpoint.prepare({ snapshot: draft(), intent: "save", capture: "current", lineage: [] })).rejects.toThrow("Another authoring scope");
    expect(participant.checkpoint).not.toHaveBeenCalled();
  });

  it("does not transfer a pending old draft into a new draft in a cached editor", async () => {
    const view = fixture();
    view.current().authoring.getResource().checkpoint.register("native", { readWork: () => ({ pending: true, sequence: 1, acceptedSequence: 0 }), checkpoint: vi.fn() });
    flushSync(() => view.current().document.loadDraft(draft("other")));
    await expect(view.current().authoring.saveCheckpoint.prepare({ snapshot: draft("other"), intent: "save", capture: "current", lineage: [] })).rejects.toThrow("retained canvas edits");
    expect(view.current().authoring.saveCheckpoint.probe()).toBe(true);
  });

  it("does not perform Undo when a retained participant refuses its checkpoint", async () => {
    const view = fixture();
    view.current().authoring.getResource().checkpoint.register("native", { readWork: () => ({ pending: true, sequence: 1, acceptedSequence: 0 }),
      checkpoint: async () => { throw new Error("Serialization failed; work retained"); } });
    const undo = vi.fn(() => undefined);
    await expect(view.current().authoring.transition("undo", undo)).rejects.toThrow("Serialization failed");
    expect(undo).not.toHaveBeenCalled();
    expect(view.current().authoring.saveCheckpoint.probe()).toBe(true);
  });
});
