import { describe, expect, it } from "vitest";
import { applyWorkflowOperationBatch, parseFrozenWorkflowBatch, WorkflowBatchRefusal } from "../workflowGraphOperationBatch";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import { buildWorkflowSnapshotContent } from "../workflow-editor/useWorkflowContextBridge";
import fixture from "./workflowBatchConformance.json";

// Spec 1193 slice C: the same cases Elsa.Weaver.Tests prepares on the server (WorkflowBatchConformanceTests). What the
// server freezes the applier must apply; what the server refuses the applier refuses too, so the two never drift.
const catalog = fixture.catalog as unknown as ActivityCatalogItem[];
const draft = () => structuredClone(fixture.draft) as unknown as WorkflowDraft;

describe("workflow batch contract shared with the server", () => {
  it("shares the snapshot the Studio bridge builds from the shared draft", () => {
    // The very builder useWorkflowContextBridge sends, side-table ids (reservedIds) included.
    const catalogByVersion = new Map(catalog.map(item => [item.activityVersionId, item]));
    const { activities, connections, positions, secretReferences, structure } = buildWorkflowSnapshotContent(draft(), catalogByVersion);
    const shared = fixture.snapshot;
    expect({ activities, connections, positions, secretReferences, structure })
      .toEqual({ activities: shared.activities, connections: shared.connections, positions: shared.positions, secretReferences: shared.secretReferences, structure: shared.structure });
  });

  it("discloses positions only for placed activities and keeps a stale layout record's id reserved", () => {
    // A version promoted after a node was removed keeps that node's layout record (the witness's v3 draft did).
    const stale = draft();
    stale.layout = [...(stale.layout ?? []), { nodeId: "removed-node", x: 1, y: 2 } as (typeof stale.layout)[number]];
    const catalogByVersion = new Map(catalog.map(item => [item.activityVersionId, item]));
    const content = buildWorkflowSnapshotContent(stale, catalogByVersion) as { positions: Array<{ nodeId: string }>; structure: { reservedIds?: string[] } };
    expect(content.positions.map(item => item.nodeId)).toEqual(fixture.snapshot.positions.map(item => item.nodeId));
    expect(content.structure.reservedIds).toContain("removed-node");
  });

  it.each(fixture.cases.filter(item => item.accepted).map(item => [item.name, item] as const))("applies the server's frozen %s batch", (_name, item) => {
    const result = applyWorkflowOperationBatch(draft(), parseFrozenWorkflowBatch(item.frozen), catalog);
    expect(result.appliedCount).toBe(item.frozen!.operations.length);
    const root = result.draft.state.rootActivity!;
    const a = (root.structure!.payload.activities as ActivityNode[]).find(node => node.nodeId === "a")!;
    // The wrapper keeps its memory reference; only the authored value changed.
    expect(a.text).toEqual({ typeName: "String", expression: { type: "Literal", value: "Hello" }, memoryReference: { id: "keep-me" } });
  });

  it.each(fixture.cases.filter(item => !item.accepted).map(item => [item.name, item] as const))("refuses %s as the server does", (_name, item) => {
    const before = draft();
    const refuse = () => applyWorkflowOperationBatch(before, parseFrozenWorkflowBatch(item.clientFrozen), catalog);
    expect(refuse).toThrow(WorkflowBatchRefusal);
    expect(refuse).toThrow(item.clientReason!);
    expect(before).toEqual(draft());
  });
});
