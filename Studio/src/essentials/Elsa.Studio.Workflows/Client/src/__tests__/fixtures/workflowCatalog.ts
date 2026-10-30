import type { ActivityCatalogItem } from "../../workflowTypes";

export function flowchartActivity(): ActivityCatalogItem {
  return {
    activityVersionId: "flowchart-v1", activityTypeKey: "Elsa.Activities.Flowchart", version: "1.0.0",
    category: "Composition", displayName: "Flowchart", executionType: "Action", inputs: [], outputs: [],
    authoringTemplate: {
      nodeId: "root", activityVersionId: "flowchart-v1", inputs: [], outputs: [],
      structure: { kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: { activities: [], connections: [] } }
    }
  };
}
