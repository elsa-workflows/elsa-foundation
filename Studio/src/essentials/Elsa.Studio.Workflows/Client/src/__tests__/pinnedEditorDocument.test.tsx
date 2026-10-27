import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { ScopeFrame } from "../workflowAdapter";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { usePinnedEditorDocument } from "../workflow-editor/usePinnedEditorDocument";
import { getDraftSignature } from "../workflow-editor/editorHelpers";
import { useWorkflowGraphOperationBatch } from "../workflow-editor/useWorkflowGraphOperationBatch";
import { workflowGraphOperationApplyEvent, workflowGraphOperationUndoEvent } from "../workflowGraphOperationBatch";
import { writeLine } from "./fixtures";

const snapshot: WorkflowDraft = { id: "draft", definitionId: "definition", state: { rootActivity: { nodeId: "write", activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] } }, layout: [], validationErrors: [] };
const changed: WorkflowDraft = { ...snapshot, layout: [{ nodeId: "write", x: 100, y: 200 }] };
const context = {} as StudioEndpointContext;
let editable: WorkflowDocumentApi;
let workspace: ReturnType<typeof usePinnedEditorDocument>;
let currentContext: StudioEndpointContext;
let root: Root;
let container: HTMLDivElement;
function Probe() {
  editable = useWorkflowDocument();
  workspace = usePinnedEditorDocument({ document: editable, context: currentContext, definitionId: "definition", catalog: [writeLine] });
  const bridge = { draft: editable.state.draft, details: { definition: { id: "definition" } } as WorkflowDefinitionDetails,
    catalog: [writeLine], replaceDraftByBatch: workspace.replaceDraftByBatch, canEdit: workspace.canEdit, setStatus: () => {}, setError: () => {} };
  useWorkflowGraphOperationBatch(bridge);
  return null;
}
beforeEach(() => {
  currentContext = context; container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Probe />));
  flushSync(() => { editable.loadDraft(snapshot); editable.startTestRun({ snapshot, draftSignature: getDraftSignature(snapshot), endpointContext: context,
    view: { testRunId: "test", definitionId: "definition", definitionVersionId: "draft:1", artifactId: "artifact", workflowExecutionId: "run", status: "DispatchAccepted" } }); });
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });

it("presents the exact dispatched snapshot and returns to later edits without replacing the editable document", () => {
  flushSync(() => editable.editDraft(() => changed));
  flushSync(() => { expect(workspace.showSnapshot()).toBe(true); });
  expect(workspace.pinned).toBe(true); expect(workspace.state.draft).toBe(snapshot); expect(editable.state.draft).toBe(changed);
  flushSync(() => workspace.returnToEditing());
  expect(workspace.state.draft).toBe(changed); expect(editable.state.testRun?.snapshot).toBe(snapshot);
});

it("blocks every authoring mutation path while historical presentation is active", () => {
  flushSync(() => workspace.showSnapshot());
  const recipe = vi.fn(() => changed);
  flushSync(() => {
    workspace.editDraft(recipe); workspace.editDraftAndSelect(recipe, "foreign");
    workspace.loadDraft(changed); workspace.replaceDraftByBatch(changed, "foreign");
  });
  expect(recipe).not.toHaveBeenCalled(); expect(editable.state.draft).toBe(snapshot); expect(editable.state.selectedNodeId).toBeNull();
  expect(editable.state.testRun?.view.testRunId).toBe("test");
});

it("keeps historical scope and selection separate from the authoring navigation", () => {
  flushSync(() => editable.select("write"));
  flushSync(() => workspace.showSnapshot());
  const frames = [{ ownerNodeId: "snapshot-container", slotId: "body", label: "Recorded body" }] as ScopeFrame[];
  flushSync(() => workspace.navigateToScope(frames, "recorded-child"));
  expect(workspace.state.selectedNodeId).toBe("recorded-child"); expect(editable.state.selectedNodeId).toBe("write"); expect(editable.state.frames).toEqual([]);
  flushSync(() => workspace.returnToEditing()); expect(workspace.state.selectedNodeId).toBe("write");
  flushSync(() => workspace.showSnapshot()); expect(workspace.state.frames).toBe(frames);
});

it("does not commit a delayed callback from the historical view after returning to editing", () => {
  flushSync(() => workspace.showSnapshot()); const oldEdit = workspace.editDraft;
  flushSync(() => workspace.returnToEditing());
  flushSync(() => oldEdit(() => changed)); expect(editable.state.draft).toBe(snapshot);
  flushSync(() => workspace.editDraft(() => changed)); expect(editable.state.draft).toBe(changed);
});

it("invalidates historical presentation when an endpoint or authorization session changes", () => {
  flushSync(() => workspace.showSnapshot());
  currentContext = {} as StudioEndpointContext; flushSync(() => root.render(<Probe />));
  expect(workspace.pinned).toBe(false); expect(workspace.showSnapshot()).toBe(false);
  currentContext = context; flushSync(() => root.render(<Probe />));
  flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent)));
  expect(workspace.showSnapshot()).toBe(false);
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent)));
  expect(workspace.pinned).toBe(false); flushSync(() => workspace.showSnapshot()); expect(workspace.pinned).toBe(true);
});

it("requires the stored snapshot's exact signature before presenting it", () => {
  flushSync(() => editable.startTestRun({ ...editable.state.testRun!, snapshot: changed }));
  expect(workspace.showSnapshot()).toBe(false); expect(workspace.pinned).toBe(false);
});

it("rejects delayed authoring callbacks from an earlier authorization session", () => {
  const oldEdit = workspace.editDraft;
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent)));
  flushSync(() => oldEdit(() => changed)); expect(editable.state.draft).toBe(snapshot);
  flushSync(() => workspace.editDraft(() => changed)); expect(editable.state.draft).toBe(changed);
});

it("rejects queued graph apply and undo while inspecting, without consuming the editable undo point", () => {
  const apply = () => {
    const respond = vi.fn();
    flushSync(() => window.dispatchEvent(new CustomEvent(workflowGraphOperationApplyEvent, { detail: { respond,
      batch: { schemaVersion: "elsa.workflow-graph-operation-batch.v1", workflowDefinitionId: "definition", operations: [
        { id: "position", kind: "set-designer-position", parameters: { activityId: "write", x: 100, y: 200 } }
      ] } } })));
    return respond.mock.calls[0][0];
  };
  const undo = (undoToken: string) => {
    const respond = vi.fn();
    flushSync(() => window.dispatchEvent(new CustomEvent(workflowGraphOperationUndoEvent, { detail: { respond, undoToken } })));
    return respond.mock.calls[0][0];
  };
  const result = apply(); expect(result.ok).toBe(true);
  const actual = editable.state.draft!;
  flushSync(() => editable.startTestRun({ snapshot: actual, draftSignature: getDraftSignature(actual), endpointContext: context,
    view: { testRunId: "new-test", definitionId: "definition", definitionVersionId: "draft:1", status: "DispatchAccepted" } }));
  flushSync(() => workspace.showSnapshot());
  expect(apply()).toMatchObject({ ok: false, message: expect.stringContaining("Return to editing") });
  expect(undo(result.result.undoToken)).toMatchObject({ ok: false, message: expect.stringContaining("Return to editing") });
  expect(editable.state.draft).toBe(actual);
  flushSync(() => workspace.returnToEditing());
  expect(undo(result.result.undoToken).ok).toBe(true);
  expect(editable.state.draft?.layout).toEqual(snapshot.layout);
});
