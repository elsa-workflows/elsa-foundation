import { describe, expect, it, vi } from "vitest";
import type { DefinitionListRequest } from "../api/workflowDesign";
import type {
  WorkflowDefinitionSummary,
  WorkflowDefinitionsResponse,
  WorkflowExecutableReference,
  WorkflowExecutableSummary
} from "../workflowTypes";
import {
  executableMatchesDefinitionSearch,
  getExecutableDefinitionIds,
  loadDefinitionNames
} from "../workflow-editor/executableDefinitionNames";

function definition(id: string, name: string): WorkflowDefinitionSummary {
  return {
    id,
    name,
    createdAt: "2026-10-18T00:00:00Z",
    lastModifiedAt: "2026-10-18T00:00:00Z",
    versionCount: 1
  };
}

function executableReference(overrides: Partial<WorkflowExecutableReference> = {}): WorkflowExecutableReference {
  return {
    sourceReferenceId: "source-reference-1",
    artifactId: "artifact-1",
    definitionId: "definition-reference",
    definitionVersionId: "definition-version-reference",
    artifactVersion: "1",
    createdAt: "2026-10-18T00:00:00Z",
    scope: "Published",
    ...overrides
  };
}

function executable(overrides: Partial<WorkflowExecutableSummary> = {}): WorkflowExecutableSummary {
  return {
    artifactId: "artifact-1",
    artifactVersion: "1",
    artifactHash: "sha256:artifact-hash",
    definitionId: "definition-root",
    definitionVersionId: "definition-version-root",
    createdAt: "2026-10-18T00:00:00Z",
    rootActivityType: "Elsa.Workflows.TestActivity",
    rootActivityVersion: "1",
    nodeCount: 1,
    resumeTargetCount: 0,
    references: [],
    ...overrides
  };
}

function page(
  definitions: WorkflowDefinitionSummary[],
  pageNumber: number,
  nextContinuationToken: string | null
): WorkflowDefinitionsResponse {
  return { definitions, page: pageNumber, pageSize: 100, nextContinuationToken, isPaged: true };
}

describe("executable definition names", () => {
  it("collects unique root and reference definition IDs without treating source or version IDs as definitions", () => {
    const row = executable({
      definitionId: "definition-root",
      definitionVersionId: "version-root",
      sourceId: "source-root",
      references: [
        executableReference({
          definitionId: "definition-reference",
          definitionVersionId: "version-reference",
          sourceId: "source-reference"
        }),
        executableReference({ definitionId: "definition-root" }),
        executableReference({ definitionId: " " })
      ]
    });

    expect(getExecutableDefinitionIds(row)).toEqual(["definition-root", "definition-reference"]);
  });

  it("loads duplicate display names by ID across continuation pages and stops after resolving all targets", async () => {
    const listPage = vi.fn(async (request: DefinitionListRequest): Promise<WorkflowDefinitionsResponse> => {
      if (request.continuationToken === undefined) {
        return page([definition("definition-root", "Order workflow")], 1, "opaque-next-page");
      }

      return page([definition("definition-reference", "Order workflow")], 2, "unneeded-third-page");
    });

    const names = await loadDefinitionNames(
      ["definition-root", "definition-reference", "definition-root"],
      listPage
    );

    expect([...names]).toEqual([
      ["definition-root", "Order workflow"],
      ["definition-reference", "Order workflow"]
    ]);
    expect(listPage).toHaveBeenNthCalledWith(1, { search: "", state: "all", page: 1, pageSize: 100, includeAllWhenUnpaged: true });
    expect(listPage).toHaveBeenNthCalledWith(2, {
      search: "",
      state: "all",
      page: 2,
      pageSize: 100,
      includeAllWhenUnpaged: true,
      continuationToken: "opaque-next-page"
    });
    expect(listPage).toHaveBeenCalledTimes(2);
  });

  it("keeps names found before exhaustion or a later page failure", async () => {
    const pagedLookup = vi.fn(async (request: DefinitionListRequest): Promise<WorkflowDefinitionsResponse> =>
      request.continuationToken === undefined
        ? page([definition("definition-root", "Order workflow")], 1, "missing-page")
        : page([], 2, null));

    const partialNames = await loadDefinitionNames(["definition-root", "missing-definition"], pagedLookup);
    expect([...partialNames]).toEqual([["definition-root", "Order workflow"]]);
    expect(pagedLookup).toHaveBeenCalledTimes(2);

    const laterFailure = vi.fn(async (request: DefinitionListRequest): Promise<WorkflowDefinitionsResponse> => {
      if (request.continuationToken) throw new Error("Temporary failure");
      return page([definition("definition-root", "Order workflow")], 1, "next-page");
    });
    expect([...await loadDefinitionNames(["definition-root", "missing-definition"], laterFailure)]).toEqual([
      ["definition-root", "Order workflow"]
    ]);
    expect(laterFailure).toHaveBeenCalledTimes(2);

    const deniedLookup = vi.fn(async (): Promise<WorkflowDefinitionsResponse> => {
      throw new Error("Forbidden");
    });
    expect([...await loadDefinitionNames(["definition-root"], deniedLookup)]).toEqual([]);
    expect(deniedLookup).toHaveBeenCalledTimes(1);
  });

  it("uses the single full legacy response and treats blank names as resolved IDs", async () => {
    const legacy = vi.fn(async (): Promise<WorkflowDefinitionsResponse> => ({
      definitions: [definition("definition-first", "First workflow"), definition("definition-root", "Legacy workflow")],
      page: 1, pageSize: 100, totalCount: 2
    }));
    expect([...await loadDefinitionNames(["definition-root"], legacy)]).toEqual([["definition-root", "Legacy workflow"]]);
    expect(legacy).toHaveBeenCalledTimes(1);

    const blankName = vi.fn(async (): Promise<WorkflowDefinitionsResponse> => ({
      ...page([definition("definition-blank", "   ")], 1, "next-page")
    }));
    expect([...await loadDefinitionNames(["definition-blank"], blankName)]).toEqual([]);
    expect(blankName).toHaveBeenCalledTimes(1);
  });

  it("caps unresolved paged name lookups", async () => {
    const bounded = vi.fn(async (request: DefinitionListRequest): Promise<WorkflowDefinitionsResponse> =>
      page([], request.page, `next-${request.page}`));

    expect([...await loadDefinitionNames(["missing-definition"], bounded)]).toEqual([]);
    expect(bounded).toHaveBeenCalledTimes(5);
  });

  it("stops on repeated continuation tokens", async () => {
    const repeated = vi.fn(async (): Promise<WorkflowDefinitionsResponse> => page([], 1, "same-token"));
    expect([...await loadDefinitionNames(["missing"], repeated)]).toEqual([]);
    expect(repeated).toHaveBeenCalledTimes(2);
  });

  it("matches legacy flat fields, resolved definition names, and reference-only identities", () => {
    const row = executable({
      sourceVersion: "source-version-root",
      references: [executableReference({
        sourceReferenceId: "reference-record-id",
        definitionId: "definition-reference",
        definitionVersionId: "definition-version-reference",
        sourceId: "source-reference-id",
        sourceVersion: "source-version-reference"
      })]
    });
    const names = new Map([
      ["definition-root", "Nightly order processing"],
      ["definition-reference", "Invoice archive"]
    ]);

    expect(executableMatchesDefinitionSearch(row, "NIGHTLY ORDER", names)).toBe(true);
    expect(executableMatchesDefinitionSearch(row, "invoice archive", names)).toBe(true);
    expect(executableMatchesDefinitionSearch(row, "source-version-root", names)).toBe(true);
    expect(executableMatchesDefinitionSearch(row, "definition-version-reference", names)).toBe(true);
    expect(executableMatchesDefinitionSearch(row, "source-reference-id", names)).toBe(true);
    expect(executableMatchesDefinitionSearch(row, "source-version-reference", names)).toBe(true);
    expect(executableMatchesDefinitionSearch(row, "unrelated-workflow", names)).toBe(false);
  });
});
