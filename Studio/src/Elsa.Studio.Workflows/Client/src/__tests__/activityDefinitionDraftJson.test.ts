import { describe, expect, it } from "vitest";
import type { ActivityDefinitionDraftView } from "../activityDefinitionTypes";
import {
  applyActivityDefinitionDraftJson,
  serializeActivityDefinitionDraftJson
} from "../activityDefinitionDraftJson";

describe("Activity Definition authoring draft JSON", () => {
  it("serializes only editable authoring fields", () => {
    const serialized = JSON.parse(serializeActivityDefinitionDraftJson(draft()));

    expect(serialized).toEqual({
      presentationLabel: "Working copy",
      contract: expect.objectContaining({ contractSchemaVersion: "1" }),
      implementation: {
        providerKey: "elsa.activity-graph",
        schemaVersion: "2",
        payload: expect.objectContaining({ extension: { keep: true } })
      },
      layout: [{ nodeId: "child", data: { x: 10, y: 20, extension: "keep" } }]
    });
    expect(serialized).not.toHaveProperty("draftId");
    expect(serialized).not.toHaveProperty("revision");
    expect(serialized).not.toHaveProperty("validation");
    expect(serialized).not.toHaveProperty("createdAt");
  });

  it("rejects invalid JSON and provider identity changes without changing the draft", () => {
    expect(applyActivityDefinitionDraftJson(draft(), "{")).toEqual({
      error: "Activity Definition JSON must be valid JSON."
    });

    const projection = JSON.parse(serializeActivityDefinitionDraftJson(draft()));
    projection.implementation.schemaVersion = "1";
    expect(applyActivityDefinitionDraftJson(draft(), JSON.stringify(projection))).toEqual({
      error: "Provider changes require the explicit provider migration workflow."
    });
  });

  it("preserves missing existing layout and safely places new graph nodes", () => {
    const current = draft();
    const projection = JSON.parse(serializeActivityDefinitionDraftJson(current));
    projection.implementation.payload.rootActivity.structure.payload.activities.push({
      nodeId: "new-child",
      activityVersionId: "write-line-v1",
      inputs: [],
      outputs: [],
      structure: null
    });
    projection.layout = [];

    const result = applyActivityDefinitionDraftJson(current, JSON.stringify(projection));
    expect(result).toHaveProperty("draft");
    if (!("draft" in result)) return;
    expect(result.draft.layout).toContainEqual({
      nodeId: "child",
      data: { x: 10, y: 20, extension: "keep" }
    });
    expect(result.draft.layout).toContainEqual({
      nodeId: "new-child",
      data: { x: 320, y: 80 }
    });
    expect(result.draft.revision).toBe(current.revision);
    expect(result.draft.draftId).toBe(current.draftId);
  });
});

function draft(): ActivityDefinitionDraftView {
  return {
    draftId: "draft-1",
    definitionId: "definition-1",
    revision: 7,
    status: "Draft",
    contract: {
      contractSchemaVersion: "1",
      inputs: [],
      outputs: [],
      outcomes: []
    },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "2",
      manifestFingerprint: "sha256:one",
      payload: {
        rootActivity: {
          nodeId: "root",
          activityVersionId: "sequence-v1",
          inputs: [],
          outputs: [],
          structure: {
            kind: "elsa.sequence.structure",
            schemaVersion: "1",
            payload: {
              activities: [{
                nodeId: "child",
                activityVersionId: "write-line-v1",
                inputs: [],
                outputs: [],
                structure: null
              }]
            }
          }
        },
        variables: [],
        outputMappings: [],
        outcomeMappings: [],
        extension: { keep: true }
      }
    },
    layout: [{ nodeId: "child", data: { x: 10, y: 20, extension: "keep" } }],
    validation: {
      draftId: "draft-1",
      revision: 7,
      isValid: true,
      validatedAt: "2026-07-28T00:00:00Z",
      diagnostics: []
    },
    createdAt: "2026-07-28T00:00:00Z",
    updatedAt: "2026-07-28T00:00:00Z",
    presentationLabel: "Working copy"
  };
}
