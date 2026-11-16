import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import { flowchartActivity, sequenceActivity, writeLine } from "./fixtures";

// Shared fixtures for the reviewed Operation Batch (spec 1193 slice C) applier and editor tests.

export const flowSlot = "elsa.flowchart.structure:activities";
export const sequenceSlot = "elsa.sequence.structure:activities";
export const writer: ActivityCatalogItem = {
  ...writeLine,
  inputs: [
    { referenceKey: "text", name: "Text", typeName: "String" },
    { referenceKey: "apiKey", name: "ApiKey", typeName: "String", isCredential: true },
    { referenceKey: "canStartWorkflow", name: "CanStartWorkflow", typeName: "Boolean" }
  ]
};
export const retired: ActivityCatalogItem = { ...writeLine, activityVersionId: "retired-v1", activityTypeKey: "Acme.Retired", available: false, availabilityReason: "Excluded by policy." };
export const batchCatalog = [flowchartActivity, sequenceActivity, writer, retired];

/** A root flowchart holding two Write Line nodes connected a → b and a sequence that holds one Write Line. */
export function nestedDraft(): WorkflowDraft {
  const node = (nodeId: string, activity: ActivityCatalogItem, payload?: Record<string, unknown>): ActivityNode => ({
    nodeId, activityVersionId: activity.activityVersionId, inputs: [], outputs: [],
    ...(payload ? { structure: { kind: (activity.designFacets![0] as { kind: string }).kind, schemaVersion: "1.0.0", payload } } : {})
  });
  return {
    id: "draft-1", definitionId: "definition-1", sourceVersionId: "version-1",
    state: { variables: [], inputs: [], outputs: [], rootActivity: node("flow", flowchartActivity, {
      activities: [node("a", writer), node("b", writer), node("seq", sequenceActivity, { activities: [node("inner", writer)] })],
      connections: [{ id: "a-b", source: { nodeId: "a", port: "Done" }, target: { nodeId: "b" } }], startNodeId: "a", nodeMetadata: {}, connectionMetadata: {}
    }) },
    layout: [{ nodeId: "a", x: 100, y: 100 }, { nodeId: "inner", x: 50, y: 50 }],
    activityPresentation: [{ nodeId: "seq", displayName: "Inner sequence" }],
    validationErrors: []
  };
}
