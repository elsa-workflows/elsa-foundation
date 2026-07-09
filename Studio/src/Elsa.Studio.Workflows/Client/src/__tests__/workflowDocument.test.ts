import { describe, expect, it } from "vitest";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import type { WorkflowTestRunState } from "../workflow-editor/editorTypes";
import {
  initialWorkflowDocumentState,
  workflowDocumentReducer,
  type WorkflowDocumentState
} from "../workflow-editor/workflowDocument";

function makeActivity(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v1`, inputs: [], outputs: [] };
}

function makeDraft(id: string, rootNodeId = "root"): WorkflowDraft {
  return {
    id,
    definitionId: "def-1",
    state: { rootActivity: makeActivity(rootNodeId) },
    layout: [],
    validationErrors: []
  };
}

const testRun: WorkflowTestRunState = {
  draftSignature: "sig",
  view: { testRunId: "tr", definitionId: "def-1", definitionVersionId: "v1", status: "Running" }
};

// A "dirty" starting state so we can assert exactly which fields each transition resets.
function dirtyState(): WorkflowDocumentState {
  return {
    draft: makeDraft("draft-a"),
    frames: [{ ownerNodeId: "owner", slotId: "body", label: "Owner / Body" }],
    selectedNodeId: "node-1",
    testRun,
    publishedArtifactId: "artifact-1"
  };
}

describe("workflowDocumentReducer", () => {
  it("starts empty", () => {
    expect(initialWorkflowDocumentState).toEqual({
      draft: null,
      frames: [],
      selectedNodeId: null,
      testRun: null,
      publishedArtifactId: null
    });
  });

  it("draftLoaded resets scope + selection but preserves testRun and publishedArtifact", () => {
    const draft = makeDraft("draft-b");
    const next = workflowDocumentReducer(dirtyState(), { type: "draftLoaded", draft });
    expect(next.draft).toBe(draft);
    expect(next.frames).toEqual([]);
    expect(next.selectedNodeId).toBeNull();
    // Preserved so a post-promote reload keeps the just-published artifact and a stale run self-invalidates.
    expect(next.testRun).toBe(testRun);
    expect(next.publishedArtifactId).toBe("artifact-1");
  });

  it("draftLoaded can clear the draft", () => {
    const next = workflowDocumentReducer(dirtyState(), { type: "draftLoaded", draft: null });
    expect(next.draft).toBeNull();
  });

  it("draftReplacedByBatch resets scope, selection, testRun and publishedArtifact", () => {
    const draft = makeDraft("draft-c");
    const next = workflowDocumentReducer(dirtyState(), { type: "draftReplacedByBatch", draft, selectedNodeId: "last-node" });
    expect(next.draft).toBe(draft);
    expect(next.frames).toEqual([]);
    expect(next.selectedNodeId).toBe("last-node");
    expect(next.testRun).toBeNull();
    expect(next.publishedArtifactId).toBeNull();
  });

  it("draftEdited applies the recipe and preserves scope, selection, testRun and artifact", () => {
    const start = dirtyState();
    const edited = makeDraft("draft-a", "root-edited");
    const next = workflowDocumentReducer(start, { type: "draftEdited", recipe: () => edited });
    expect(next.draft).toBe(edited);
    expect(next.frames).toBe(start.frames);
    expect(next.selectedNodeId).toBe("node-1");
    expect(next.testRun).toBe(testRun);
    expect(next.publishedArtifactId).toBe("artifact-1");
  });

  it("draftEdited leaves state untouched when the recipe returns null", () => {
    const start = dirtyState();
    const next = workflowDocumentReducer(start, { type: "draftEdited", recipe: () => null });
    expect(next).toBe(start);
  });

  it("draftEdited recipe receives the current state", () => {
    const start = dirtyState();
    let received: WorkflowDocumentState | null = null;
    workflowDocumentReducer(start, { type: "draftEdited", recipe: state => { received = state; return null; } });
    expect(received).toBe(start);
  });

  it("draftEditedAndSelected updates draft and selection together", () => {
    const start = dirtyState();
    const edited = makeDraft("draft-a", "root-edited");
    const next = workflowDocumentReducer(start, { type: "draftEditedAndSelected", recipe: () => edited, selectedNodeId: "new-node" });
    expect(next.draft).toBe(edited);
    expect(next.selectedNodeId).toBe("new-node");
    expect(next.frames).toBe(start.frames);
  });

  it("draftEditedAndSelected still moves selection when the recipe returns null", () => {
    const start = dirtyState();
    const next = workflowDocumentReducer(start, { type: "draftEditedAndSelected", recipe: () => null, selectedNodeId: "new-node" });
    expect(next.draft).toBe(start.draft);
    expect(next.selectedNodeId).toBe("new-node");
  });

  it("selectionChanged sets the selected node and no-ops when unchanged", () => {
    const start = dirtyState();
    const changed = workflowDocumentReducer(start, { type: "selectionChanged", selectedNodeId: "node-2" });
    expect(changed.selectedNodeId).toBe("node-2");

    const unchanged = workflowDocumentReducer(start, { type: "selectionChanged", selectedNodeId: "node-1" });
    expect(unchanged).toBe(start);
  });

  it("scopeNavigated replaces frames and selection", () => {
    const frames = [{ ownerNodeId: "o2", slotId: "s2", label: "L2" }];
    const next = workflowDocumentReducer(dirtyState(), { type: "scopeNavigated", frames, selectedNodeId: "node-3" });
    expect(next.frames).toBe(frames);
    expect(next.selectedNodeId).toBe("node-3");
  });

  it("slotEntered appends a frame and clears selection by default", () => {
    const start = dirtyState();
    const frame = { ownerNodeId: "o3", slotId: "s3", label: "L3" };
    const next = workflowDocumentReducer(start, { type: "slotEntered", frame, selectedNodeId: null });
    expect(next.frames).toEqual([...start.frames, frame]);
    expect(next.selectedNodeId).toBeNull();
  });

  it("slotEntered can land with a child of the entered slot pre-selected", () => {
    const start = dirtyState();
    const frame = { ownerNodeId: "o3", slotId: "s3", label: "L3" };
    const next = workflowDocumentReducer(start, { type: "slotEntered", frame, selectedNodeId: "child-1" });
    expect(next.frames).toEqual([...start.frames, frame]);
    expect(next.selectedNodeId).toBe("child-1");
  });

  it("test run transitions set and clear the run", () => {
    const empty = { ...dirtyState(), testRun: null };
    const started = workflowDocumentReducer(empty, { type: "testRunStarted", testRun });
    expect(started.testRun).toBe(testRun);

    const cleared = workflowDocumentReducer(started, { type: "testRunCleared" });
    expect(cleared.testRun).toBeNull();

    // Clearing an already-empty run is a no-op reference.
    expect(workflowDocumentReducer(empty, { type: "testRunCleared" })).toBe(empty);
  });

  it("publishedArtifactChanged sets and clears the artifact id", () => {
    const set = workflowDocumentReducer(dirtyState(), { type: "publishedArtifactChanged", publishedArtifactId: "artifact-2" });
    expect(set.publishedArtifactId).toBe("artifact-2");

    const cleared = workflowDocumentReducer(set, { type: "publishedArtifactChanged", publishedArtifactId: null });
    expect(cleared.publishedArtifactId).toBeNull();
  });

  it("does not mutate the input state", () => {
    const start = dirtyState();
    const before = JSON.stringify(start);
    workflowDocumentReducer(start, { type: "draftReplacedByBatch", draft: makeDraft("x"), selectedNodeId: "n" });
    workflowDocumentReducer(start, { type: "slotEntered", frame: { ownerNodeId: "o", slotId: "s", label: "l" }, selectedNodeId: null });
    expect(JSON.stringify(start)).toBe(before);
  });
});
