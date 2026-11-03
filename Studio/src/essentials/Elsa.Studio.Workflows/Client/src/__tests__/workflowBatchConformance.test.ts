import { describe, expect, it } from "vitest";
import { applyWorkflowOperationBatch, parseFrozenWorkflowBatch, WorkflowBatchRefusal } from "../workflowGraphOperationBatch";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import fixture from "./workflowBatchConformance.json";

// Spec 1193 slice C: the same cases Elsa.Agent.Tests prepares on the server (WorkflowBatchConformanceTests). What the
// server freezes the applier must apply; what the server refuses the applier refuses too, so the two never drift.
const catalog = fixture.catalog as unknown as ActivityCatalogItem[];
const draft = () => structuredClone(fixture.draft) as unknown as WorkflowDraft;

describe("workflow batch contract shared with the server", () => {
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
    expect(() => applyWorkflowOperationBatch(before, parseFrozenWorkflowBatch(item.clientFrozen), catalog)).toThrow(WorkflowBatchRefusal);
    expect(before).toEqual(draft());
  });
});
