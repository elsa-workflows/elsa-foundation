import { expect, it } from "vitest";
import { activityGraphPromotionPayload, workflowPromotionState } from "../graph-authoring/blockPromotionHostDocuments";
import type { NativeBlockDocument } from "../graph-authoring/nativeBlocks";

const document = { rootActivity: { nodeId: "root", activityVersionId: "sequence", inputs: [], outputs: [] }, layout: [], activityPresentation: [] } as NativeBlockDocument;
const outputRead = (producerNodeId: string) => ({ referenceKey: "result", name: "Result", value: { expressionType: "ActivityResult", value: { producerNodeId, projectionKey: "output-1" } } });

it("applies the workflow's rewritten declarations with the replaced root in one state", () => {
  const state = { rootActivity: { nodeId: "old" }, variables: [{ referenceKey: "v" }], inputs: [{ referenceKey: "greeting" }], outputs: [outputRead("inside")], strategyOptions: { mode: "keep" } };
  const rewritten = { variables: state.variables, inputs: state.inputs, outputs: [outputRead("block")], strategyOptions: state.strategyOptions };
  const next = workflowPromotionState(state, document, [rewritten]);
  expect(next.rootActivity).toBe(document.rootActivity);
  expect(next.outputs).toEqual([outputRead("block")]);
  expect(next.strategyOptions).toEqual({ mode: "keep" });
  expect(state.outputs).toEqual([outputRead("inside")]);
  expect(() => workflowPromotionState(state, document, [])).toThrow("did not return the workflow's own declarations");
});

it("applies an Activity Definition graph's rewritten mappings and keeps its other payload members", () => {
  const contract = { contractSchemaVersion: "1", inputs: [], outputs: [{ referenceKey: "out", name: "Out" }], outcomes: [] };
  const payload = { rootActivity: { nodeId: "old" }, variables: [], outputMappings: [{ outputReferenceKey: "out", value: outputRead("inside").value }], outcomeMappings: [{ sourceOutcomeReferenceKey: "Done", boundaryOutcomeReferenceKey: "done" }], extension: "kept" };
  const rewritten = { variables: [], outputMappings: [{ outputReferenceKey: "out", value: outputRead("block").value }], outcomeMappings: payload.outcomeMappings };
  const next = activityGraphPromotionPayload({ ...contract, outputs: [{ name: "Out", referenceKey: "out" }] }, payload, document.rootActivity, [contract, rewritten])!;
  expect(next.rootActivity).toBe(document.rootActivity);
  expect(next.outputMappings).toEqual(rewritten.outputMappings);
  expect(next.outcomeMappings).toEqual(payload.outcomeMappings);
  expect(next.extension).toBe("kept");
});

it("refuses an Activity Definition promotion that would change the activity's own public contract", () => {
  const contract = { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] };
  const changed = { ...contract, outcomes: [{ referenceKey: "done", name: "Finished", isEmitted: true }] };
  expect(activityGraphPromotionPayload(contract, {}, document.rootActivity, [changed, {}])).toBeNull();
});
