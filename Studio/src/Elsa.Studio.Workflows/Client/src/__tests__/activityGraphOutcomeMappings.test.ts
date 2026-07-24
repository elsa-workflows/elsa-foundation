import { describe, expect, it } from "vitest";
import {
  activityGraphImplementationEditorContribution,
  activityGraphSchema2ImplementationEditorContribution
} from "../activityGraphContribution";
import {
  getActivityGraphOutcomeMappingOptions,
  normalizeActivityGraphPayload
} from "../ActivityGraphImplementationEditor";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

describe("Activity Graph schema-2 boundary outcome mappings", () => {
  it("initializes schema 2 with an empty mapping list while preserving the schema-1 manifest", () => {
    expect(activityGraphImplementationEditorContribution.createInitialImplementation()).toEqual({
      payload: {
        rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null },
        variables: [],
        outputMappings: []
      },
      layout: []
    });
    expect(activityGraphSchema2ImplementationEditorContribution.createInitialImplementation()).toEqual({
      payload: {
        rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null },
        variables: [],
        outputMappings: [],
        outcomeMappings: []
      },
      layout: []
    });
  });

  it("preserves valid mappings while discarding malformed mappings during normalization", () => {
    const payload = normalizeActivityGraphPayload({
      rootActivity: rootActivity(),
      variables: [],
      outputMappings: [],
      outcomeMappings: [
        { sourceOutcomeReferenceKey: "approved", boundaryOutcomeReferenceKey: "approved-boundary" },
        { sourceOutcomeReferenceKey: "", boundaryOutcomeReferenceKey: "missing-source" },
        { sourceOutcomeReferenceKey: "missing-boundary", boundaryOutcomeReferenceKey: 42 }
      ]
    });

    expect(payload.outcomeMappings).toEqual([
      { sourceOutcomeReferenceKey: "approved", boundaryOutcomeReferenceKey: "approved-boundary" }
    ]);
  });

  it("offers only stable root-outcome keys and emitted boundary outcomes", () => {
    const options = getActivityGraphOutcomeMappingOptions(rootActivity(), catalogItem(), {
      outcomes: [
        { referenceKey: "approved-boundary", name: "Approved", isEmitted: true },
        { referenceKey: "draft-boundary", name: "Draft", isEmitted: false }
      ]
    });

    expect(options.sourceOutcomes).toEqual([
      { referenceKey: "approved", name: "Approved" },
      { referenceKey: "rejected", name: "Rejected" }
    ]);
    expect(options.boundaryOutcomes).toEqual([
      { referenceKey: "approved-boundary", name: "Approved" }
    ]);
  });
});

function rootActivity(): ActivityNode {
  return {
    nodeId: "root",
    activityVersionId: "decision-v1",
    inputs: [],
    outputs: [],
    structure: null
  };
}

function catalogItem(): ActivityCatalogItem {
  return {
    activityVersionId: "decision-v1",
    activityTypeKey: "acme.decision",
    version: "1.0.0",
    category: "Tests",
    displayName: "Decision",
    executionType: "Action",
    inputs: [],
    outputs: [],
    ports: [
      { type: "outcome", referenceKey: "approved", displayName: "Approved" },
      { type: "outcome", referenceKey: "rejected", name: "Rejected" },
      { type: "flow", name: "unstable-name-only" }
    ]
  };
}
