import { describe, expect, it } from "vitest";
import {
  appendElsa3ReusableImportAnalysis,
  groupElsa3ReusableImportItems,
  isElsa3ReusableImportItemSelectable,
  startElsa3ReusableImportAnalysis,
  summarizeElsa3ReusableImportSelection
} from "../elsa3ReusableImportModel";
import type {
  Elsa3MigrationDiagnostic,
  Elsa3ReusableImportAnalysisPage,
  Elsa3ReusableImportItem
} from "../elsa3ReusableImportTypes";

describe("Elsa 3 reusable import model", () => {
  it("keeps one ordered row per exact source version while paging one immutable plan", () => {
    const first = startElsa3ReusableImportAnalysis(page(0, [
      item("definition-b", "source-b-v2", 2),
      item("definition-a", "source-a-v2", 2)
    ], 2));
    const complete = appendElsa3ReusableImportAnalysis(first, page(2, [
      item("definition-a", "source-a-v1", 1)
    ], null));

    expect(complete.items.map(entry => entry.sourceVersionId)).toEqual([
      "source-a-v1",
      "source-a-v2",
      "source-b-v2"
    ]);
    expect(groupElsa3ReusableImportItems(complete.items).map(group => [
      group.sourceDefinitionId,
      group.versions.length
    ])).toEqual([
      ["definition-a", 2],
      ["definition-b", 1]
    ]);
  });

  it("rejects pages from a changed plan or collection instead of mixing analysis", () => {
    const first = startElsa3ReusableImportAnalysis(page(0, [item("a", "a-v1", 1)], 1));
    expect(() => appendElsa3ReusableImportAnalysis(first, {
      ...page(1, [item("b", "b-v1", 1)], null),
      planId: "changed-plan"
    })).toThrow("does not belong");
    expect(() => appendElsa3ReusableImportAnalysis(first, {
      ...page(2, [item("b", "b-v1", 1)], null),
      offset: 2
    })).toThrow("does not continue");
  });

  it("disables invalid cycle members without blocking a valid unrelated closure summary", () => {
    const cycle = diagnostic("ELS3-REUSABLE-DEPENDENCY-CYCLE", {
      cycle: ["cycle-a-v1", "cycle-b-v1"]
    });
    const validOwner = item("owner", "owner-v1", 1, {
      isReusable: true,
      rewrites: [
        {
          ownerSourceVersionId: "owner-v1",
          nodeId: "reuse",
          targetSourceVersionId: "dependency-v1",
          targetActivityDefinitionVersionId: "activity-version-dependency"
        }
      ]
    });
    const validDependency = item("dependency", "dependency-v1", 1, { isReusable: true });
    const invalidCycle = item("cycle-a", "cycle-a-v1", 1, {
      diagnostics: [cycle],
      canApply: false
    });

    expect(isElsa3ReusableImportItemSelectable(validOwner)).toBe(true);
    expect(isElsa3ReusableImportItemSelectable(invalidCycle)).toBe(false);
    expect(summarizeElsa3ReusableImportSelection(
      [validOwner, validDependency, invalidCycle],
      ["owner-v1", "dependency-v1"]
    )).toEqual({
      exactSourceVersions: 2,
      activityDefinitions: 2,
      activityDefinitionVersions: 2,
      wrapperWorkflows: 2,
      ordinaryWorkflows: 0,
      rewrites: 1
    });
  });
});

function page(offset: number, items: Elsa3ReusableImportItem[], nextOffset: number | null): Elsa3ReusableImportAnalysisPage {
  return {
    collectionHandle: "collection",
    planId: "plan",
    offset,
    limit: 2,
    processed: offset + items.length,
    total: 3,
    processedDiagnostics: 0,
    totalDiagnostics: 0,
    isComplete: nextOffset === null,
    nextOffset,
    items,
    diagnostics: []
  };
}

function item(
  sourceDefinitionId: string,
  sourceVersionId: string,
  sourceVersion: number,
  overrides: Partial<Elsa3ReusableImportItem> = {}
): Elsa3ReusableImportItem {
  return {
    sourceDefinitionId,
    sourceVersionId,
    sourceVersion,
    sourceFingerprint: `fingerprint-${sourceVersionId}`,
    isReusable: false,
    workflowDefinitionId: `workflow-${sourceVersionId}`,
    workflowVersionId: `workflow-version-${sourceVersionId}`,
    activityDefinitionId: null,
    activityDefinitionVersionId: null,
    activityTypeKey: null,
    dependencies: [],
    rewrites: [],
    directStarts: [],
    diagnostics: [],
    canApply: true,
    ...overrides
  };
}

function diagnostic(
  code: string,
  overrides: Partial<Elsa3MigrationDiagnostic> = {}
): Elsa3MigrationDiagnostic {
  return {
    severity: "Error",
    code,
    message: "Invalid exact source version.",
    metadata: {},
    pathSegments: [],
    cycle: [],
    isError: true,
    ...overrides
  };
}
