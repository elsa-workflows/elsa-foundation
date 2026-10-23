import { describe, expect, it } from "vitest";
import type { StudioActivityDefinitionImplementationEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityProviderAuthoringCapability } from "../activityDefinitionTypes";
import type { ActivityCatalogItem } from "../workflowTypes";
import {
  createGraphInitialImplementation,
  createGraphInitialState,
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

  it("selects the sole usable provider without choosing among multiple usable providers", () => {
    const graphOnly = createProviderChoices([provider("elsa.activity-graph", "Activity Graph", ["1", "2"])], [contribution("elsa.activity-graph", "2")]);
    const scriptOnly = createProviderChoices([provider("contoso.script", "Script", ["1"])], [contribution("contoso.script", "1")]);
    const unavailable = createProviderChoices([provider("contoso.missing", "Missing editor", ["1"])], []);

    expect(isActivityGraphOnlyChoice(graphOnly)).toBe(true);
    expect(defaultProviderChoice(graphOnly)).toMatchObject({ providerKey: "elsa.activity-graph", schemaVersion: "2" });
    expect(defaultProviderChoice(scriptOnly)).toMatchObject({ providerKey: "contoso.script", schemaVersion: "1" });
    expect(defaultProviderChoice([...graphOnly, ...scriptOnly])).toBeNull();
    expect(defaultProviderChoice(unavailable)).toBeNull();
  });

  it.each([
    ["flowchart", "flowchart-v1"],
    ["sequence", "sequence-v1"],
    ["bpmn", "bpmn-v1"]
  ] as const)("creates a %s graph root from the shared composition template", (kind, expectedVersion) => {
    const implementation = createGraphInitialImplementation(emptyImplementation(), catalog(), expectedVersion);

    expect(implementation?.payload).toMatchObject({
      rootActivity: { nodeId: "root", activityVersionId: expectedVersion, inputs: [], outputs: [] }
    });
  });

  it("preserves canonical template bindings when initializing the graph", () => {
    const descriptor = catalog()[1];
    const binding = { referenceKey: "option", value: { expressionType: "Literal", value: true }, vendorMetadata: "keep" };
    descriptor.authoringTemplate!.inputs = [binding];
    const initialized = createGraphInitialState(emptyImplementation(), [descriptor], descriptor.activityVersionId, []);
    expect((initialized?.implementation.payload as { rootActivity: { inputs: unknown[] } }).rootActivity.inputs).toEqual([binding]);
    expect(createGraphInitialState(emptyImplementation(), [descriptor], "missing-exact-version", [])).toBeNull();
  });

  it("seeds only an advertised Done outcome and maps its exact root key", () => {
    const initialized = createGraphInitialState(emptyImplementation(), catalog(), "sequence-v1", []);

    expect(initialized?.outcomes).toEqual([
      { referenceKey: "sequence.done.source-key", name: "Done", isEmitted: true, description: null }
    ]);
    expect((initialized?.implementation.payload as { outcomeMappings: unknown[] }).outcomeMappings).toEqual([
      { sourceOutcomeReferenceKey: "sequence.done.source-key", boundaryOutcomeReferenceKey: "sequence.done.source-key" }
    ]);
  });

  it("derives the exact public identity and mapping when the root exposes one noncanonical outcome", () => {
    const root = activity("single-outcome-v1", "Acme.SingleOutcome", "Single outcome", [
      { type: "outcome", referenceKey: "opaque/vendor-complete/v2", displayName: "Finished" }
    ]);
    const initialized = createGraphInitialState(emptyImplementation(), [root], root.activityVersionId, []);

    expect(initialized?.outcomes).toEqual([
      { referenceKey: "opaque/vendor-complete/v2", name: "Finished", isEmitted: true, description: null }
    ]);
    expect((initialized?.implementation.payload as { outcomeMappings: unknown[] }).outcomeMappings).toEqual([
      { sourceOutcomeReferenceKey: "opaque/vendor-complete/v2", boundaryOutcomeReferenceKey: "opaque/vendor-complete/v2" }
    ]);
  });

  it("does not guess between multiple root outcomes when Done is not advertised", () => {
    const root = activity("ambiguous-outcomes-v1", "Acme.Decision", "Decision", [
      { type: "outcome", referenceKey: "approved-source", displayName: "Approved" },
      { type: "outcome", referenceKey: "rejected-source", displayName: "Rejected" }
    ]);

    const initialized = createGraphInitialState(emptyImplementation(), [root], root.activityVersionId, []);

    expect(initialized?.outcomes).toEqual([]);
    expect((initialized?.implementation.payload as { outcomeMappings: unknown[] }).outcomeMappings).toEqual([]);
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
    activity("sequence-v1", "Elsa.Sequence", "Sequence", [
      { type: "outcome", referenceKey: "sequence.done.source-key", displayName: "Done" },
      { type: "outcome", referenceKey: "sequence.break.source-key", displayName: "Break" }
    ]),
    activity("bpmn-v1", "Elsa.BpmnProcess", "BPMN Process")
  ];
}

function activity(activityVersionId: string, activityTypeKey: string, displayName: string, ports: unknown[] = []): ActivityCatalogItem {
  return {
    activityVersionId,
    activityTypeKey,
    displayName,
    version: "1.0.0",
    category: "Control flow",
    executionType: "Action",
    inputs: [],
    outputs: [],
    ports,
    authoringTemplate: { nodeId: "template", activityVersionId, inputs: [], outputs: [], structure: null }
  };
}
