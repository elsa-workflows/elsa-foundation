import { describe, expect, it, vi } from "vitest";
import type { WorkflowDraft } from "../workflowTypes";
import type { AuthoringCheckpointCapture, AuthoringCheckpointRegistration } from "../graph-authoring/authoringCheckpoint";
import { createWorkflowAuthoringResource } from "../workflow-editor/workflowAuthoringResource";

function fixture() {
  const draft: WorkflowDraft = { id: "draft", definitionId: "definition", layout: [], validationErrors: [], state: {
    rootActivity: { nodeId: "root", activityVersionId: "exact", inputs: [], outputs: [], displayName: "Before" }
  } };
  let live: AuthoringCheckpointCapture<WorkflowDraft> = { token: { resourceId: "resource", authorityId: "authority", scopeId: "root", revision: 1 }, snapshot: draft, lineage: ["before"] };
  let authorized = true;
  const resource = createWorkflowAuthoringResource({ readLive: () => live, canEdit: () => authorized });
  return { resource, draft, read: () => live, revoke: () => { authorized = false; },
    update: (value: Partial<typeof live>) => { live = { ...live, ...value }; },
    pending() {
      let acceptedSequence = 0;
      const registration: AuthoringCheckpointRegistration = resource.checkpoint.register("canvas", {
        readWork: () => ({ sequence: 1, acceptedSequence, pending: acceptedSequence !== 1 }),
        async checkpoint() {
          live = { token: { ...live.token, revision: live.token.revision + 1 },
            snapshot: { ...draft, state: { ...draft.state, rootActivity: { ...draft.state.rootActivity!, displayName: "Canvas" } } }, lineage: ["canvas-line"] };
          const receipt = registration.acknowledge(1, live.token);
          acceptedSequence = 1;
          return { receipt, release: vi.fn() };
        }
      });
      return registration;
    }
  };
}

describe("workflow resource save checkpoints", () => {
  it("current saves capture the admitted canvas source and its history line", async () => {
    const view = fixture();
    view.pending();
    const saved = await view.resource.prepare({ snapshot: view.draft, intent: "save", capture: "current", lineage: ["stale-line"] });
    expect(saved.draft.state.rootActivity?.displayName).toBe("Canvas");
    expect(saved.lineage).toEqual(["canvas-line"]);
    expect(view.resource.checkpoint.probe()).toBe(false);
    saved.assertCurrent?.();
  });

  it("refuses a reviewed snapshot when pending native work changes its source", async () => {
    const view = fixture();
    view.pending();
    await expect(view.resource.prepare({ snapshot: view.draft, intent: "save", capture: "snapshot", lineage: ["reviewed"] })).rejects.toThrow("Review the updated draft");
    expect(view.read().snapshot.state.rootActivity?.displayName).toBe("Canvas");
  });

  it("keeps an explicit derived snapshot when checkpointing leaves its source unchanged", async () => {
    const view = fixture();
    const derived = { ...view.draft, state: { ...view.draft.state, rootActivity: { ...view.draft.state.rootActivity!, activityVersionId: "new-version" } } };
    const saved = await view.resource.prepare({ snapshot: derived, intent: "save", capture: "snapshot", lineage: ["derived-line"] });
    expect(saved.draft).toBe(derived);
    expect(saved.lineage).toEqual(["derived-line"]);
    expect(view.read().snapshot.state.rootActivity?.activityVersionId).toBe("exact");
  });

  it("rejects another draft before invoking participants", async () => {
    const view = fixture();
    const registration = view.pending();
    await expect(view.resource.prepare({ snapshot: { ...view.draft, id: "other" }, intent: "save", capture: "current", lineage: [] })).rejects.toThrow("another workflow draft");
    expect(view.read().token.revision).toBe(1);
    expect(() => registration.retire()).toThrow();
  });

  it("rechecks authorization after preparation and while queued for HTTP", async () => {
    const view = fixture();
    const saved = await view.resource.prepare({ snapshot: view.draft, intent: "save", capture: "current", lineage: [] });
    view.revoke();
    expect(saved.assertCurrent).toThrow("authorization changed");
    await expect(view.resource.prepare({ snapshot: view.draft, intent: "save", capture: "current", lineage: [] })).rejects.toThrow("authorization changed");
  });

  it("allows later source edits and scope navigation without changing an already captured save", async () => {
    const view = fixture();
    const saved = await view.resource.prepare({ snapshot: view.draft, intent: "save", capture: "current", lineage: [] });
    view.update({ token: { ...view.read().token, revision: 2, scopeId: "nested" }, snapshot: { ...view.draft, layout: [{ nodeId: "root", x: 100, y: 200 }] } });
    expect(() => saved.assertCurrent?.()).not.toThrow();
    expect(saved.draft.layout).toEqual([]);
  });

  it.each(["authorityId", "resourceId"] as const)("does not send an old save under a new %s", async field => {
    const view = fixture();
    const saved = await view.resource.prepare({ snapshot: view.draft, intent: "save", capture: "current", lineage: [] });
    view.update({ token: { ...view.read().token, [field]: "other" } });
    expect(saved.assertCurrent).toThrow("authorization changed");
  });
});
