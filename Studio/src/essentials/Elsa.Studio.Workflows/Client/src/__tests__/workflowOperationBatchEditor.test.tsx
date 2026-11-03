import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createAgentRegistry, type StudioAgentClientExecutionResult, type StudioAgentPreparedInvocation, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { registerWorkflowAgent } from "../workflowAgent";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { useWorkflowGraphOperationBatch } from "../workflow-editor/useWorkflowGraphOperationBatch";
import { useWorkflowContextBridge } from "../workflow-editor/useWorkflowContextBridge";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";
import { useDraftHistory } from "../workflow-editor/useDraftHistory";
import { getDraftContentRevision, getDraftSignature } from "../workflow-editor/editorHelpers";
import { historyCaptureDelayMs } from "../workflow-editor/constants";
import type { FrozenWorkflowBatch, FrozenWorkflowOperation } from "../workflowGraphOperationBatch";
import type { WorkflowDefinitionDetails } from "../workflowTypes";
import { batchCatalog, flowSlot, nestedDraft, sequenceSlot, writer } from "./batchFixtures";
import { updateDraft } from "../api/workflowDesign";

vi.mock("../api/workflowDesign", async importOriginal => ({ ...await importOriginal<typeof import("../api/workflowDesign")>(), updateDraft: vi.fn(async (_context, draft) => draft) }));

// Spec 1193 slice C (FR-008–FR-011, SC-002): a reviewed Operation Batch applied in the real editor hooks — document
// reducer, designer history, persistence and the Weaver context bridge — with fake timers for the history capture delay
// and the autosave interval.

const details = { definition: { id: "definition-1", name: "Nested workflow" } } as WorkflowDefinitionDetails;
const catalogByVersion = new Map(batchCatalog.map(item => [item.activityVersionId, item]));
const context = { baseUrl: "https://owned-persistence.example/", http: {} } as unknown as StudioEndpointContext;
const noop = () => {};
let agent: ReturnType<typeof createAgentRegistry>, controller: ReturnType<typeof registerWorkflowAgent>;
let doc: WorkflowDocumentApi, history: ReturnType<typeof useDraftHistory>, persistence: ReturnType<typeof useWorkflowPersistence>;
let editingAllowed: boolean, root: Root, container: HTMLDivElement, sequence = 0;

function Probe() {
  doc = useWorkflowDocument();
  const current = doc;
  history = useDraftHistory({ draft: doc.state.draft, restoreDraft: snapshot => current.editDraft(() => snapshot) });
  persistence = useWorkflowPersistence({ context, draft: doc.state.draft, editDraft: doc.editDraft, setStatus: noop, setError: noop, lineageHold: history.lineageHold });
  useWorkflowGraphOperationBatch({ draft: doc.state.draft, details, catalog: batchCatalog, document: doc, controller, holdForReview: persistence.holdForReview,
    reviewHeld: persistence.reviewHeld, canEdit: () => editingAllowed, setStatus: noop, setError: noop, history: history.batchHistory });
  useWorkflowContextBridge({ controller, clientId: agent.clientId, details, draft: doc.state.draft, selectedNode: null, selectedNodeId: doc.state.selectedNodeId,
    selectedDescriptor: null, inspectedNode: null, inspectedDescriptor: null, inspectedIsScopeOwner: false, catalogByVersion });
  return null;
}

function load() {
  const draft = nestedDraft();
  flushSync(() => { history.resetHistory(draft); doc.loadDraft(structuredClone(draft)); persistence.markSaved(draft); });
}

beforeEach(() => {
  vi.mocked(updateDraft).mockReset().mockImplementation(async (_context, snapshot) => snapshot);
  vi.useFakeTimers(); editingAllowed = true; agent = createAgentRegistry(); controller = registerWorkflowAgent(agent);
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Probe />)); load();
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.clearAllMocks(); vi.useRealTimers(); });

describe("reviewed Operation Batch in the editor", () => {
  it("applies every operation atomically as one history entry; one Undo restores the exact pre-batch state and one Redo the batch", async () => {
    const before = structuredClone(doc.state.draft!);
    flushSync(() => doc.setPublishedArtifact("artifact-1"));
    const invocation = request(fiveKinds());

    const result = await run(invocation);

    expect(result).toMatchObject({ outcome: "applied", after: { revision: getDraftContentRevision(doc.state.draft!) },
      result: { typeId: "workflows.batch-result", data: { observed: true, historyEntry: true, appliedCount: 6, addedNodeIds: ["greet"], baseRevision: invocation.target!.revision } } });
    expect(result.after!.revision).toMatch(/^sha256:[0-9a-f]{64}$/);
    const applied = structuredClone(doc.state.draft!);
    expect(getDraftSignature(applied)).not.toBe(getDraftSignature(before));
    // The test-run result and the published-artifact association are cleared, and Undo does not restore them.
    expect(doc.state.publishedArtifactId).toBeNull();
    expect(history.lineageHold.isHeld()).toBe(true);

    flushSync(() => history.undo());
    expect(getDraftSignature(doc.state.draft!)).toBe(getDraftSignature(before));
    expect(doc.state.publishedArtifactId).toBeNull();
    expect(history.lineageHold.isHeld()).toBe(false);
    flushSync(() => history.redo());
    expect(getDraftSignature(doc.state.draft!)).toBe(getDraftSignature(applied));
    expect(history.lineageHold.isHeld()).toBe(true);
    expect(updateDraft).not.toHaveBeenCalled();
  });

  it("flushes a hand edit whose capture timer is still armed, so the batch is its own entry", async () => {
    const original = structuredClone(doc.state.draft!);
    flushSync(() => doc.editDraft(({ draft }) => ({ ...draft!, layout: [...draft!.layout, { nodeId: "b", x: 777, y: 777 }] })));
    const handEdited = structuredClone(doc.state.draft!);
    await vi.advanceTimersByTimeAsync(historyCaptureDelayMs - 50); // the capture timer is armed, not fired

    const result = await run(request([op({ kind: "position", nodeId: "a", x: 900, y: 900 })]));
    expect(result.outcome).toBe("applied");
    await vi.advanceTimersByTimeAsync(historyCaptureDelayMs * 4); // the armed timer fires and captures nothing new

    flushSync(() => history.undo());
    expect(getDraftSignature(doc.state.draft!)).toBe(getDraftSignature(handEdited));
    flushSync(() => history.undo());
    expect(getDraftSignature(doc.state.draft!)).toBe(getDraftSignature(original));
    expect(history.canUndoNow).toBe(false);
  });

  it.each(["content", "binding-race", "selection", "second-tab", "remount", "read-only"])("refuses a stale %s binding with zero change", async kind => {
    const invocation = request([op({ kind: "position", nodeId: "a", x: 900, y: 900 })]);
    let expected = structuredClone(doc.state.draft!);
    let result: StudioAgentClientExecutionResult;
    if (kind === "content") {
      flushSync(() => doc.editDraft(({ draft }) => ({ ...draft!, layout: [{ nodeId: "b", x: 1, y: 2 }] })));
      expected = structuredClone(doc.state.draft!);
      result = await run(invocation);
    } else if (kind === "binding-race") {
      // A hand edit queued in the same tick reaches the reducer first: the compare-and-replace refuses.
      let completion!: Promise<StudioAgentClientExecutionResult>;
      flushSync(() => { doc.editDraft(({ draft }) => ({ ...draft!, layout: [{ nodeId: "b", x: 1, y: 2 }] })); completion = controller.execute!(invocation); });
      result = await completion;
      expected = structuredClone(doc.state.draft!);
    } else if (kind === "selection") {
      flushSync(() => doc.select("a"));
      result = await run(invocation);
    } else if (kind === "second-tab") {
      result = await run({ ...invocation, client: { ...invocation.client!, clientId: "another-tab" } });
    } else if (kind === "remount") {
      flushSync(() => root.render(null)); flushSync(() => root.render(<Probe />)); load();
      expected = structuredClone(doc.state.draft!);
      result = await run(invocation);
    } else {
      editingAllowed = false;
      result = await run(invocation);
    }

    expect(result.outcome).toBe("refused");
    expect(getDraftSignature(doc.state.draft!)).toBe(getDraftSignature(expected));
    expect(history.lineageHold.isHeld()).toBe(false);
    if (kind !== "read-only") expect(result.summary).toMatch(/stale/);
  });

  it("fails closed when Studio's live risk recheck disagrees with the reviewed classification", async () => {
    const before = structuredClone(doc.state.draft!);
    const result = await run(request([op({ kind: "remove", nodeId: "b" }, "reviewRequired")], "reviewRequired"));

    expect(result.outcome).toBe("refused");
    expect(result.summary).toContain("recheck");
    expect(getDraftSignature(doc.state.draft!)).toBe(getDraftSignature(before));
  });

  it("refuses a schema violation in the frozen batch with zero change", async () => {
    const invocation = request([op({ kind: "position", nodeId: "a", x: 1, y: 1 })]);
    const operations = (invocation.arguments as FrozenWorkflowBatch).operations as unknown as Array<Record<string, unknown>>;
    operations[0].type = "rewritten";
    const before = structuredClone(doc.state.draft!);

    const result = await run(invocation);

    expect(result.outcome).toBe("refused");
    expect(result.summary).toContain("Unknown field");
    expect(getDraftSignature(doc.state.draft!)).toBe(getDraftSignature(before));
  });

  it("holds autosave across Undo and Redo and never persists batch content without an explicit save", async () => {
    const result = await run(request(fiveKinds()));
    await vi.advanceTimersByTimeAsync(10_000);
    expect(updateDraft).not.toHaveBeenCalled();

    // Undo before the batch entry releases the hold: autosave may persist the pre-batch state only.
    let undone!: Promise<string>;
    flushSync(() => { undone = controller.batch!.undo(token(result)); });
    await expect(undone).resolves.toContain("designer's history");
    expect(history.lineageHold.isHeld()).toBe(false);
    await vi.advanceTimersByTimeAsync(10_000);
    // Redo re-holds; later manual edits inherit the line and stay held.
    flushSync(() => history.redo());
    expect(history.lineageHold.isHeld()).toBe(true);
    flushSync(() => doc.editDraft(({ draft }) => ({ ...draft!, layout: [...draft!.layout, { nodeId: "a", x: 41, y: 41 }] })));
    await vi.advanceTimersByTimeAsync(10_000);
    expect(history.lineageHold.isHeld()).toBe(true);
    expect(vi.mocked(updateDraft).mock.calls.filter(([, value]) => JSON.stringify(value).includes("greet"))).toHaveLength(0);
  });

  it("releases the hold only through the user's explicit save, which marks the covered batch persisted", async () => {
    const result = await run(request(fiveKinds()));
    expect(controller.batch!.state(token(result)).persisted).toBe(false);

    await persistence.saveDraft(doc.state.draft!, "Saved");
    await vi.advanceTimersByTimeAsync(0);
    flushSync(noop);

    expect(updateDraft).toHaveBeenCalledTimes(1);
    expect(JSON.stringify(vi.mocked(updateDraft).mock.calls[0][1])).toContain("greet");
    expect(history.lineageHold.isHeld()).toBe(false);
    expect(controller.batch!.state(token(result)).persisted).toBe(true);
  });

  it("disables the review card's Undo with a reason after a later edit; designer history stays the path", async () => {
    const result = await run(request([op({ kind: "position", nodeId: "a", x: 900, y: 900 })]));
    expect(controller.batch!.state(token(result)).status).toBe("available");

    flushSync(() => doc.editDraft(({ draft }) => ({ ...draft!, layout: [...draft!.layout, { nodeId: "b", x: 300, y: 300 }] })));
    const later = structuredClone(doc.state.draft!);
    expect(controller.batch!.state(token(result))).toMatchObject({ status: "unavailable", reason: expect.stringContaining("A later edit exists") });
    await expect(controller.batch!.undo(token(result))).rejects.toThrow("A later edit exists");
    expect(getDraftSignature(doc.state.draft!)).toBe(getDraftSignature(later));
    await vi.advanceTimersByTimeAsync(historyCaptureDelayMs * 2);
    expect(controller.batch!.state(token(result)).status).toBe("unavailable");
  });

  it("keeps the hold keyed to lineage when the 50-entry cap drops the batch entry", async () => {
    await run(request(fiveKinds()));
    for (let index = 0; index < 60; index++) {
      flushSync(() => doc.editDraft(({ draft }) => ({ ...draft!, layout: [...draft!.layout.filter(record => record.nodeId !== "b"), { nodeId: "b", x: 100 + index, y: 100 }] })));
      await vi.advanceTimersByTimeAsync(historyCaptureDelayMs + 10);
    }
    for (let index = 0; index < 60; index++) flushSync(() => history.undo());

    // The oldest entries, the batch's own among them, were dropped; every remaining entry still carries the batch.
    expect(JSON.stringify(doc.state.draft)).toContain("greet");
    expect(history.lineageHold.isHeld()).toBe(true);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(updateDraft).not.toHaveBeenCalled();
  });

  it("keeps the scope frame and moves the inspector to the surviving ancestor scope when the selected node is removed", async () => {
    flushSync(() => doc.navigateToScope([{ ownerNodeId: "seq", slotId: sequenceSlot, label: "Inner sequence / Activities" }], "inner"));
    const invocation = request([op({ kind: "remove", nodeId: "inner" }, "destructive")], "destructive");

    const result = await run(invocation);

    expect(result.outcome).toBe("applied");
    expect(doc.state.frames).toEqual([{ ownerNodeId: "seq", slotId: sequenceSlot, label: "Inner sequence / Activities" }]);
    expect(doc.state.selectedNodeId).toBeNull();
    // The receipt answered the originating binding; the selection change then rotated it, as expected.
    expect(controller.snapshot?.client?.generationId).not.toBe(invocation.client!.generationId);
  });

  it("returns to the nearest existing ancestor scope when the viewed container is removed", async () => {
    flushSync(() => doc.navigateToScope([{ ownerNodeId: "seq", slotId: sequenceSlot, label: "Inner sequence / Activities" }], null));
    const result = await run(request([op({ kind: "remove", nodeId: "seq" }, "destructive")], "destructive"));

    expect(result.outcome).toBe("applied");
    expect(doc.state.frames).toEqual([]);
    expect(doc.state.selectedNodeId).toBeNull();
  });

  it("preserves a surviving selection", async () => {
    flushSync(() => doc.select("a"));
    const result = await run(request([op({ kind: "position", nodeId: "b", x: 200, y: 200 })]));
    expect(result.outcome).toBe("applied");
    expect(doc.state.selectedNodeId).toBe("a");
  });

  it("discloses structure, the working-state kind and visible Secret References, but no Secret value", async () => {
    await run(request([op({ kind: "set-property", nodeId: "b", property: "apiKey", inputName: "ApiKey", typeName: "String", value: { secretReference: { name: "smtp", typeName: "ApiKey", scope: null } } })]));
    const attachment = controller.snapshot!;
    expect(attachment.target!.revision).toBe(getDraftContentRevision(doc.state.draft!));
    const content = attachment.content as { activities: Array<{ id: string }>; structure: { versions: Array<{ id: string; nodes: number[] }>; containers: Array<Record<string, unknown>> } };
    expect(content).toMatchObject({ workingState: "draft", secretReferences: [{ name: "smtp", typeName: "ApiKey", scope: null }] });
    const index = (id: string) => content.activities.findIndex(activity => activity.id === id);
    // Compact: the root and each container with its slots' member indexes, and indexes grouped by exact version.
    expect(content.structure.containers).toEqual([
      { id: "flow", parentId: null, slot: null, slots: [{ id: flowSlot, mode: "flowchart", cardinality: "many", nodes: [index("a"), index("b"), index("seq")] }] },
      { id: "seq", parentId: "flow", slot: flowSlot, slots: [{ id: sequenceSlot, mode: "sequence", cardinality: "many", nodes: [index("inner")] }] }
    ]);
    expect(content.structure.versions.find(group => group.id === writer.activityVersionId)?.nodes.sort()).toEqual([index("a"), index("b"), index("inner")].sort());
    expect(JSON.stringify(content)).not.toContain("activityVersionId");
  });
});

function fiveKinds(): FrozenWorkflowOperation[] {
  return [
    op({ kind: "add", nodeId: "greet", activityVersionId: writer.activityVersionId, activityTypeKey: writer.activityTypeKey, version: "1.0.0", scopePath: ["flow", "seq"], slot: sequenceSlot, position: null }),
    op({ kind: "set-property", nodeId: "greet", property: "text", inputName: "Text", typeName: "String", value: { literal: "Hello" } }),
    op({ kind: "update", nodeId: "a", fields: { displayName: "Say hi" }, startInput: null }),
    op({ kind: "position", nodeId: "b", x: 400, y: 400 }),
    op({ kind: "connect", source: "a", target: "seq", sourcePort: "Done", targetPort: null, scopeOwnerId: "flow", slot: flowSlot }),
    op({ kind: "disconnect", source: "a", target: "b", sourcePort: "Done" }, "destructive")
  ];
}

function op<T extends Omit<FrozenWorkflowOperation, "index" | "risk">>(operation: T, risk: "reviewRequired" | "destructive" = "reviewRequired") {
  return { index: -1, risk, ...operation } as unknown as FrozenWorkflowOperation;
}

function request(operations: FrozenWorkflowOperation[], risk?: "reviewRequired" | "destructive"): StudioAgentPreparedInvocation {
  const indexed = operations.map((operation, index) => ({ ...operation, index }));
  const batchRisk = indexed.some(item => item.risk === "destructive") ? "destructive" : "reviewRequired";
  const frozen: FrozenWorkflowBatch = { schemaVersion: "1", highComplexity: false, risk: batchRisk, operations: indexed };
  sequence++;
  return { invocationId: `invocation-${sequence}`, sessionId: "session", turnId: "turn", toolCallId: `call-${sequence}`, toolName: "workflow_apply_operation_batch",
    toolContractVersion: "1", moduleId: "Elsa.Studio.Workflows", actorId: "actor", tenantId: "tenant", target: controller.snapshot!.target!, arguments: frozen,
    approvalDigest: "digest", risk: risk ?? batchRisk, preview: { typeId: "workflows.batch-result", schemaVersion: "1", data: {} }, executionLocation: "clientWorkingState",
    requiresApproval: true, client: controller.snapshot!.client!, expiresAt: new Date(Date.now() + 60_000).toISOString() };
}

async function run(invocation: StudioAgentPreparedInvocation) {
  let completion!: Promise<StudioAgentClientExecutionResult>;
  flushSync(() => { completion = controller.execute!(invocation); });
  return completion;
}

function token(result: StudioAgentClientExecutionResult) {
  return (result.result!.data as { undoToken: string }).undoToken;
}
