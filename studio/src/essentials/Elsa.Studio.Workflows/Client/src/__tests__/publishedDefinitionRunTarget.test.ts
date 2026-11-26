import { beforeEach, describe, expect, it, vi } from "vitest";
import { listExecutables } from "../api/runtime";
import type { WorkflowExecutableReference, WorkflowExecutableSummary } from "../workflowTypes";
import { resolvePublishedDefinitionRunTarget } from "../workflow-editor/useExecutableWorkflowRun";

vi.mock("../api/runtime", () => ({ listExecutables: vi.fn() }));

describe("published definition run target", () => {
  beforeEach(() => vi.clearAllMocks());

  it("selects the newest live published reference for the definition and ignores other definitions", async () => {
    vi.mocked(listExecutables).mockResolvedValue([
      executable("older", "definition-1", [reference("older-ref", "definition-1", "2025-01-01T00:00:00Z", "older-version")]),
      executable("newer", "definition-1", [reference("newer-ref", "definition-1", "2026-01-01T00:00:00Z", "newer-version")]),
      executable("unrelated", "definition-2", [reference("unrelated-ref", "definition-2", "2027-01-01T00:00:00Z")])
    ]);

    await expect(resolvePublishedDefinitionRunTarget({} as never, "definition-1")).resolves.toEqual({
      artifactId: "newer",
      definitionVersionId: "newer-version",
      sourceReferenceId: "newer-ref"
    });
    expect(listExecutables).toHaveBeenCalledWith(expect.anything(), { scope: "published", includeRetired: false });
  });

  it("uses the matching definition reference when an executable is shared", async () => {
    vi.mocked(listExecutables).mockResolvedValue([
      executable("shared", "definition-2", [
        reference("other-ref", "definition-2", "2027-01-01T00:00:00Z"),
        reference("matching-ref", "definition-1", "2026-01-01T00:00:00Z")
      ])
    ]);

    await expect(resolvePublishedDefinitionRunTarget({} as never, "definition-1")).resolves.toEqual({
      artifactId: "shared",
      definitionVersionId: "matching-version",
      sourceReferenceId: "matching-ref"
    });
  });
});

function executable(artifactId: string, definitionId: string, references: WorkflowExecutableReference[]): WorkflowExecutableSummary {
  return {
    artifactId,
    artifactVersion: "1.0.0",
    artifactHash: "hash",
    definitionId,
    definitionVersionId: "summary-version",
    createdAt: "2025-01-01T00:00:00Z",
    publishedAt: references[0]?.publishedAt,
    rootActivityType: "Start",
    rootActivityVersion: "1.0.0",
    nodeCount: 1,
    resumeTargetCount: 0,
    references
  };
}

function reference(sourceReferenceId: string, definitionId: string, publishedAt: string, definitionVersionId?: string): WorkflowExecutableReference {
  return {
    sourceReferenceId,
    artifactId: "artifact",
    definitionId,
    definitionVersionId: definitionVersionId ?? (definitionId === "definition-1" ? "matching-version" : "other-version"),
    artifactVersion: "1.0.0",
    createdAt: publishedAt,
    publishedAt,
    scope: "Published"
  };
}