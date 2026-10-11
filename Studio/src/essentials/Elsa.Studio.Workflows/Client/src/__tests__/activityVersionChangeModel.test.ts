import { describe, expect, it } from "vitest";
import type { ActivityDefinitionVersionView } from "../activityDefinitionTypes";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import type { ActivityVersionDiffView } from "../api/activityVersionChange";
import {
  analyzeActivityVersionChange,
  applyActivityVersionChange,
  createActivityVersionChangePrecondition,
  findActivityOccurrence,
  validateActivityVersionChangePrecondition
} from "../workflow-editor/activityVersionChangeModel";

const oldVersion = version("version-1", "1.0.0", ["Keep", "Remove", "Change"], ["Done", "Removed"]);
const newVersion = version("version-2", "2.0.0", ["Keep", "Change"], ["Done"]);
const diff: ActivityVersionDiffView = {
  from: { kind: "Version", definitionId: "activity-def", versionId: "version-1", version: "1.0.0" },
  to: { kind: "Version", definitionId: "activity-def", versionId: "version-2", version: "2.0.0" },
  compatibility: "Breaking",
  requiredBump: "Major",
  behaviorChanged: true,
  provider: { changed: false },
  summary: { breaking: 3, additive: 0, nonBehavioral: 0, warnings: 0 },
  changes: [
    change("remove-input", "Input", "Remove", "Breaking"),
    change("change-input", "Input", "Change", "Breaking"),
    change("remove-outcome", "Outcome", "Removed", "Breaking")
  ],
  diagnostics: []
};

describe("activity version change model", () => {
  it("changes only the selected occurrence by default and keeps authored values untouched", () => {
    const draft = makeDraft();
    const next = applyActivityVersionChange(draft, "first", "version-1", "version-2", "occurrence");

    expect(findActivityOccurrence(next.state.rootActivity, "first")?.activityVersionId).toBe("version-2");
    expect(findActivityOccurrence(next.state.rootActivity, "second")?.activityVersionId).toBe("version-1");
    expect(findActivityOccurrence(next.state.rootActivity, "first")?.keep).toEqual({ expression: { type: "Literal", value: "kept" } });
  });

  it("changes every exact matching occurrence without touching other versions", () => {
    const draft = makeDraft();
    const next = applyActivityVersionChange(draft, "first", "version-1", "version-2", "matching");

    expect(findActivityOccurrence(next.state.rootActivity, "first")?.activityVersionId).toBe("version-2");
    expect(findActivityOccurrence(next.state.rootActivity, "second")?.activityVersionId).toBe("version-2");
    expect(findActivityOccurrence(next.state.rootActivity, "other")?.activityVersionId).toBe("other-version");
  });

  it("retains only compatible stable keys and exposes removed or incompatible work", () => {
    const draft = makeDraft();
    const occurrence = findActivityOccurrence(draft.state.rootActivity, "first")!;
    const impact = analyzeActivityVersionChange(draft, occurrence, oldVersion, newVersion, diff, "matching");

    expect(impact.occurrenceIds).toEqual(["first", "second"]);
    expect(impact.preservedBindingKeys).toEqual(["Keep"]);
    expect(impact.unresolvedBindingKeys).toEqual(["Change", "Remove"]);
    expect(impact.preservedOutcomeKeys).toEqual(["Done"]);
    expect(impact.unresolvedOutcomeKeys).toEqual(["Removed"]);
  });

  it("rejects a stale draft or occurrence precondition without producing an edit", () => {
    const draft = makeDraft();
    const occurrence = findActivityOccurrence(draft.state.rootActivity, "first")!;
    const precondition = createActivityVersionChangePrecondition(draft, occurrence);
    const edited = {
      ...draft,
      state: { ...draft.state, strategyOptions: { changed: true } }
    };

    expect(validateActivityVersionChangePrecondition(draft, precondition)).toBeNull();
    expect(validateActivityVersionChangePrecondition(edited, precondition)).toContain("local work was kept");
  });
});

function makeDraft(): WorkflowDraft {
  const first = activity("first", "version-1");
  first.keep = { expression: { type: "Literal", value: "kept" } };
  first.remove = { expression: { type: "Literal", value: "unresolved" } };
  first.change = { expression: { type: "Literal", value: "incompatible" } };
  const root: ActivityNode = {
    nodeId: "root",
    activityVersionId: "flowchart",
    inputs: [],
    outputs: [],
    structure: {
      kind: "Flowchart",
      schemaVersion: "1",
      payload: {
        activities: [first, activity("second", "version-1"), activity("other", "other-version")],
        connections: [
          { id: "done", source: { nodeId: "first", port: "Done" }, target: { nodeId: "other" } },
          { id: "removed", source: { nodeId: "first", port: "Removed" }, target: { nodeId: "second" } }
        ]
      }
    }
  };
  return {
    id: "draft-1",
    definitionId: "workflow-1",
    sourceVersionId: "published-source",
    state: { rootActivity: root },
    layout: [],
    validationErrors: []
  };
}

function activity(nodeId: string, activityVersionId: string): ActivityNode {
  return { nodeId, activityVersionId, inputs: [], outputs: [] };
}

function version(
  versionId: string,
  semanticVersion: string,
  inputs: string[],
  outcomes: string[]
): ActivityDefinitionVersionView {
  return {
    definition: {
      definitionId: "activity-def",
      activityTypeKey: "Contoso.Activity",
      category: "Contoso",
      displayName: "Activity",
      contentAuthority: { kind: "Design", authorityKey: "Design" }
    },
    versionId,
    version: semanticVersion,
    contract: {
      contractSchemaVersion: "1",
      inputs: inputs.map(referenceKey => ({
        referenceKey,
        name: referenceKey,
        type: { alias: "String", collectionKind: "None" },
        isRequired: false,
        isNullable: true,
        default: null,
        storageDriverKey: "Workflow",
        durability: "Durable"
      })),
      outputs: [],
      outcomes: outcomes.map(referenceKey => ({ referenceKey, name: referenceKey, isEmitted: true }))
    },
    provider: { providerKey: "ActivityGraph", schemaVersion: "1", manifestFingerprint: "hash" },
    lifecycle: "Active",
    publishedAt: "2026-07-19T00:00:00Z"
  };
}

function change(
  changeId: string,
  memberKind: string,
  referenceKey: string,
  impact: string
) {
  return {
    changeId,
    area: "Contract",
    kind: "Changed",
    subject: { memberKind, referenceKey },
    impact,
    requiredBump: "Major",
    message: `${referenceKey} changed`
  };
}
