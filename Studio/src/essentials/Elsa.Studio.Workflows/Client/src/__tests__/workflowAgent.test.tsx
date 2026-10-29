import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { authSessionEndedEvent, createAgentRegistry, type StudioAgentPreparedInvocation, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { registerWorkflowAgent } from "../workflowAgent";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { useWorkflowGraphOperationBatch } from "../workflow-editor/useWorkflowGraphOperationBatch";
import { useWorkflowContextBridge } from "../workflow-editor/useWorkflowContextBridge";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";
import { getDraftRevision } from "../workflow-editor/editorHelpers";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { writeLine } from "./fixtures";
import { updateDraft } from "../api/workflowDesign";
vi.mock("../api/workflowDesign", () => ({ updateDraft: vi.fn(async (_context, draft) => draft) }));
const draft: WorkflowDraft = { id: "draft", definitionId: "definition", state: { rootActivity: { nodeId: "write", activityVersionId: writeLine.activityVersionId, inputs: [{ name: "Password", value: "secret-property-value" } as never], outputs: [] } }, layout: [], validationErrors: [] };
const details = { definition: { id: "definition", name: "Test workflow" } } as WorkflowDefinitionDetails;
const noop = () => {};
let agent: ReturnType<typeof createAgentRegistry>, controller: ReturnType<typeof registerWorkflowAgent>, doc: WorkflowDocumentApi;
let editingAllowed: boolean;
let persistence: ReturnType<typeof useWorkflowPersistence>, root: Root, container: HTMLDivElement;
function Probe() {
  doc = useWorkflowDocument();
  persistence = useWorkflowPersistence({ context: {} as StudioEndpointContext, draft: doc.state.draft, editDraft: doc.editDraft, setStatus: noop, setError: noop });
  useWorkflowGraphOperationBatch({ draft: doc.state.draft, details, catalog: [writeLine], document: doc, controller, holdForReview: persistence.holdForReview, reviewHeld: persistence.reviewHeld, canEdit: () => editingAllowed, setStatus: noop, setError: noop });
  useWorkflowContextBridge({ controller, clientId: agent.clientId, details, draft: doc.state.draft, selectedNode: null, selectedNodeId: doc.state.selectedNodeId, selectedDescriptor: null, inspectedNode: null, inspectedDescriptor: null, inspectedIsScopeOwner: false, catalogByVersion: new Map([[writeLine.activityVersionId, writeLine]]) });
  return <output>{JSON.stringify(doc.state.draft?.layout)}</output>;
}
function request(): StudioAgentPreparedInvocation {
  return { invocationId: "invocation", sessionId: "session", turnId: "turn", toolCallId: "call", toolName: "workflow_set_designer_position", toolContractVersion: "1", moduleId: "Elsa.Studio.Workflows", actorId: "actor", tenantId: "tenant", target: controller.snapshot!.target!, arguments: { activityId: "write", x: 150, y: 250 }, approvalDigest: "digest", risk: "reviewRequired", preview: { typeId: "workflows.layout-change", schemaVersion: "1", data: {} }, executionLocation: "clientWorkingState", requiresApproval: true, client: controller.snapshot!.client!, expiresAt: new Date(Date.now() + 60000).toISOString() };
}
beforeEach(() => {
  vi.useFakeTimers(); editingAllowed = true; agent = createAgentRegistry(); controller = registerWorkflowAgent(agent);
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Probe />)); flushSync(() => { doc.loadDraft(structuredClone(draft)); persistence.markSaved(draft); });
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.clearAllMocks(); vi.useRealTimers(); });
describe("native reviewed workflow effect", () => {
  it("exposes bounded exact context without property values", async () => {
    const context = await agent.contextProviders.list()[0].collect({ surface: { route: "/workflows" } });
    expect(context).toMatchObject({ attachments: [{ target: { resourceId: "definition", revision: getDraftRevision(draft) }, content: { activities: [{ id: "write" }] } }] });
    expect(JSON.stringify(context)).not.toContain("secret-property-value");
  });
  it("observes atomic editor state, holds autosave and undoes the whole change", async () => {
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); });
    const result = await completion;
    expect(result).toMatchObject({ outcome: "applied", result: { data: { observed: true, revision: getDraftRevision(doc.state.draft!) } } });
    expect(container.textContent).toContain('"x":150'); expect(persistence.reviewHeld).toBe(true);
    await vi.advanceTimersByTimeAsync(10000); expect(updateDraft).not.toHaveBeenCalled();
    const token = (result.result!.data as { undoToken: string }).undoToken;
    let undone!: Promise<string>; flushSync(() => { undone = controller.undo!(token); }); await undone;
    expect(doc.state.draft?.layout).toEqual([]);
  });
  it("refuses a queued revision race at the reducer without clobbering edits", async () => {
    const invocation = request(); let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { doc.editDraft(current => ({ ...current.draft!, layout: [{ nodeId: "write", x: 1, y: 2 }] })); completion = controller.execute!(invocation); });
    expect((await completion).outcome).toBe("refused"); expect(doc.state.draft?.layout).toEqual([{ nodeId: "write", x: 1, y: 2 }]);
  });
  it("refuses stale generation and guarded undo after an intervening edit", async () => {
    const invalid = request(); invalid.client = { ...invalid.client!, generationId: "vanished" };
    expect((await controller.execute!(invalid)).outcome).toBe("refused");
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); const result = await completion;
    flushSync(() => doc.editDraft(current => ({ ...current.draft!, layout: [{ nodeId: "write", x: 9, y: 10 }] })));
    await expect(controller.undo!((result.result!.data as { undoToken: string }).undoToken)).rejects.toThrow("stale");
    expect(doc.state.draft?.layout[0].x).toBe(9);
  });
  it("does not consume the native whole-change undo point while editing is unavailable", async () => {
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); const result = await completion;
    const token = (result.result!.data as { undoToken: string }).undoToken;
    editingAllowed = false;
    await expect(controller.undo!(token)).rejects.toThrow("stale");
    expect(doc.state.draft?.layout[0].x).toBe(150);
    editingAllowed = true;
    let undone!: Promise<string>; flushSync(() => { undone = controller.undo!(token); }); await undone;
    expect(doc.state.draft?.layout).toEqual([]);
  });
  it("refuses an old preparation after authorization context disappears", async () => {
    const invocation = request();
    flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent)));
    expect((await controller.execute!(invocation)).outcome).toBe("refused");
    expect(doc.state.draft?.layout).toEqual([]);
  });
  it("permits explicit save of the observed draft while automatic persistence is held", async () => {
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); await completion;
    await persistence.saveDraft(doc.state.draft!, "Saved");
    await vi.advanceTimersByTimeAsync(0);
    expect(updateDraft).toHaveBeenCalledTimes(1);
    expect(updateDraft).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ layout: [{ nodeId: "write", x: 150, y: 250 }] }));
    expect(persistence.reviewHeld).toBe(false);
  });

});
