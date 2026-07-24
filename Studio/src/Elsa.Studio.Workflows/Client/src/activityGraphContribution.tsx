import { lazy } from "react";
import type { StudioActivityDefinitionImplementationEditorContribution } from "@elsa-workflows/studio-sdk";
import { activityGraphDiagnosticFocusEvent, type ActivityGraphDiagnosticFocusEventDetail } from "./activityGraphDiagnosticFocus";

const ActivityGraphImplementationEditor = lazy(() => import("./ActivityGraphImplementationEditor").then(module => ({ default: module.ActivityGraphImplementationEditor })));

export const activityGraphImplementationEditorContribution = createActivityGraphContribution("1");
export const activityGraphSchema2ImplementationEditorContribution = createActivityGraphContribution("2");

function createActivityGraphContribution(schemaVersion: "1" | "2"): StudioActivityDefinitionImplementationEditorContribution {
  return {
    id: `elsa.activity-graph.schema-${schemaVersion}`,
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: schemaVersion,
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
        outputMappings: [],
        ...(schemaVersion === "2" ? { outcomeMappings: [] } : {})
      },
      layout: []
    }),
    focusDiagnosticLocation: ({ location, editorElement }) => {
      if (location.providerKey !== "elsa.activity-graph" || !location.jsonPointer?.startsWith("/")) {
        return {
          kind: "unsupported",
          announcement: "The Activity Graph editor does not support this diagnostic location."
        };
      }
      const focusRoot = editorElement.querySelector<HTMLElement>("[data-provider-diagnostic-focus-root]");
      if (!focusRoot) {
        return {
          kind: "unsupported",
          announcement: "The Activity Graph editor retained this diagnostic but its focus surface is unavailable."
        };
      }
      return new Promise(resolve => {
        const accepted = !focusRoot.dispatchEvent(new CustomEvent<ActivityGraphDiagnosticFocusEventDetail>(
          activityGraphDiagnosticFocusEvent,
          { bubbles: false, cancelable: true, detail: { location, complete: resolve } }
        ));
        if (!accepted) resolve({
          kind: "unsupported",
          announcement: "The Activity Graph editor retained this diagnostic but cannot focus its typed location."
        });
      });
    },
    component: ActivityGraphImplementationEditor
  };
}
