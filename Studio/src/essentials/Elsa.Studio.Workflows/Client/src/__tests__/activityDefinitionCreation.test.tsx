import { describe, expect, it } from "vitest";
import type { StudioActivityDefinitionImplementationEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityProviderAuthoringCapability } from "../activityDefinitionTypes";
import type { ActivityCatalogItem } from "../workflowTypes";
import {
  createGraphInitialImplementation,
  createProviderChoices,
  defaultProviderChoice,
  isActivityGraphOnlyChoice
} from "../ActivityDefinitionCreateDialog";

describe("Activity Definition creation choices", () => {
  it("collapses Activity Graph schema revisions and selects the latest exact Studio-supported schema", () => {
    const choices = createProviderChoices([
      provider("elsa.activity-graph", "Activity Graph", ["1", "2", "3"]),
      provider("contoso.script", "Script", ["1"])
    ], [contribution("elsa.activity-graph", "1"), contribution("elsa.activity-graph", "2"), contribution("contoso.script", "1")]);

    expect(choices).toHaveLength(2);
    expect(choices.find(choice => choice.providerKey === "elsa.activity-graph")).toMatchObject({ schemaVersion: "2" });
    expect(choices.map(choice => choice.label)).toEqual(["Activity Graph", "Script"]);
  });

  it("automatically selects Activity Graph only when it is the sole meaningful implementation type", () => {
    const graphOnly = createProviderChoices([provider("elsa.activity-graph", "Activity Graph", ["1", "2"])], [contribution("elsa.activity-graph", "2")]);

    expect(isActivityGraphOnlyChoice(graphOnly)).toBe(true);
    expect(defaultProviderChoice(graphOnly)).toMatchObject({ providerKey: "elsa.activity-graph", schemaVersion: "2" });
    expect(defaultProviderChoice(createProviderChoices([provider("contoso.script", "Script", ["1"])], [contribution("contoso.script", "1")]))).toBeNull();
  });

  it.each([
    ["flowchart", "flowchart-v1"],
    ["sequence", "sequence-v1"],
    ["bpmn", "bpmn-v1"]
  ] as const)("creates a %s graph root from the shared composition template", (kind, expectedVersion) => {
    const implementation = createGraphInitialImplementation(emptyImplementation(), catalog(), expectedVersion);

    expect(implementation?.payload).toMatchObject({
      rootActivity: { nodeId: "root", activityVersionId: expectedVersion }
    });
  });
});

function provider(providerKey: string, displayName: string, schemas: string[]): ActivityProviderAuthoringCapability {
  return {
    providerKey,
    displayName,
    manifestSchemas: schemas.map(schemaVersion => ({ schemaVersion, isAuthorable: true, migratableFromSchemaVersions: [] })),
    requiredOutcomes: []
  };
}

function contribution(providerKey: string, providerSchemaVersion: string): StudioActivityDefinitionImplementationEditorContribution {
  return {
    id: `${providerKey}.${providerSchemaVersion}`,
    providerKey,
    providerSchemaVersion,
    createInitialImplementation: emptyImplementation,
    component: () => null
  };
}

function emptyImplementation() {
  return { payload: { variables: [], outputMappings: [], outcomeMappings: [] }, layout: [] };
}

function catalog(): ActivityCatalogItem[] {
  return [
    activity("flowchart-v1", "Elsa.Flowchart", "Flowchart"),
    activity("sequence-v1", "Elsa.Sequence", "Sequence"),
    activity("bpmn-v1", "Elsa.BpmnProcess", "BPMN Process")
  ];
}

function activity(activityVersionId: string, activityTypeKey: string, displayName: string): ActivityCatalogItem {
  return {
    activityVersionId,
    activityTypeKey,
    displayName,
    version: "1.0.0",
    category: "Control flow",
    executionType: "Action",
    inputs: [],
    outputs: [],
    authoringTemplate: { nodeId: "template", activityVersionId, inputs: [], outputs: [], structure: null }
  };
}
