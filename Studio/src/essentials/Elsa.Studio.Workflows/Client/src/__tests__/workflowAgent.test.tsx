import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { authSessionEndedEvent, createAgentRegistry, type StudioAgentPreparedInvocation, type StudioAgentToolResult, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { registerWorkflowAgent } from "../workflowAgent";
import { WorkflowLayoutResult } from "../workflowAgentResult";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { useWorkflowGraphOperationBatch } from "../workflow-editor/useWorkflowGraphOperationBatch";
import { useWorkflowContextBridge } from "../workflow-editor/useWorkflowContextBridge";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";
import { useWorkflowOperations } from "../workflow-editor/useWorkflowOperations";
import { getDraftRevision } from "../workflow-editor/editorHelpers";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { writeLine } from "./fixtures";
import { updateDraft } from "../api/workflowDesign";
vi.mock("../api/workflowDesign", async importOriginal => ({ ...await importOriginal<typeof import("../api/workflowDesign")>(), updateDraft: vi.fn(async (_context, draft) => draft) }));
const draft: WorkflowDraft = { id: "draft", definitionId: "definition", state: { rootActivity: { nodeId: "write", activityVersionId: writeLine.activityVersionId, inputs: [{ name: "Password", value: "secret-property-value" } as never], outputs: [] } }, layout: [], validationErrors: [] };
const details = { definition: { id: "definition", name: "Test workflow" } } as WorkflowDefinitionDetails;
const noop = () => {};
let agent: ReturnType<typeof createAgentRegistry>, controller: ReturnType<typeof registerWorkflowAgent>, doc: WorkflowDocumentApi;
let editingAllowed: boolean;
let layoutResult: StudioAgentToolResult | undefined;
let persistence: ReturnType<typeof useWorkflowPersistence>, root: Root, container: HTMLDivElement;
let operations: ReturnType<typeof useWorkflowOperations>;
const publicationGet = vi.fn(), publicationPost = vi.fn(), publicationError = vi.fn();
const context = { baseUrl: "https://owned-persistence.example/", http: { getJson: publicationGet, postJson: publicationPost } } as unknown as StudioEndpointContext;
function Probe() {
  doc = useWorkflowDocument();
  persistence = useWorkflowPersistence({ context, draft: doc.state.draft, editDraft: doc.editDraft, setStatus: noop, setError: noop });
  operations = useWorkflowOperations({ context, draft: doc.state.draft, details, catalog: [writeLine], busy: false, saveDraft: persistence.saveDraft, flushPendingSave: persistence.flushPendingSave, reload: async () => {}, startTestRun: noop, setPublishedArtifact: noop, setOperation: noop, setStatus: noop, setError: publicationError, setActiveRightPanelId: noop, setInspectorCollapsed: noop, setAutosavePaused: persistence.setAutosavePaused });
  useWorkflowGraphOperationBatch({ draft: doc.state.draft, details, catalog: [writeLine], document: doc, controller, holdForReview: persistence.holdForReview, reviewHeld: persistence.reviewHeld, canEdit: () => editingAllowed, setStatus: noop, setError: noop });
  useWorkflowContextBridge({ controller, clientId: agent.clientId, details, draft: doc.state.draft, selectedNode: null, selectedNodeId: doc.state.selectedNodeId, selectedDescriptor: null, inspectedNode: null, inspectedDescriptor: null, inspectedIsScopeOwner: false, catalogByVersion: new Map([[writeLine.activityVersionId, writeLine]]) });
  return <><output>{JSON.stringify(doc.state.draft?.layout)}</output>{layoutResult ? <WorkflowLayoutResult controller={controller} proposal={null} result={layoutResult.payload?.data} toolResult={layoutResult} /> : null}</>;
}
function request(): StudioAgentPreparedInvocation {
  return { invocationId: "invocation", sessionId: "session", turnId: "turn", toolCallId: "call", toolName: "workflow_set_designer_position", toolContractVersion: "1", moduleId: "Elsa.Studio.Workflows", actorId: "actor", tenantId: "tenant", target: controller.snapshot!.target!, arguments: { activityId: "write", x: 150, y: 250 }, approvalDigest: "digest", risk: "reviewRequired", preview: { typeId: "workflows.layout-change", schemaVersion: "1", data: {} }, executionLocation: "clientWorkingState", requiresApproval: true, client: controller.snapshot!.client!, expiresAt: new Date(Date.now() + 60000).toISOString() };
}
beforeEach(() => {
  vi.mocked(updateDraft).mockReset().mockImplementation(async (_context, snapshot) => snapshot);
  publicationGet.mockReset().mockRejectedValue(new Error("Publication reads must not start after a failed covering save."));
  publicationPost.mockReset().mockRejectedValue(new Error("Publication mutations must not start after a failed covering save."));
  publicationError.mockReset();
  vi.useFakeTimers(); editingAllowed = true; layoutResult = undefined; agent = createAgentRegistry(); controller = registerWorkflowAgent(agent);
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
  it("permits the rendered whole-change Undo to retry after temporary read-only refusal", async () => {
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); const result = await completion;
    layoutResult = { toolCallId: "call", toolName: "workflow_set_designer_position", succeeded: true, content: result.summary, payload: result.result };
    flushSync(() => root.render(<Probe />));
    const undo = container.querySelector<HTMLButtonElement>("button")!;
    editingAllowed = false;
    flushSync(() => undo.click()); await vi.advanceTimersByTimeAsync(0); flushSync(() => root.render(<Probe />));
    expect(container.querySelector('[role="status"]')?.textContent).toContain("stale");
    expect(doc.state.draft?.layout[0].x).toBe(150);
    expect(undo.disabled).toBe(false);
    editingAllowed = true;
    flushSync(() => undo.click()); await vi.advanceTimersByTimeAsync(0); flushSync(() => root.render(<Probe />));
    expect(doc.state.draft?.layout).toEqual([]); expect(undo.disabled).toBe(true);
    expect(updateDraft).not.toHaveBeenCalled();
  });
  it("disables rendered Undo while pending and after its single native inverse commits", async () => {
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); const result = await completion;
    layoutResult = { toolCallId: "call", toolName: "workflow_set_designer_position", succeeded: true, content: result.summary, payload: result.result };
    flushSync(() => root.render(<Probe />));
    const nativeUndo = controller.undo!; let release!: () => void;
    const ready = new Promise<void>(resolve => { release = resolve; });
    const invoke = vi.fn(async (token: string) => { await ready; return nativeUndo(token); }); controller.undo = invoke;
    const undo = container.querySelector<HTMLButtonElement>("button")!;
    flushSync(() => undo.click()); expect(undo.disabled).toBe(true);
    flushSync(() => undo.click()); expect(invoke).toHaveBeenCalledTimes(1);
    release(); await vi.advanceTimersByTimeAsync(0); flushSync(() => root.render(<Probe />));
    expect(doc.state.draft?.layout).toEqual([]); expect(undo.disabled).toBe(true);
    expect(invoke).toHaveBeenCalledTimes(1);
  });
  it("refuses an old preparation after authorization context disappears", async () => {
    const invocation = request();
    flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent)));
    expect((await controller.execute!(invocation)).outcome).toBe("refused");
    expect(doc.state.draft?.layout).toEqual([]);
  });
  it.each(["save", "publish"])("permits explicit %s of the observed draft without changing the autosave preference", async action => {
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); await completion;
    if (action === "publish") await persistence.flushPendingSave();
    else await persistence.saveDraft(doc.state.draft!, "Saved");
    await vi.advanceTimersByTimeAsync(0);
    expect(updateDraft).toHaveBeenCalledTimes(1);
    expect(updateDraft).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ layout: [{ nodeId: "write", x: 150, y: 250 }] }));
    expect(persistence.reviewHeld).toBe(false);
    expect(persistence.autosaveEnabled).toBe(true);
  });

  it.each(["in-flight", "queued", "stale-callback"])("keeps reviewed B unsaved and undoable after an older %s manual save of A completes", async kind => {
    const snapshotA = structuredClone(doc.state.draft!);
    let resolveA!: (value: WorkflowDraft) => void;
    vi.mocked(updateDraft).mockImplementationOnce(() => new Promise(resolve => { resolveA = resolve; }));
    const saveA = persistence.saveDraft(snapshotA, "Saved");
    const queuedA = kind === "queued" ? persistence.saveDraft(snapshotA, "Saved") : null;
    await Promise.resolve(); flushSync(noop);
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); });
    const applied = await completion;
    const staleA = kind === "stale-callback" ? persistence.saveDraft(snapshotA, "Saved") : null;
    resolveA(snapshotA); await saveA; if (queuedA) await queuedA; if (staleA) await staleA;
    flushSync(noop); await vi.advanceTimersByTimeAsync(10000); flushSync(noop);
    expect.soft(updateDraft).toHaveBeenCalledTimes(kind === "in-flight" ? 1 : 2);
    expect.soft(vi.mocked(updateDraft).mock.calls.filter(([, value]) => value.layout?.some(position => position.x === 150))).toHaveLength(0);
    expect.soft(persistence.reviewHeld).toBe(true);
    expect(persistence.autosaveEnabled).toBe(true);
    let undone!: Promise<string>; flushSync(() => { undone = controller.undo!((applied.result!.data as { undoToken: string }).undoToken); });
    await expect(undone).resolves.toContain("Restored");
    expect(doc.state.draft?.layout).toEqual([]);
  });

  it.each(["save", "publish"])("preserves the hold and whole-change undo when explicit %s persistence fails", async action => {
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); const applied = await completion;
    vi.mocked(updateDraft).mockRejectedValueOnce(new Error("Owning store rejected the save."));
    await expect(action === "publish" ? persistence.flushPendingSave() : persistence.saveDraft(doc.state.draft!, "Saved")).rejects.toThrow("Owning store rejected");
    flushSync(noop); await vi.advanceTimersByTimeAsync(10000); flushSync(noop);
    expect(updateDraft).toHaveBeenCalledTimes(1); expect(persistence.reviewHeld).toBe(true); expect(persistence.autosaveEnabled).toBe(true);
    let undone!: Promise<string>; flushSync(() => { undone = controller.undo!((applied.result!.data as { undoToken: string }).undoToken); });
    await expect(undone).resolves.toContain("Restored"); expect(doc.state.draft?.layout).toEqual([]);
  });

  it.each([true, false])("releases held B only when its queued explicit save succeeds, retaining autosave preference %s", async autosave => {
    flushSync(() => persistence.setAutosaveEnabled(autosave));
    const snapshotA = structuredClone(doc.state.draft!);
    let resolveA!: (value: WorkflowDraft) => void, resolveB!: (value: WorkflowDraft) => void;
    vi.mocked(updateDraft).mockImplementationOnce(() => new Promise(resolve => { resolveA = resolve; }))
      .mockImplementationOnce(() => new Promise(resolve => { resolveB = resolve; }));
    const saveA = persistence.saveDraft(snapshotA, "Saved"); await Promise.resolve(); flushSync(noop);
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); const applied = await completion;
    const snapshotB = structuredClone(doc.state.draft!); const saveB = persistence.saveDraft(snapshotB, "Saved");
    resolveA(snapshotA); await saveA; await Promise.resolve(); flushSync(noop);
    expect(persistence.reviewHeld).toBe(true);
    await vi.advanceTimersByTimeAsync(10000); flushSync(noop); expect(updateDraft).toHaveBeenCalledTimes(2);
    resolveB(snapshotB); await saveB; await vi.advanceTimersByTimeAsync(0); flushSync(noop);
    expect(persistence.reviewHeld).toBe(false); expect(persistence.autosaveEnabled).toBe(autosave);
    await expect(controller.undo!((applied.result!.data as { undoToken: string }).undoToken)).rejects.toThrow("stale");
  });

  it("aborts the real publication caller before preflight, promotion or publish when held B cannot save", async () => {
    let completion!: ReturnType<NonNullable<typeof controller.execute>>;
    flushSync(() => { completion = controller.execute!(request()); }); const applied = await completion;
    vi.mocked(updateDraft).mockRejectedValueOnce(new Error("Owning store rejected B."));
    await operations.preparePublication("publish"); flushSync(noop);
    expect.soft(publicationGet).not.toHaveBeenCalled(); expect(publicationPost).not.toHaveBeenCalled();
    expect(publicationError).toHaveBeenLastCalledWith(expect.stringContaining("Owning store rejected B."));
    expect(operations.publicationReview).toBeNull(); expect(persistence.reviewHeld).toBe(true); expect(persistence.autosavePaused).toBe(false);
    let undone!: Promise<string>; flushSync(() => { undone = controller.undo!((applied.result!.data as { undoToken: string }).undoToken); });
    await expect(undone).resolves.toContain("Restored"); expect(doc.state.draft?.layout).toEqual([]);
  });

});
