import { lazy } from "react";
import type { StudioActivityDefinitionImplementationEditorContribution } from "@elsa-workflows/studio-sdk";

const ActivityGraphImplementationEditor = lazy(() => import("./ActivityGraphImplementationEditor").then(module => ({ default: module.ActivityGraphImplementationEditor })));

export const activityGraphImplementationEditorContribution: StudioActivityDefinitionImplementationEditorContribution = {
  id: "elsa.activity-graph.schema-1",
  providerKey: "elsa.activity-graph",
  providerSchemaVersion: "1",
  createInitialImplementation: () => ({
    payload: {
      rootActivity: {
        nodeId: "root",
        activityVersionId: "",
        inputs: [],
        outputs: [],
        structure: null
      },
      variables: [],
      outputMappings: []
    },
    layout: []
  }),
  component: ActivityGraphImplementationEditor
};
