import { describe, expect, it } from "vitest";
import { applyWorkflowOperationBatch, parseFrozenWorkflowBatch, WorkflowBatchRefusal } from "../workflowGraphOperationBatch";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import { collectWorkflowContextActivities, collectWorkflowContextConnections, collectWorkflowContextStructure } from "../workflow-editor/editorHelpers";
import { compactWorkflowStructure } from "../workflow-editor/useWorkflowContextBridge";
import { indexActivityPresentation } from "../activityPresentation";
import fixture from "./workflowBatchConformance.json";

// Spec 1193 slice C: the same cases Elsa.Agent.Tests prepares on the server (WorkflowBatchConformanceTests). What the
// server freezes the applier must apply; what the server refuses the applier refuses too, so the two never drift.
const catalog = fixture.catalog as unknown as ActivityCatalogItem[];
const draft = () => structuredClone(fixture.draft) as unknown as WorkflowDraft;

describe("workflow batch contract shared with the server", () => {
  it("shares the snapshot the Studio bridge builds from the shared draft", () => {
    // The same calls useWorkflowContextBridge makes, including the side-table ids that become reservedIds.
    const source = draft();
    const catalogByVersion = new Map(catalog.map(item => [item.activityVersionId, item]));
    const activities = collectWorkflowContextActivities(source.state.rootActivity, catalogByVersion, [], indexActivityPresentation(source.activityPresentation));
    const structure = compactWorkflowStructure(activities, collectWorkflowContextStructure(source.state.rootActivity, catalogByVersion),
      [...(source.layout ?? []), ...(source.activityPresentation ?? [])].map(record => record.nodeId));
    const built = { activities, connections: collectWorkflowContextConnections(source.state.rootActivity, catalogByVersion),
      positions: source.layout.map(item => ({ nodeId: item.nodeId, x: item.x, y: item.y })), structure };

    const { activities: sharedActivities, connections, positions, structure: sharedStructure } = fixture.snapshot;
    expect(built).toEqual({ activities: sharedActivities, connections, positions, structure: sharedStructure });
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
