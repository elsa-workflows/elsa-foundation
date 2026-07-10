import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

// Shared activity-catalog fixtures for workflow-editor tests. The facet shapes mirror the REAL
// backend attributes (ClrAssemblyScanner over [ActivityStructure]/[ActivityChildSlot]): ForEach
// declares no Mode so its facet mode is "generic"; Flowchart/Sequence declare canvas modes. Tests
// that deliberately exercise legacy or facetless shapes (e.g. workflowAdapter.test.ts's fallback
// paths) keep local variants instead of importing these.

export const writeLine: ActivityCatalogItem = {
  activityVersionId: "activity-write-line-v1",
  activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
  version: "1.0.0",
  category: "Primitives",
  displayName: "Write Line",
  description: null,
  executionType: "Action",
  inputs: [],
  outputs: [],
  designFacets: []
};

export const flowchartActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-flowchart-v1",
  activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
  category: "Composition",
  displayName: "Flowchart",
  designFacets: [{
    kind: "elsa.flowchart.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "flowchart",
      supportsScopedVariables: true,
      slots: [{ name: "Flowchart.Activities", property: "activities", displayName: "Activities", cardinality: "many" }],
      initialPayload: { activities: [], connections: [], startNodeId: null, nodeMetadata: {}, connectionMetadata: {} }
    }
  }]
};

export const sequenceActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-sequence-v1",
  activityTypeKey: "Elsa.Activities.Sequence.Activities.Sequence",
  category: "Composition",
  displayName: "Sequence",
  designFacets: [{
    kind: "elsa.sequence.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "sequence",
      supportsScopedVariables: true,
      slots: [{ name: "Sequence.Activities", property: "activities", displayName: "Activities", cardinality: "many" }],
      initialPayload: { activities: [] }
    }
  }]
};

export const forEachActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-for-each-v1",
  activityTypeKey: "Elsa.Activities.ForEach.Activities.ForEach",
  category: "Control Flow",
  displayName: "For Each",
  designFacets: [{
    kind: "elsa.foreach.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "generic",
      supportsScopedVariables: true,
      slots: [{ name: "ForEach.Body", property: "body", displayName: "Body", cardinality: "single" }],
      initialPayload: { body: null }
    }
  }]
};

export function leafNode(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] };
}

export function flowchartNode(nodeId: string, activities: ActivityNode[]): ActivityNode {
  return {
    nodeId,
    activityVersionId: flowchartActivity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: {
      kind: "elsa.flowchart.structure",
      schemaVersion: "1.0.0",
      payload: { activities, connections: [], startNodeId: null, nodeMetadata: {}, connectionMetadata: {} }
    }
  };
}

export function sequenceNode(nodeId: string, activities: ActivityNode[]): ActivityNode {
  return {
    nodeId,
    activityVersionId: sequenceActivity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities } }
  };
}

export function forEachNode(nodeId: string, body: ActivityNode | null): ActivityNode {
  return {
    nodeId,
    activityVersionId: forEachActivity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: { kind: "elsa.foreach.structure", schemaVersion: "1.0.0", payload: { body } }
  };
}
